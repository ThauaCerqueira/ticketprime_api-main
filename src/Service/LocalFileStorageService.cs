using Microsoft.Extensions.Logging;

namespace src.Service;

/// <summary>
/// Implementação de IStorageService com armazenamento local em disco.
/// Salva em wwwroot/uploads/{subpasta}/{uuid}.ext acessível via URL relativa.
/// Em produção, substitua por S3StorageService, AzureBlobStorageService, etc.
/// </summary>
public sealed class LocalFileStorageService : IStorageService
{
    private static readonly HashSet<string> _tiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif"
    };

    private static readonly long _tamanhoMaximoBytes = 5 * 1024 * 1024; // 5 MB

    private readonly string _pastaRaiz;      // ex: "wwwroot/uploads"
    private readonly string _basePath;       // ex: "wwwroot" (resolvido de WebRootPath ou fallback)
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IWebHostEnvironment env, ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;
        // ⚠️ WebRootPath pode ser null se a pasta wwwroot não existir (ex: dev初期).
        //    Fallback explícito para Path.Combine(ContentRootPath, "wwwroot").
        var basePath = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _basePath = Path.GetFullPath(basePath);
        _pastaRaiz = Path.Combine(_basePath, "uploads");
        _logger.LogInformation("LocalFileStorageService initialized at {PastaRaiz}", _pastaRaiz);
    }

    public async Task<string> SalvarAsync(Stream stream, string nomeArquivo, string contentType, string subpasta = "geral")
    {
        // Valida tipo MIME
        if (!_tiposPermitidos.Contains(contentType))
            throw new InvalidOperationException(
                $"Tipo de arquivo não permitido: {contentType}. Aceitos: JPEG, PNG, WebP, GIF.");

        // Valida tamanho
        if (stream.Length > _tamanhoMaximoBytes)
            throw new InvalidOperationException("Arquivo excede o limite de 5 MB.");

        // Sanitiza subpasta — apenas alfanumérico e hífens
        var subpastaSegura = System.Text.RegularExpressions.Regex.Replace(subpasta, @"[^a-zA-Z0-9\-]", "");
        if (string.IsNullOrWhiteSpace(subpastaSegura)) subpastaSegura = "geral";

        // Extrai extensão do content type (não confia no nome enviado pelo cliente)
        var ext = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".bin"
        };

        Directory.CreateDirectory(_pastaRaiz);
        var pasta = Path.Combine(_pastaRaiz, subpastaSegura);
        Directory.CreateDirectory(pasta);

        // UUID como nome — evita path traversal e colisões
        var nomeUuid = $"{Guid.NewGuid():N}{ext}";
        var caminhoFisico = Path.Combine(pasta, nomeUuid);

        // Garante que o caminho final está dentro da pasta raiz (defesa contra path traversal)
        var caminhoCanônico = Path.GetFullPath(caminhoFisico);
        if (!caminhoCanônico.StartsWith(Path.GetFullPath(_pastaRaiz), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de upload inválido.");

        stream.Position = 0;
        using var fileStream = File.Create(caminhoFisico);
        await stream.CopyToAsync(fileStream);

        var urlRelativa = $"/uploads/{subpastaSegura}/{nomeUuid}";
        _logger.LogInformation("Arquivo salvo: {Url} ({Bytes} bytes)", urlRelativa, stream.Length);

        return urlRelativa;
    }

    public Task<bool> RemoverAsync(string caminhoRelativo)
    {
        try
        {
            // Sanitiza o caminho relativo (ex: "/uploads/eventos/uuid.jpg" → "uploads/eventos/uuid.jpg")
            var caminhoLimpo = caminhoRelativo.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var caminhoFisico = Path.GetFullPath(Path.Combine(_basePath, caminhoLimpo));

            // Garante que está dentro de wwwroot (defesa contra path traversal)
            if (!caminhoFisico.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Tentativa de remoção fora da pasta permitida: {Caminho}", caminhoRelativo);
                return Task.FromResult(false);
            }

            if (File.Exists(caminhoFisico))
            {
                File.Delete(caminhoFisico);
                _logger.LogInformation("Arquivo removido: {Caminho}", caminhoRelativo);
                return Task.FromResult(true);
            }

            _logger.LogWarning("Arquivo não encontrado para remoção: {Caminho}", caminhoRelativo);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover arquivo: {Caminho}", caminhoRelativo);
            return Task.FromResult(false);
        }
    }
}
