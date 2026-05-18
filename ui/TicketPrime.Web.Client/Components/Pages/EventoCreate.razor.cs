using FluentValidation;
using Microsoft.AspNetCore.Components;
using Severity = MudBlazor.Severity;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TicketPrime.Web.Client.Services;
using TicketPrime.Web.Shared.Models;
using TicketPrime.Web.Client.Validators;

namespace TicketPrime.Web.Client.Components.Pages;

public partial class EventoCreate : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IValidator<EventoCreateDto> Validator { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;
    // ─────────────────────────────────────────────────────────────────────────
    // Constantes de negócio
    // ─────────────────────────────────────────────────────────────────────────
    private const int    MaxFotos              = 10;
    private const int    MinFotos              = 1;
    private const long   MaxTamanhoFoto        = 5 * 1024 * 1024;   // 5 MB
    private const int    MinResolucaoLargura   = 800;
    private const int    MinResolucaoAltura    = 600;
    private const int    MaxEventosAtivosOrg   = 5;   // limite por organizador
    private static readonly HashSet<string> TiposAceitos =
        ["image/jpeg", "image/png", "image/webp"];

    // ─────────────────────────────────────────────────────────────────────────
    // Estado do formulário
    // ─────────────────────────────────────────────────────────────────────────
    private EventoCreateDto          _evento    = new();

    // Erros de validação inline
    private Dictionary<string, string> _erros    = new();
    private string?                    _erroGeral;

    // ── Estado do formulário de setores e lotes ─────────────────────────────
    // Estes campos auxiliares permitem adicionar itens às listas de TiposIngresso e Lotes
    private string _novoSetorNome        = string.Empty;
    private string? _novoSetorDescricao;
    private string _novoSetorPreco       = string.Empty;
    private string _novoSetorCapacidade  = string.Empty;

    private string _novoLoteNome         = string.Empty;
    private string _novoLotePreco        = string.Empty;
    private string _novoLoteQtd          = string.Empty;
    private int?   _novoLoteTicketTypeIndex;
    private string _novoLoteDataInicio   = string.Empty;
    private string _novoLoteDataFim      = string.Empty;

    /// <summary>Se true, usa o modelo de setores (múltiplos tipos de ingresso).</summary>
    private bool _usarSetores;

    private string Erro(string campo) =>
        _erros.TryGetValue(campo, out var e) ? e : "";

    // ─────────────────────────────────────────────────────────────────────────
    // Estado de fotos
    // ─────────────────────────────────────────────────────────────────────────
    private readonly List<FotoItem>     _fotos           = [];
    /// <summary>Hashes SHA-256 dos conteúdos de imagem já adicionados (deduplicação).</summary>
    private readonly HashSet<string>    _hashesConteudo  = [];

    // ─────────────────────────────────────────────────────────────────────────
    // Estado da UI
    // ─────────────────────────────────────────────────────────────────────────
    private bool    _carregando          = false;
    private bool    _criptografandoFotos = false;
    private bool    _isDragOver          = false;
    // Crypto gerenciado internamente – não exposto na UI
    private bool    _cryptoInicializado  = false;
    private string? _cryptoErro          = null;

    private DotNetObjectReference<EventoCreate>? _dotNetRef;

    // ─────────────────────────────────────────────────────────────────────────
    // Gêneros disponíveis
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly string[] _generosMusicas =
        ["Rock", "Pop", "Sertanejo", "Eletrônico", "Forró", "MPB", "Outro"];

    // Campos auxiliares para separar data e hora antes de combinar em DataHora
    private DateTime? _dataEvento;
    private TimeSpan? _horaEvento;
    private DateTime? _dataEventoTermino;
    private TimeSpan? _horaEventoTermino;

    private string _dataStr
    {
        get => _dataEvento?.ToString("yyyy-MM-dd") ?? "";
        set { _dataEvento = DateTime.TryParse(value, out var d) ? d : null; AtualizarDataHora(); }
    }

    private string _horaStr
    {
        get => _horaEvento?.ToString(@"hh\:mm") ?? "";
        set { _horaEvento = TimeSpan.TryParse(value, out var t) ? t : null; AtualizarDataHora(); }
    }

    private string _precoStr
    {
        get => _evento.Preco.HasValue ? _evento.Preco.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
        set => _evento.Preco = decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private string _taxaStr
    {
        get => _evento.TaxaServico.HasValue ? _evento.TaxaServico.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
        set => _evento.TaxaServico = decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private string _dataTerminoStr
    {
        get => _dataEventoTermino?.ToString("yyyy-MM-dd") ?? "";
        set { _dataEventoTermino = DateTime.TryParse(value, out var d) ? d : null; AtualizarDataHoraTermino(); }
    }

    private string _horaTerminoStr
    {
        get => _horaEventoTermino?.ToString(@"hh\:mm") ?? "";
        set { _horaEventoTermino = TimeSpan.TryParse(value, out var t) ? t : null; AtualizarDataHoraTermino(); }
    }

    private void AtualizarDataHora()
    {
        if (_dataEvento.HasValue)
            _evento.DataHora = _dataEvento.Value.Date + (_horaEvento ?? TimeSpan.Zero);
        else
            _evento.DataHora = null;
    }

    private void AtualizarDataHoraTermino()
    {
        if (_dataEventoTermino.HasValue)
            _evento.DataHoraTermino = _dataEventoTermino.Value.Date + (_horaEventoTermino ?? TimeSpan.Zero);
        else
            _evento.DataHoraTermino = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ciclo de vida
    // ─────────────────────────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        await Session.EnsureAdminAsync(Navigation);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _dotNetRef = DotNetObjectReference.Create(this);
        await InicializarCryptoAsync();
        await InicializarDropZoneAsync();
    }

    private async Task InicializarCryptoAsync()
    {
        try
        {
            await CryptoSvc.InicializarAsync();
            _cryptoInicializado = true;
            _cryptoErro         = null;
        }
        catch (Exception ex)
        {
            _cryptoErro         = ex.Message;
            _cryptoInicializado = false;
            Snackbar.Add("Falha ao inicializar criptografia: " + ex.Message, Severity.Error);
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task InicializarDropZoneAsync()
    {
        if (!_cryptoInicializado) return;

        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("ticketPrimeCrypto.initDropZone", _dotNetRef, "ec-drop-zone");
        }
        catch (Exception ex)
        {
            // Drop zone é funcionalidade extra; falha não bloqueia o formulário
            Console.Error.WriteLine("[EventoCreate] Falha ao inicializar drop zone: " + ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Callbacks invocadas pelo JavaScript (drag & drop)
    // ─────────────────────────────────────────────────────────────────────────

    [JSInvokable]
    public void OnDragEnter()
    {
        _isDragOver = true;
        InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public void OnDragLeave()
    {
        _isDragOver = false;
        InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnFilesDropped(DroppedFileData[] files)
    {
        foreach (var file in files)
        {
            if (_fotos.Count >= MaxFotos) break;
            await AdicionarFotoAsync(file.Base64Data, file.Type, file.Name, file.Size);
        }
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public void OnDropErrors(string[] erros)
    {
        foreach (var erro in erros)
            Snackbar.Add(erro, Severity.Warning);
        InvokeAsync(StateHasChanged);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Upload via InputFile (clique)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleInputFileAsync(InputFileChangeEventArgs e)
    {
        var arquivos = e.GetMultipleFiles(MaxFotos - _fotos.Count);

        foreach (var arquivo in arquivos)
        {
            if (_fotos.Count >= MaxFotos)
            {
                Snackbar.Add($"Limite de {MaxFotos} fotos atingido.", Severity.Warning);
                break;
            }

            if (!TiposAceitos.Contains(arquivo.ContentType))
            {
                Snackbar.Add($"\"{arquivo.Name}\" – tipo não suportado (apenas JPG, PNG, WebP).", Severity.Warning);
                continue;
            }

            if (arquivo.Size > MaxTamanhoFoto)
            {
                Snackbar.Add($"\"{arquivo.Name}\" – excede 5 MB.", Severity.Warning);
                continue;
            }

            try
            {
                var base64 = await LerArquivoComoBase64Async(arquivo);
                await AdicionarFotoAsync(base64, arquivo.ContentType, arquivo.Name, arquivo.Size);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Erro ao ler \"{arquivo.Name}\": {ex.Message}", Severity.Error);
            }
        }
    }

    private static async Task<string> LerArquivoComoBase64Async(IBrowserFile arquivo)
    {
        await using var stream = arquivo.OpenReadStream(MaxTamanhoFoto);
        using var ms           = new MemoryStream();
        await stream.CopyToAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Adicionar foto + gerar thumbnail + criptografar
    // ─────────────────────────────────────────────────────────────────────────

    private async Task AdicionarFotoAsync(string base64, string mimeType, string nome, long tamanho)
    {
        // ── Regra: resolução mínima (800 × 600) ────────────────────────────────
        try
        {
            var dims = await JS.InvokeAsync<ImageDimensions>(
                "ticketPrimeCrypto.getImageDimensions", base64, mimeType);

            if (dims.Width < MinResolucaoLargura || dims.Height < MinResolucaoAltura)
            {
                Snackbar.Add(
                    $"\"{nome}\" – resolução insuficiente ({dims.Width}×{dims.Height}). " +
                    $"Mínimo exigido: {MinResolucaoLargura}×{MinResolucaoAltura} px.",
                    Severity.Warning);
                return;
            }
        }
        catch
        {
            // Se não conseguir ler dimensões, bloqueia o upload por segurança
            Snackbar.Add($"\"{nome}\" – não foi possível verificar as dimensões da imagem.", Severity.Error);
            return;
        }

        // ── Regra: sem fotos duplicadas (hash SHA-256 do conteúdo) ──────────────
        string hashConteudo;
        try
        {
            hashConteudo = await JS.InvokeAsync<string>("ticketPrimeCrypto.hashImageContent", base64);
        }
        catch
        {
            hashConteudo = nome + "|" + tamanho;   // fallback: nome+tamanho
        }

        if (!_hashesConteudo.Add(hashConteudo))
        {
            Snackbar.Add($"\"{nome}\" – imagem idêntica a uma já adicionada. Remova a duplicata.", Severity.Warning);
            return;
        }

        // ── Gera thumbnail (JPEG, 400px max) para exibição na vitrine ───────────
        string thumbnailBase64;
        try
        {
            thumbnailBase64 = await JS.InvokeAsync<string>("ticketPrimeCrypto.generateThumbnail", base64, mimeType, 400);
        }
        catch
        {
            thumbnailBase64 = string.Empty;
            Console.Error.WriteLine($"[EventoCreate] Falha ao gerar thumbnail para \"{nome}\".");
        }

        var foto = new FotoItem
        {
            ThumbnailDataUrl = $"data:{mimeType};base64,{base64}",
            ThumbnailBase64  = thumbnailBase64,
            MimeType         = mimeType,
            NomeArquivo      = nome,
            Tamanho          = tamanho,
            HashConteudo     = hashConteudo,
            Criptografando   = true
        };

        _fotos.Add(foto);
        _criptografandoFotos = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var dadosCifrados = await CryptoSvc.CriptografarImagemAsync(base64, mimeType, nome, tamanho);

            // Cria preview quebrado a partir dos primeiros bytes do ciphertext
            // (o navegador exibirá a imagem como corrompida, demonstrando que os dados são ilegíveis)
            var previewBase64 = dadosCifrados.CiphertextBase64.Length > 400
                ? dadosCifrados.CiphertextBase64[..400]
                : dadosCifrados.CiphertextBase64;

            foto.CiphertextPreviewDataUrl = $"data:{mimeType};base64,{previewBase64}";
            foto.DadosCriptografados      = dadosCifrados;
            foto.Criptografada            = true;
        }
        catch (Exception ex)
        {
            _fotos.Remove(foto);
            Snackbar.Add($"Erro ao criptografar \"{nome}\": {ex.Message}", Severity.Error);
        }
        finally
        {
            foto.Criptografando  = false;
            _criptografandoFotos = _fotos.Any(f => f.Criptografando);
            await InvokeAsync(StateHasChanged);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Remover foto
    // ─────────────────────────────────────────────────────────────────────────

    private void RemoverFoto(string id)
    {
        var foto = _fotos.FirstOrDefault(f => f.Id == id);
        if (foto is null) return;

        // Libera o hash para permitir re-adicionar a mesma imagem futuramente
        if (!string.IsNullOrEmpty(foto.HashConteudo))
            _hashesConteudo.Remove(foto.HashConteudo);

        _fotos.Remove(foto);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Abrir seletor de arquivos
    // ─────────────────────────────────────────────────────────────────────────

    private async Task AbrirSeletorArquivosAsync()
    {
        // Aciona o <input type="file"> oculto via JS
        await JS.InvokeVoidAsync("ticketPrimeCrypto.clickFileInput", "ec-file-input");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Criar evento – submit
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    // Métodos para gerenciar Tipos de Ingresso (Setores)
    // ─────────────────────────────────────────────────────────────────────────

    private void AdicionarSetor()
    {
        if (string.IsNullOrWhiteSpace(_novoSetorNome)) return;
        if (!decimal.TryParse(_novoSetorPreco, NumberStyles.Any, CultureInfo.InvariantCulture, out var preco)) return;
        if (!int.TryParse(_novoSetorCapacidade, out var capacidade) || capacidade <= 0) return;

        _evento.TiposIngresso.Add(new TicketTypeFormItem
        {
            Nome = _novoSetorNome.Trim(),
            Descricao = string.IsNullOrWhiteSpace(_novoSetorDescricao) ? null : _novoSetorDescricao.Trim(),
            Preco = preco,
            CapacidadeTotal = capacidade,
            Ordem = _evento.TiposIngresso.Count + 1
        });

        // Limpa campos
        _novoSetorNome = string.Empty;
        _novoSetorDescricao = null;
        _novoSetorPreco = string.Empty;
        _novoSetorCapacidade = string.Empty;
    }

    private void RemoverSetor(TicketTypeFormItem item)
    {
        _evento.TiposIngresso.Remove(item);
        // Reordena
        for (int i = 0; i < _evento.TiposIngresso.Count; i++)
            _evento.TiposIngresso[i].Ordem = i + 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Métodos para gerenciar Lotes Progressivos
    // ─────────────────────────────────────────────────────────────────────────

    private void AdicionarLote()
    {
        if (string.IsNullOrWhiteSpace(_novoLoteNome)) return;
        if (!decimal.TryParse(_novoLotePreco, NumberStyles.Any, CultureInfo.InvariantCulture, out var preco)) return;
        if (!int.TryParse(_novoLoteQtd, out var qtd) || qtd <= 0) return;

        DateTime? dataInicio = null;
        if (DateTime.TryParse(_novoLoteDataInicio, out var di)) dataInicio = di;

        DateTime? dataFim = null;
        if (DateTime.TryParse(_novoLoteDataFim, out var df)) dataFim = df;

        _evento.Lotes.Add(new LoteFormItem
        {
            Nome = _novoLoteNome.Trim(),
            TicketTypeIndex = _novoLoteTicketTypeIndex,
            Preco = preco,
            QuantidadeMaxima = qtd,
            DataInicio = dataInicio,
            DataFim = dataFim
        });

        // Limpa campos
        _novoLoteNome = string.Empty;
        _novoLotePreco = string.Empty;
        _novoLoteQtd = string.Empty;
        _novoLoteTicketTypeIndex = null;
        _novoLoteDataInicio = string.Empty;
        _novoLoteDataFim = string.Empty;
    }

    private void RemoverLote(LoteFormItem item)
    {
        _evento.Lotes.Remove(item);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Criar evento – submit
    // ─────────────────────────────────────────────────────────────────────────

    private async Task CriarEventoAsync()
    {
        _erros.Clear();
        _erroGeral = null;
        StateHasChanged();

        var resultado = Validator.Validate(_evento);
        if (!resultado.IsValid)
        {
            foreach (var erro in resultado.Errors)
                _erros.TryAdd(erro.PropertyName, erro.ErrorMessage);

            _erroGeral = "Corrija os erros destacados antes de continuar.";
            StateHasChanged();
            return;
        }

        if (_criptografandoFotos)
        {
            Snackbar.Add("Aguarde a criptografia das fotos terminar.", Severity.Info);
            return;
        }

        // ── Regra: mínimo de fotos obrigatórias ───────────────────────────────
        if (_fotos.Count < MinFotos)
        {
            Snackbar.Add($"Adicione pelo menos {MinFotos} foto da banda antes de criar o evento.", Severity.Warning);
            return;
        }

        var fotosNaoCifradas = _fotos.Where(f => !f.Criptografada).ToList();
        if (fotosNaoCifradas.Count > 0)
        {
            Snackbar.Add("Algumas fotos não foram criptografadas. Tente removê-las e adicioná-las novamente.", Severity.Warning);
            return;
        }

        _carregando = true;
        StateHasChanged();

        try
        {
            // ── Status inicial: Rascunho ────────────────────────────────────────
            _evento.Status = EventStatus.Rascunho;

            // ── Mapeia EventoCreateDto → CreateEventDto ─────────────────────────
            var dto = new CreateEventDto
            {
                Nome = _evento.Nome,
                CapacidadeTotal = _evento.CapacidadeMaxima,
                DataEvento = _evento.DataHora ?? DateTime.Now.AddDays(1),
                DataTermino = _evento.DataHoraTermino,
                PrecoPadrao = _evento.EventoGratuito ? 0 : (_evento.Preco ?? 0),
                LimiteIngressosPorUsuario = 6,
                TaxaServico = _evento.TaxaServico ?? 0,
                Local = _evento.Local,
                Descricao = _evento.Descricao,
                GeneroMusical = _evento.GeneroMusical,
                EventoGratuito = _evento.EventoGratuito,
                TemMeiaEntrada = _evento.TemMeiaEntrada,
                Status = _evento.Status,
                // ── Mapeia Tipos de Ingresso (Setores) ─────────────────────────
                TiposIngresso = _usarSetores && _evento.TiposIngresso.Count > 0
                    ? _evento.TiposIngresso.Select(t => new TicketTypeDto
                    {
                        Nome = t.Nome,
                        Descricao = t.Descricao,
                        Preco = t.Preco,
                        CapacidadeTotal = t.CapacidadeTotal,
                        Ordem = t.Ordem
                    }).ToList()
                    : null,
                // ── Mapeia Lotes Progressivos ──────────────────────────────────
                Lotes = _evento.Lotes.Count > 0
                    ? _evento.Lotes.Select(l => new LoteDto
                    {
                        Nome = l.Nome,
                        // TicketTypeId é 1-based index quando usando setores
                        TicketTypeId = l.TicketTypeIndex.HasValue ? l.TicketTypeIndex.Value + 1 : null,
                        Preco = l.Preco,
                        QuantidadeMaxima = l.QuantidadeMaxima,
                        DataInicio = l.DataInicio,
                        DataFim = l.DataFim
                    }).ToList()
                    : null,
                // ── Inclui fotos criptografadas (E2E) + thumbnail (vitrine) ────
                Fotos = _fotos
                    .Where(f => f.DadosCriptografados is { Criptografada: true })
                    .Select(f => new EncryptedPhotoDto
                    {
                        CiphertextBase64      = f.DadosCriptografados!.CiphertextBase64,
                        IvBase64              = f.DadosCriptografados!.IvBase64,
                        ChaveAesCifradaBase64 = f.DadosCriptografados!.ChaveAesCifradaBase64,
                        ChavePublicaOrgJwk    = f.DadosCriptografados!.ChavePublicaOrgJwk,
                        HashNomeOriginal      = f.DadosCriptografados!.HashNomeOriginal,
                        TipoMime              = f.DadosCriptografados!.TipoMime,
                        TamanhoBytes          = f.DadosCriptografados!.TamanhoBytes,
                        Criptografada         = f.DadosCriptografados!.Criptografada,
                        ThumbnailBase64       = string.IsNullOrEmpty(f.ThumbnailBase64) ? null : f.ThumbnailBase64
                    })
                    .ToList()
            };

            Console.WriteLine($"[DEBUG] Enviando POST /api/eventos com {dto.Fotos?.Count ?? 0} foto(s) criptografada(s)");
            var response = await Http.PostAsJsonAsync("api/eventos", dto);

            if (response.IsSuccessStatusCode)
            {
                Snackbar.Add("Evento criado com sucesso! 🎉",
                             Severity.Success, config => config.VisibleStateDuration = 4000);

                await Task.Delay(800);
                Navigation.NavigateTo("/eventos");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var err = JsonSerializer.Deserialize<JsonElement>(body);
                    var msg = err.TryGetProperty("mensagem", out var m) ? m.GetString() : "Erro ao criar evento.";
                    Snackbar.Add(msg ?? "Erro desconhecido.", Severity.Error);
                }
                catch
                {
                    Snackbar.Add($"Erro ao criar evento (código {(int)response.StatusCode}).", Severity.Error);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Snackbar.Add("❌ Erro de conexão com o servidor. Verifique se a API está rodando.", Severity.Error);
            Console.WriteLine($"[DEBUG] HttpRequestException: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Snackbar.Add("❌ A requisição excedeu o tempo limite. Tente novamente.", Severity.Error);
        }
        catch (Exception ex)
        {
            Snackbar.Add("Erro ao criar evento: " + ex.Message, Severity.Error);
        }
        finally
        {
            _carregando = false;
            StateHasChanged();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dispose
    // ─────────────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        await CryptoSvc.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tipos internos
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Estado local de uma foto na UI (pré e pós-criptografia).</summary>
    private sealed class FotoItem
    {
        public string             Id                       { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string             ThumbnailDataUrl         { get; set; } = string.Empty;
        public string             CiphertextPreviewDataUrl { get; set; } = string.Empty;
        public bool               Criptografando           { get; set; }
        public bool               Criptografada            { get; set; }
        public string             MimeType                 { get; set; } = string.Empty;
        public string             NomeArquivo              { get; set; } = string.Empty;
        public long               Tamanho                  { get; set; }
        /// <summary>SHA-256 do conteúdo bruto da imagem (deduplicação).</summary>
        public string             HashConteudo             { get; set; } = string.Empty;
        public EncryptedPhoto? DadosCriptografados      { get; set; }
        /// <summary>
        /// Thumbnail redimensionado (JPEG, max 400px largura) em Base64 (sem prefixo).
        /// Armazenado sem criptografia para exibição na vitrine pública.
        /// </summary>
        public string             ThumbnailBase64          { get; set; } = string.Empty;
    }

    /// <summary>DTO para arquivos recebidos via drag & drop do JavaScript.</summary>
    public sealed class DroppedFileData
    {
        public string Name       { get; set; } = string.Empty;
        public long   Size       { get; set; }
        public string Type       { get; set; } = string.Empty;
        public string Base64Data { get; set; } = string.Empty;
    }

    /// <summary>Dimensões de imagem retornadas pelo JS via ticketPrimeCrypto.getImageDimensions.</summary>
    private sealed class ImageDimensions
    {
        public int Width  { get; set; }
        public int Height { get; set; }
    }
}
