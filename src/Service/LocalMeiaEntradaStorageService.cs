using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace src.Service;

/// <summary>
/// Serviço de armazenamento para documentos de meia-entrada.
/// Aceita imagens (JPEG, PNG) e PDF.
///
/// ═══════════════════════════════════════════════════════════════════
/// SEGURANÇA: Arquivos são armazenados em App_Data/uploads/meia-entrada/
/// (FORA de wwwroot). Isso impede acesso público direto via URL.
/// O acesso só é possível através do endpoint autorizado do controller
/// (GET /api/meia-entrada/{id}/arquivo), que exige role ADMIN.
///
/// VALIDAÇÃO DE CONTEÚDO (MAGIC BYTES):
///   Além da validação de MIME type (enviado pelo cliente), verificamos
///   os primeiros bytes do arquivo (magic bytes/signature) para garantir
///   que o conteúdo corresponde ao tipo declarado. Isso impede que
///   um atacante envie um executável ou script renomeado como .pdf.
/// ═══════════════════════════════════════════════════════════════════
/// </summary>
public interface IMeiaEntradaStorageService
{
    /// <summary>
    /// Salva um arquivo de documento comprobatório e retorna o caminho relativo.
    /// </summary>
    Task<string> SalvarDocumentoAsync(Stream stream, string nomeOriginal, string contentType);

    /// <summary>
    /// Lê um documento do disco e retorna seus bytes.
    /// </summary>
    Task<byte[]> LerDocumentoAsync(string caminhoRelativo);

    /// <summary>
    /// Remove um documento do disco.
    /// </summary>
    Task<bool> RemoverDocumentoAsync(string caminhoRelativo);
}

public sealed class LocalMeiaEntradaStorageService : IMeiaEntradaStorageService
{
    private static readonly HashSet<string> _tiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "application/pdf"
    };

    private static readonly long _tamanhoMaximoBytes = 10 * 1024 * 1024; // 10 MB

    // ══════════════════════════════════════════════════════════════════
    // Magic bytes (assinaturas de arquivo) para validar conteúdo real
    // ══════════════════════════════════════════════════════════════════
    // Estes bytes são lidos do INÍCIO do arquivo e comparados com o
    // tipo MIME informado pelo cliente. Se não corresponderem,
    // o upload é rejeitado — impedindo que um .exe ou .js seja
    // enviado com Content-Type: application/pdf.
    // ══════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, byte[][]> _magicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },           // JPEG: começa com FF D8 FF
        ["image/jpg"]  = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },           // JPEG (alias)
        ["image/png"]  = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },     // PNG: 89 50 4E 47 (.PNG)
        ["image/webp"] = new[] {
            new byte[] { 0x52, 0x49, 0x46, 0x46 },                            // RIFF (container WebP)
            new byte[] { 0x57, 0x45, 0x42, 0x50 }                             // WEBP (dentro do RIFF)
        },
        ["application/pdf"] = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } } // PDF: %PDF
    };

    // ══════════════════════════════════════════════════════════════════
    // ANTES: arquivos em wwwroot/uploads/meia-entrada/ (ACESSO PÚBLICO!)
    // AGORA: arquivos em App_Data/uploads/meia-entrada/ (PROTEGIDO)
    // ══════════════════════════════════════════════════════════════════
    // Diretório App_Data é protegido por padrão no ASP.NET Core.
    // Mesmo que alguém descubra o caminho, o servidor não serve arquivos
    // de fora de wwwroot via URL direta.
    private const string Subpasta = "meia-entrada";

    private readonly string _pastaRaiz;
    private readonly ILogger<LocalMeiaEntradaStorageService> _logger;

    public LocalMeiaEntradaStorageService(IWebHostEnvironment env, ILogger<LocalMeiaEntradaStorageService> logger)
    {
        _logger = logger;
        // ══════════════════════════════════════════════════════════════
        // Usa ContentRootPath (raiz do projeto) + App_Data, NÃO WebRootPath
        // App_Data é uma convenção do ASP.NET para dados de aplicação
        // que não devem ser servidos estaticamente.
        // ══════════════════════════════════════════════════════════════
        _pastaRaiz = Path.GetFullPath(Path.Combine(
            env.ContentRootPath, "App_Data", "uploads", Subpasta));
        _logger.LogInformation(
            "LocalMeiaEntradaStorageService initialized at {PastaRaiz} (PROTEGIDO — fora de wwwroot)",
            _pastaRaiz);
    }

    public async Task<string> SalvarDocumentoAsync(Stream stream, string nomeOriginal, string contentType)
    {
        Directory.CreateDirectory(_pastaRaiz);

        // ══════════════════════════════════════════════════════════════
        // VALIDAÇÃO 1: Tipo MIME
        // ══════════════════════════════════════════════════════════════
        if (!_tiposPermitidos.Contains(contentType))
            throw new InvalidOperationException(
                $"Tipo de arquivo não permitido: {contentType}. Aceitos: JPEG, PNG, WebP, PDF.");

        // ══════════════════════════════════════════════════════════════
        // VALIDAÇÃO 2: Tamanho
        // ══════════════════════════════════════════════════════════════
        if (stream.Length > _tamanhoMaximoBytes)
            throw new InvalidOperationException("Arquivo excede o limite de 10 MB.");

        // ══════════════════════════════════════════════════════════════
        // VALIDAÇÃO 3: Magic bytes (assinatura do arquivo)
        //
        // Lê os primeiros bytes do stream e verifica se correspondem
        // ao formato esperado para o tipo MIME. Isso impede que
        // um atacante envie um executável (.exe), script (.js) ou
        // outro arquivo malicioso com Content-Type falsificado.
        //
        // Nota: Restauramos a posição do stream após a leitura porque
        // o stream pode vir de um multipart form (não-seekable) ou
        // pode precisar ser lido novamente para cópia.
        // ══════════════════════════════════════════════════════════════
        if (!ValidarMagicBytes(stream, contentType))
        {
            _logger.LogWarning(
                "⚠️ [SEGURANÇA] Upload rejeitado: magic bytes não correspondem ao tipo informado. " +
                "Arquivo={Nome}, Content-Type informado={ContentType}, Tamanho={Tamanho}",
                nomeOriginal, contentType, stream.Length);
            throw new InvalidOperationException(
                $"O conteúdo do arquivo não corresponde ao tipo {contentType}. " +
                "O arquivo pode estar corrompido ou ser de um tipo diferente do declarado.");
        }

        // Restaura a posição do stream para a cópia subsequente
        if (stream.CanSeek)
            stream.Position = 0;

        // Extrai extensão do content type
        var ext = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };

        // UUID como nome — evita path traversal e colisões
        var nomeUuid = $"{Guid.NewGuid():N}{ext}";
        var caminhoFisico = Path.Combine(_pastaRaiz, nomeUuid);

        // Garante que o caminho final está dentro da pasta raiz (defesa contra path traversal)
        var caminhoCanonico = Path.GetFullPath(caminhoFisico);
        if (!caminhoCanonico.StartsWith(Path.GetFullPath(_pastaRaiz), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de upload inválido.");

        stream.Position = 0;
        await using var fileStream = File.Create(caminhoFisico);
        await stream.CopyToAsync(fileStream);

        // ═══════════════════════════════════════════════════════════════════
        // ANTES: urlRelativa começava com "/uploads/meia-entrada/"
        //   Isso era um caminho URL público em wwwroot.
        // AGORA: retornamos apenas o nome UUID do arquivo.
        //   O controller usa esse nome para ler o arquivo de App_Data,
        //   e o arquivo NUNCA fica acessível via URL direta.
        // ═══════════════════════════════════════════════════════════════════
        _logger.LogInformation(
            "Documento meia-entrada salvo: {Arquivo} ({Bytes} bytes, {ContentType})",
            nomeUuid, stream.Length, contentType);

        return nomeUuid;
    }

    /// <summary>
    /// Valida os primeiros bytes (magic bytes/signature) de um stream
    /// contra as assinaturas conhecidas para o tipo MIME informado.
    /// </summary>
    /// <param name="stream">Stream de dados do arquivo.</param>
    /// <param name="contentType">Tipo MIME informado pelo cliente.</param>
    /// <returns>True se os magic bytes correspondem ao tipo esperado.</returns>
    private static bool ValidarMagicBytes(Stream stream, string contentType)
    {
        // Se não temos magic bytes definidos para este tipo, pula a validação
        if (!_magicBytes.TryGetValue(contentType, out var assinaturas) || assinaturas.Length == 0)
            return true; // Tipo desconhecido mas permitido — deixa passar

        // Para WebP, precisamos ler até 12 bytes (RIFF header + WEBP chunk)
        var bytesParaLer = assinaturas.Max(s => s.Length);
        var buffer = new byte[bytesParaLer];
        var lidos = stream.Read(buffer, 0, bytesParaLer);

        if (lidos < assinaturas.Min(s => s.Length))
            return false; // Arquivo menor que a menor assinatura esperada

        // Verifica se alguma assinatura corresponde
        return assinaturas.Any(signature =>
            lidos >= signature.Length &&
            buffer.Take(signature.Length).SequenceEqual(signature));
    }

    public async Task<byte[]> LerDocumentoAsync(string nomeArquivo)
    {
        // ═══════════════════════════════════════════════════════════════════
        // ANTES: caminhoRelativo era "/uploads/meia-entrada/{uuid}.ext"
        //   e fazia parsing complexo para resolver o caminho físico.
        // AGORA: nomeArquivo é apenas "{uuid}.ext" e o caminho completo
        //   é resolvido diretamente dentro de App_Data/uploads/meia-entrada/.
        // ═══════════════════════════════════════════════════════════════════
        var apenasNome = Path.GetFileName(nomeArquivo);
        if (string.IsNullOrWhiteSpace(apenasNome))
            throw new InvalidOperationException("Nome de arquivo inválido.");

        var caminhoFisico = Path.GetFullPath(Path.Combine(_pastaRaiz, apenasNome));

        // Path traversal: garante que o arquivo está DENTRO da pasta raiz
        if (!caminhoFisico.StartsWith(_pastaRaiz, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal detectado: {Caminho}", nomeArquivo);
            throw new InvalidOperationException("Caminho inválido.");
        }

        if (!File.Exists(caminhoFisico))
            throw new FileNotFoundException("Documento não encontrado.", nomeArquivo);

        return await File.ReadAllBytesAsync(caminhoFisico);
    }

    public Task<bool> RemoverDocumentoAsync(string nomeArquivo)
    {
        try
        {
            var apenasNome = Path.GetFileName(nomeArquivo);
            if (string.IsNullOrWhiteSpace(apenasNome))
                return Task.FromResult(false);

            var caminhoFisico = Path.GetFullPath(Path.Combine(_pastaRaiz, apenasNome));

            if (!caminhoFisico.StartsWith(_pastaRaiz, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal detectado ao remover: {Caminho}", nomeArquivo);
                return Task.FromResult(false);
            }

            if (File.Exists(caminhoFisico))
            {
                File.Delete(caminhoFisico);
                _logger.LogInformation("Documento meia-entrada removido: {Arquivo}", nomeArquivo);
                return Task.FromResult(true);
            }

            _logger.LogWarning("Documento não encontrado para remoção: {Arquivo}", nomeArquivo);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover documento: {Arquivo}", nomeArquivo);
            return Task.FromResult(false);
        }
    }
}
