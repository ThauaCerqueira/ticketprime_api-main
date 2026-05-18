using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using src.DTOs;
using src.Infrastructure;
using src.Infrastructure.IRepository;

namespace src.Service;

public class AuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IConfiguration _configuration;
    private readonly DbConnectionFactory? _connectionFactory;
    private readonly AuditLogService? _auditLog;

    public AuthService(
        IUsuarioRepository usuarioRepository,
        IConfiguration configuration,
        DbConnectionFactory connectionFactory,
        AuditLogService? auditLog = null)
    {
        _usuarioRepository = usuarioRepository;
        _configuration = configuration;
        _connectionFactory = connectionFactory;
        _auditLog = auditLog;
    }

    // Overload para testes — permite criar sem DbConnectionFactory (refresh token não testável unitariamente sem mock de banco)
    public AuthService(IUsuarioRepository usuarioRepository, IConfiguration configuration)
    {
        _usuarioRepository = usuarioRepository;
        _configuration = configuration;
        _connectionFactory = null;
        _auditLog = null;
    }

    public virtual async Task<LoginResponseDTO?> LoginAsync(
        LoginDTO dto,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var usuario = await _usuarioRepository.ObterPorCpf(dto.Cpf);

        // Verifica o hash bcrypt — nunca compara senha em texto puro
        if (usuario == null || !BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.Senha))
        {
            // Registra tentativa de login mal-sucedida
            if (_auditLog != null)
                await _auditLog.LogLoginFalhaAsync(dto.Cpf, ipAddress ?? "unknown", userAgent);
            return null;
        }

        // Requer verificação de email antes de permitir autenticação.
        if (!usuario.EmailVerificado)
            throw new InvalidOperationException(
                "Seu email ainda não foi verificado. Verifique sua caixa de entrada e confirme o código antes de fazer login.");

        // Se "Lembrar" estiver marcado, usa token de 7 dias, senão 30 minutos
        var expireMinutes = dto.Lembrar ? 10080 : 30; // 10080 min = 7 dias

        var token = GerarToken(usuario.Cpf, usuario.Perfil, expireMinutes);
        var refreshToken = await GerarRefreshTokenAsync(usuario.Cpf);

        // Registra login bem-sucedido na auditoria
        if (_auditLog != null)
            await _auditLog.LogLoginAsync(usuario.Cpf, ipAddress ?? "unknown", userAgent);

        return new LoginResponseDTO
        {
            Cpf = usuario.Cpf,
            Nome = usuario.Nome,
            Perfil = usuario.Perfil,
            Token = token,
            SenhaTemporaria = usuario.SenhaTemporaria,
            RefreshToken = refreshToken
        };
    }

    public async Task<RefreshTokenResponseDTO?> RefreshTokenAsync(RefreshTokenRequestDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return null;

        // Hash do token recebido para busca segura no banco
        var tokenHash = ComputeSha256Hash(dto.RefreshToken);

        if (_connectionFactory == null)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        // Busca refresh token válido (não expirado, não revogado)
        var refreshToken = await connection.QueryFirstOrDefaultAsync(
            @"SELECT Id, UsuarioCpf, TokenHash, ExpiresAt
              FROM RefreshTokens WITH (UPDLOCK)
              WHERE TokenHash = @TokenHash
                AND ExpiresAt > GETUTCDATE()
                AND RevokedAt IS NULL",
            new { TokenHash = tokenHash });

        if (refreshToken == null)
            return null;

        var cpf = (string)refreshToken.UsuarioCpf;

        // Revoga o refresh token atual (rotação)
        await connection.ExecuteAsync(
            "UPDATE RefreshTokens SET RevokedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = (int)refreshToken.Id });

        var usuario = await _usuarioRepository.ObterPorCpf(cpf);
        if (usuario == null)
            return null;

        // Gera novo par de tokens
        var novoToken = GerarToken(cpf, usuario.Perfil);
        var novoRefreshToken = await GerarRefreshTokenAsync(cpf);

        var expireMinutes = _configuration.GetValue<int>("Jwt:ExpireMinutes", 30);
        if (expireMinutes <= 0) expireMinutes = 30;

        return new RefreshTokenResponseDTO
        {
            Token = novoToken,
            RefreshToken = novoRefreshToken,
            ExpiresInMinutes = expireMinutes,
            SenhaTemporaria = usuario.SenhaTemporaria
        };
    }

    /// <summary>
    /// Revoga um refresh token específico (logout). Se o token não for fornecido,
    /// revoga TODOS os tokens do usuário (logout de todos os dispositivos).
    /// </summary>
    public virtual async Task RevogarRefreshTokenAsync(string? rawToken, string? cpf = null)
    {
        if (_connectionFactory == null)
            return;

        using var connection = _connectionFactory.CreateConnection();

        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var tokenHash = ComputeSha256Hash(rawToken);
            await connection.ExecuteAsync(
                "UPDATE RefreshTokens SET RevokedAt = GETUTCDATE() WHERE TokenHash = @TokenHash AND RevokedAt IS NULL",
                new { TokenHash = tokenHash });
        }
        else if (!string.IsNullOrWhiteSpace(cpf))
        {
            await connection.ExecuteAsync(
                "UPDATE RefreshTokens SET RevokedAt = GETUTCDATE() WHERE UsuarioCpf = @Cpf AND RevokedAt IS NULL",
                new { Cpf = cpf });
        }
    }

    private async Task<string> GerarRefreshTokenAsync(string cpf)    {
        // Gera token criptograficamente seguro (64 bytes hex = 128 chars)
        var randomBytes = new byte[64];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        var refreshToken = Convert.ToHexString(randomBytes).ToLower();
        var tokenHash = ComputeSha256Hash(refreshToken);

        if (_connectionFactory == null)
            return string.Empty; // Testes sem banco — refresh token indisponível

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO RefreshTokens (UsuarioCpf, TokenHash, ExpiresAt)
              VALUES (@Cpf, @TokenHash, DATEADD(DAY, 30, GETUTCDATE()))",
            new { Cpf = cpf, TokenHash = tokenHash });

        return refreshToken;
    }

    private static string ComputeSha256Hash(string rawData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLower();
    }

    private string GerarToken(string cpf, string perfil, int? expireMinutesOverride = null)
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Chave JWT 'Jwt:Key' não encontrada. Configure em appsettings.json ou User Secrets.");

        // Se expireMinutesOverride foi passado (ex: "Lembrar-me" = 7 dias), usa ele.
        // Senão, lê da configuração (padrão 30 minutos).
        var expireMinutes = expireMinutesOverride ?? _configuration.GetValue<int>("Jwt:ExpireMinutes", 30);
        if (expireMinutes <= 0) expireMinutes = expireMinutesOverride ?? 30;

        var issuer = _configuration["Jwt:Issuer"] ?? "TicketPrime";
        var audience = _configuration["Jwt:Audience"] ?? "TicketPrime";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, cpf),
            new Claim(ClaimTypes.Role, perfil),
            new Claim("perfil", perfil),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expireMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
