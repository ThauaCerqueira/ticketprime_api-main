namespace TicketPrime.Web.Services;

public class SessionService
{
    public string? Nome { get; private set; }
    public string? Perfil { get; private set; }
    public string? Token { get; private set; }
    public bool EstaLogado => !string.IsNullOrEmpty(Token);
    public bool EhAdmin => Perfil == "ADMIN";

    public event Action? OnChange;

    public void Logar(string nome, string perfil, string token)
    {
        Nome = nome;
        Perfil = perfil;
        Token = token;
        NotificarMudanca();
    }

    public void Deslogar()
    {
        Nome = null;
        Perfil = null;
        Token = null;
        NotificarMudanca();
    }

    private void NotificarMudanca() => OnChange?.Invoke();
}