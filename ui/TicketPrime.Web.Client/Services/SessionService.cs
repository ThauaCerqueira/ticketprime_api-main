using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace TicketPrime.Web.Client.Services;

/// <summary>
/// Gerencia a sessão do usuário.
///
/// ═══════════════════════════════════════════════════════════════════
/// SEGURANÇA: NÃO armazena refresh token em localStorage.
///
/// ANTES (vulnerável):
///   - Refresh token em localStorage (tp-refresh-token)
///   - Se um XSS fosse descoberto, o atacante roubava o refresh token
///     e conseguia renovar a sessão por até 30 dias — mesmo após a
///     troca de senha.
///
/// AGORA (seguro):
///   - Refresh token DEFINIDO EXCLUSIVAMENTE como cookie httpOnly
///     (ticketprime_refresh) com Secure e SameSite=Strict.
///   - JavaScript NÃO CONSEGUE ler o cookie — imune a XSS.
///   - O cookie é enviado automaticamente nas requisições fetch.
///   - Apenas dados não-sensíveis (nome, perfil) ficam em localStorage
///     para exibição na UI.
/// ═══════════════════════════════════════════════════════════════════
/// </summary>
public class SessionService
{
    private readonly IJSRuntime _js;
    private readonly IHttpClientFactory _httpClientFactory;
    private static string? _sharedToken;
    private readonly SemaphoreSlim _carregarLock = new(1, 1);
    private bool _carregamentoInicialConcluido;

    // ══════════════════════════════════════════════════════════════
    // ANTES: tp-refresh-token era armazenado aqui.
    // AGORA: Só armazenamos dados de exibição (nome, perfil).
    // O refresh token vive apenas no cookie httpOnly.
    // ══════════════════════════════════════════════════════════════
    private const string KeyUserInfo = "tp-user-info";

    public string? Cpf { get; private set; }
    public string? Nome { get; private set; }
    public string? Perfil { get; private set; }
    public string? Token
    {
        get => _sharedToken;
        private set => _sharedToken = value;
    }
    public bool EstaLogado => !string.IsNullOrEmpty(Token);
    public bool EhAdmin => Perfil == "ADMIN";

    /// <summary>
    /// Indica que o usuário está autenticado com senha temporária e DEVE trocar a senha.
    /// antes de acessar qualquer outra funcionalidade.
    /// </summary>
    public bool DeveTrocarSenha { get; private set; }

    public void MarcarSenhaTemporaria() => DeveTrocarSenha = true;
    public void LimparSenhaTemporaria() => DeveTrocarSenha = false;

    public event Action? OnChange;

    public SessionService(IJSRuntime js, IHttpClientFactory httpClientFactory)
    {
        _js = js;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Carrega a sessão. Tenta renovar o JWT automaticamente via cookie httpOnly.
    /// O cookie ticketprime_refresh é enviado automaticamente pelo navegador
    /// na requisição POST /api/auth/refresh — sem necessidade de localStorage.
    /// </summary>
    public async Task CarregarAsync(bool forcarRefresh = false)
    {
        if (_carregamentoInicialConcluido && !forcarRefresh)
            return;

        await _carregarLock.WaitAsync();
        try
        {
            if (_carregamentoInicialConcluido && !forcarRefresh)
                return;

            var infoResult = await GetLocalStorageAsync(KeyUserInfo);

            // Evita POST /api/auth/refresh em visitante anônimo.
            // Sem indício de sessão persistida, não há cookie útil para renovar.
            if (!forcarRefresh && string.IsNullOrWhiteSpace(infoResult) && string.IsNullOrWhiteSpace(Token))
            {
                Cpf = null;
                Nome = null;
                Perfil = null;
                return;
            }

            // ══════════════════════════════════════════════════════════════
            // SEGURANÇA: NÃO lê refresh token do localStorage.
            // O cookie httpOnly ticketprime_refresh é enviado
            // AUTOMATICAMENTE na requisição para /api/auth/refresh.
            // ══════════════════════════════════════════════════════════════
            var client   = _httpClientFactory.CreateClient("TicketPrimeApi");
            var response = await client.PostAsJsonAsync("api/auth/refresh", new { });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RefreshResponse>();
                if (result != null && !string.IsNullOrEmpty(result.Token))
                {
                    Token  = result.Token;

                    // Tenta recuperar info do usuário do localStorage (apenas nome/perfil)
                    if (!string.IsNullOrEmpty(infoResult))
                    {
                        var userInfo = JsonSerializer.Deserialize<UserInfoData>(infoResult);
                        if (userInfo != null)
                        {
                            Cpf    = userInfo.Cpf;
                            Nome   = userInfo.Nome;
                            Perfil = userInfo.Perfil;
                        }
                    }

                    // Mantém a flag de senha temporária mesmo após F5
                    if (result.SenhaTemporaria)
                        DeveTrocarSenha = true;

                    NotificarMudanca();
                    return;
                }
            }

            // Refresh falhou (cookie expirado ou inexistente) — limpa tudo
            await LimparStorageAsync();
        }
        catch
        {
            // IJSRuntime ou HTTP podem lançar durante pré-renderização
            await LimparStorageAsync();
        }
        finally
        {
            _carregamentoInicialConcluido = true;
            _carregarLock.Release();
            NotificarMudanca();
        }
    }

    /// <summary>
    /// Define a sessão com os dados do login bem-sucedido.
    /// Persiste apenas dados não-sensíveis (nome, perfil) no localStorage
    /// para exibição na UI. O refresh token fica exclusivamente no cookie httpOnly.
    /// </summary>
    public async Task LogarAsync(string cpf, string nome, string perfil, string token)
    {
        Cpf    = cpf;
        Nome   = nome;
        Perfil = perfil;
        Token  = token;

        // Persiste apenas dados de exibição (nome, perfil) no localStorage
        var userInfo     = new UserInfoData { Cpf = cpf, Nome = nome, Perfil = perfil };
        var userInfoJson = JsonSerializer.Serialize(userInfo);

        try
        {
            await SetLocalStorageAsync(KeyUserInfo, userInfoJson);
        }
        catch
        {
            // localStorage pode falhar na primeira renderização — sessão segue funcional
        }

        _carregamentoInicialConcluido = true;
        NotificarMudanca();
    }

    /// <summary>
    /// Limpa a sessão em memória, revoga o refresh token no servidor
    /// e remove os dados persistidos.
    /// </summary>
    public async Task DeslogarAsync()
    {
        // Revoga o refresh token no servidor.
        // O cookie httpOnly ticketprime_refresh é enviado automaticamente
        // pelo navegador na requisição POST /api/auth/logout.
        try
        {
            var client = _httpClientFactory.CreateClient("TicketPrimeApi");
            if (!string.IsNullOrEmpty(Token))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

            // O cookie httpOnly é enviado automaticamente com Path="/"
            await client.PostAsJsonAsync("api/auth/logout", new { });
        }
        catch
        {
            // Falha na revogação server-side não impede o logout local
        }

        Cpf    = null;
        Nome   = null;
        Perfil = null;
        Token  = null;

        await LimparStorageAsync();
        _carregamentoInicialConcluido = true;
        NotificarMudanca();
    }

    // ── localStorage via IJSRuntime (apenas dados não-sensiveis) ──

    private async Task<string?> GetLocalStorageAsync(string key)
    {
        try { return await _js.InvokeAsync<string>("localStorage.getItem", key); }
        catch { return null; }
    }

    private async Task SetLocalStorageAsync(string key, string value)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { }
    }

    private async Task RemoveLocalStorageAsync(string key)
    {
        try { await _js.InvokeVoidAsync("localStorage.removeItem", key); }
        catch { }
    }

    private async Task LimparStorageAsync()
    {
        try
        {
            await RemoveLocalStorageAsync(KeyUserInfo);
        }
        catch { }
    }

    private void NotificarMudanca() => OnChange?.Invoke();

    // ---- Modelos internos ----

    private class UserInfoData
    {
        public string Cpf    { get; set; } = "";
        public string Nome   { get; set; } = "";
        public string Perfil { get; set; } = "";
    }

    private class RefreshResponse
    {
        public string Token        { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int ExpiresInMinutes { get; set; }
        public bool SenhaTemporaria { get; set; } = false;
    }
}
