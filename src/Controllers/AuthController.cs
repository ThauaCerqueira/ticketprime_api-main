using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using src.DTOs;
using src.Infrastructure.IRepository;
using src.Service;

namespace src.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("geral")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IUsuarioRepository _usuarioRepo;
    private readonly UserService _userService;
    private readonly IConfiguration _configuration;
    private readonly AuditLogService _auditLog;
    private readonly ILogger<AuthController> _logger;
    private readonly JwtBlacklistService _jwtBlacklist;

    public AuthController(
        AuthService authService,
        IUsuarioRepository usuarioRepo,
        UserService userService,
        IConfiguration configuration,
        AuditLogService auditLog,
        ILogger<AuthController> logger,
        JwtBlacklistService jwtBlacklist)
    {
        _authService = authService;
        _usuarioRepo = usuarioRepo;
        _userService = userService;
        _configuration = configuration;
        _auditLog = auditLog;
        _logger = logger;
        _jwtBlacklist = jwtBlacklist;
    }

    /// <summary>
    /// Login — retorna JWT e define cookie httpOnly.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IResult> Login([FromBody] LoginDTO dto)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.FirstOrDefault();

        LoginResponseDTO? resultado;
        try
        {
            resultado = await _authService.LoginAsync(dto, ipAddress, userAgent);
            if (resultado == null)
                return Results.Json(new { mensagem = "CPF ou senha inválidos." }, statusCode: 401);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { mensagem = ex.Message });
        }

        // Define o token JWT como cookie httpOnly (XSS-safe)
        // Se "Lembrar" estiver marcado, o cookie dura 7 dias (igual ao token)
        var expireMinutes = dto.Lembrar ? 10080 : _configuration.GetValue<int>("Jwt:ExpireMinutes", 30);
        if (expireMinutes <= 0) expireMinutes = dto.Lembrar ? 10080 : 30;

        HttpContext.Response.Cookies.Append("ticketprime_token", resultado.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(expireMinutes),
            Path = "/"
        });

        // ═══════════════════════════════════════════════════════════════════
        // SEGURANÇA: Refresh token em cookie httpOnly (NUNCA em localStorage).
        //
        // ANTES: Path = "/api/auth/refresh" (restrito)
        //   Isso impedia o endpoint /api/auth/logout de ler o cookie,
        //   forçando o frontend a enviar o refresh token no body da
        //   requisição de logout — o que anulava a segurança do cookie.
        //
        // AGORA: Path = "/"
        //   O cookie fica acessível para TODOS os endpoints da API,
        //   mas como é httpOnly + Secure + SameSite=Strict, NENHUM
        //   código JavaScript consegue lê-lo. Imune a XSS.
        //
        //   O refresh token nunca mais aparece no body de nenhuma resposta.
        // ═══════════════════════════════════════════════════════════════════
        if (!string.IsNullOrEmpty(resultado.RefreshToken))
        {
            HttpContext.Response.Cookies.Append("ticketprime_refresh", resultado.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                Path = "/"
            });
        }

        return Results.Ok(resultado);
    }

    /// <summary>
    /// Refresh token — renovação silenciosa de sessão.
    /// Aceita o token via cookie httpOnly (preferido) ou via body JSON (fallback para Blazor Server).
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IResult> Refresh([FromBody] RefreshTokenRequestDTO dto)
    {
        // Prioriza o cookie httpOnly; body é fallback para clientes Blazor Server
        var tokenParaUsar = HttpContext.Request.Cookies.TryGetValue("ticketprime_refresh", out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken)
                ? cookieToken
                : dto.RefreshToken;

        if (string.IsNullOrWhiteSpace(tokenParaUsar))
            return Results.BadRequest(new { mensagem = "Refresh token é obrigatório." });

        var refreshDto = new RefreshTokenRequestDTO { RefreshToken = tokenParaUsar };
        var resultado = await _authService.RefreshTokenAsync(refreshDto);
        if (resultado == null)
            return Results.Json(new { mensagem = "Refresh token inválido ou expirado." }, statusCode: 401);

        // Rotaciona o cookie do refresh token (httpOnly — nunca no body)
        HttpContext.Response.Cookies.Append("ticketprime_refresh", resultado.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            Path = "/"
        });

        return Results.Ok(resultado);
    }

    /// <summary>
    /// Logout — revoga o refresh token, invalida o access token via JTI blacklist
    /// e limpa os cookies de sessão.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IResult> Logout([FromBody] RefreshTokenRequestDTO? dto)
    {
        // Tenta ler o refresh token do cookie httpOnly ou do body JSON
        var rawToken = HttpContext.Request.Cookies.TryGetValue("ticketprime_refresh", out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken)
                ? cookieToken
                : dto?.RefreshToken;

        var cpf = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        await _authService.RevogarRefreshTokenAsync(rawToken, cpf);

        // Revoga o access token atual via blacklist de JTI para que não possa
        // ser reutilizado pelo tempo restante de vida do token.
        var jti = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expClaim = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Expiration)
            ?? HttpContext.User.FindFirst("exp");
        if (!string.IsNullOrEmpty(jti))
        {
            var expiry = expClaim is not null && long.TryParse(expClaim.Value, out var expUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime
                : DateTime.UtcNow.AddMinutes(30);
            _jwtBlacklist.Revoke(jti, expiry);
        }

        // Remove cookies de sessão (Path="/" para garantir que ambos sejam limpos)
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UnixEpoch,
            Path = "/"
        };
        HttpContext.Response.Cookies.Append("ticketprime_token", "", cookieOptions);
        HttpContext.Response.Cookies.Append("ticketprime_refresh", "", cookieOptions);

        return Results.Ok(new { mensagem = "Logout realizado com sucesso." });
    }

    /// <summary>
    /// Troca de senha (requer autenticação).
    /// </summary>
    [HttpPost("trocar-senha")]
    [Authorize]
    [EnableRateLimiting("escrita")]
    public async Task<IResult> TrocarSenha([FromBody] TrocarSenhaDTO dto)
    {
        var cpfToken = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(cpfToken))
            return Results.Unauthorized();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.FirstOrDefault();

        var usuario = await _usuarioRepo.ObterPorCpf(dto.Cpf);
        if (usuario == null || !BCrypt.Net.BCrypt.Verify(dto.SenhaAtual, usuario.Senha))
            return Results.Json(new { mensagem = "CPF ou senha atual inválidos." }, statusCode: 401);

        // Verifica que o CPF do token corresponde ao CPF da requisição
        if (cpfToken != dto.Cpf)
            return Results.Forbid();

        // Valida nova senha com o mesmo regex forte usado no cadastro
        if (!Regex.IsMatch(dto.SenhaNova,
                @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$"))
            return Results.BadRequest(new
            {
                mensagem = "A nova senha deve ter no mínimo 8 caracteres, " +
                           "com pelo menos uma letra maiúscula, uma minúscula, um número e um caractere especial."
            });

        var workFactor = _configuration.GetValue<int>("ConfiguracaoBCrypt:WorkFactor", 11);
        var novaSenhaHash = BCrypt.Net.BCrypt.HashPassword(dto.SenhaNova, workFactor: workFactor);

        await _usuarioRepo.AtualizarSenha(dto.Cpf, novaSenhaHash);

        // Registra troca de senha na auditoria
        await _auditLog.LogTrocaSenhaAsync(dto.Cpf, ipAddress ?? "unknown", userAgent);

        return Results.Ok(new { mensagem = "Senha alterada com sucesso." });
    }

    /// <summary>
    /// Solicita reenvio do token de verificação de email.
    /// </summary>
    [HttpPost("solicitar-verificacao-email")]
    [EnableRateLimiting("escrita")]
    public async Task<IResult> SolicitarVerificacaoEmail([FromBody] EmailVerificationRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Results.BadRequest(new { mensagem = "Email é obrigatório." });

        var usuario = await _usuarioRepo.ObterPorEmail(dto.Email);
        if (usuario == null)
            return Results.Ok(new { mensagem = "Se o email existir, um token de verificação será gerado." });

        if (usuario.EmailVerificado)
            return Results.Ok(new { mensagem = "Email já verificado." });

        // O serviço já envia o email via IEmailService (SmtpEmailService ou ConsoleEmailService)
        await _userService.GerarTokenVerificacaoEmail(dto.Email);

        return Results.Ok(new { mensagem = "Token de verificação enviado para o email informado." });
    }

    /// <summary>
    /// Confirma verificação de email com token.
    /// </summary>
    [HttpPost("confirmar-email")]
    [EnableRateLimiting("escrita")]
    public async Task<IResult> ConfirmarEmail([FromBody] EmailConfirmationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Token))
            return Results.BadRequest(new { mensagem = "Email e token são obrigatórios." });

        var usuario = await _usuarioRepo.ObterPorEmail(dto.Email);
        if (usuario == null)
            return Results.NotFound(new { mensagem = "Usuário não encontrado." });

        if (usuario.EmailVerificado)
            return Results.Ok(new { mensagem = "Email já verificado." });

        if (usuario.TokenVerificacaoEmail != dto.Token)
            return Results.BadRequest(new { mensagem = "Token inválido." });

        // Verifica expiração do token (24 horas)
        if (usuario.TokenExpiracaoEmail.HasValue && usuario.TokenExpiracaoEmail.Value < DateTime.UtcNow)
            return Results.BadRequest(new { mensagem = "Token expirado. Solicite um novo." });

        await _usuarioRepo.ConfirmarEmail(dto.Email);

        return Results.Ok(new { mensagem = "Email verificado com sucesso!" });
    }

    /// <summary>
    /// Solicita redefinição de senha — envia token por email.
    /// </summary>
    [HttpPost("solicitar-redefinicao-senha")]
    [EnableRateLimiting("escrita")]
    public async Task<IResult> SolicitarRedefinicaoSenha([FromBody] PasswordResetRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Results.BadRequest(new { mensagem = "Email é obrigatório." });

        // Serviço envia email com token (só se o email existir — prevenção de enumeração)
        await _userService.GerarResetSenhaToken(dto.Email);

        return Results.Ok(new { mensagem = "Se o email existir, um código de redefinição será enviado." });
    }

    /// <summary>
    /// Redefine a senha usando o token recebido por email.
    /// </summary>
    [HttpPost("redefinir-senha")]
    [EnableRateLimiting("escrita")]
    public async Task<IResult> RedefinirSenha([FromBody] PasswordResetDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email)
            || string.IsNullOrWhiteSpace(dto.Token)
            || string.IsNullOrWhiteSpace(dto.NovaSenha))
            return Results.BadRequest(new { mensagem = "Email, token e nova senha são obrigatórios." });

        try
        {
            var sucesso = await _userService.RedefinirSenha(dto.Email, dto.Token, dto.NovaSenha);
            if (!sucesso)
                return Results.BadRequest(new { mensagem = "Token inválido ou expirado. Solicite um novo código de redefinição." });

            // Registra redefinição de senha na auditoria
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.FirstOrDefault();

            var usuario = await _usuarioRepo.ObterPorEmail(dto.Email);
            if (usuario != null)
            {
                await _auditLog.LogRedefinicaoSenhaAsync(usuario.Cpf, ipAddress ?? "unknown", userAgent);
            }

            return Results.Ok(new { mensagem = "Senha redefinida com sucesso!" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { mensagem = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao redefinir senha");
            return Results.Json(new { mensagem = "Erro interno do servidor." }, statusCode: 500);
        }
    }
}
