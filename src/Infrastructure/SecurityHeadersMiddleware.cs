using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using src.Service;

namespace src.Infrastructure;

/// <summary>
/// Middleware que adiciona headers de segurança em todas as respostas HTTP.
/// Extraído do Program.cs para manter a configuração organizada.
/// 
/// Headers aplicados:
/// - X-Content-Type-Options: nosniff
/// - X-Frame-Options: DENY
/// - Referrer-Policy: strict-origin-when-cross-origin
/// - Permissions-Policy: câmera, microfone e geolocalização bloqueados
/// - Content-Security-Policy: política restrita de conteúdo
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _csp;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;

        // Em desenvolvimento permite conexões ao localhost (dev do frontend/API).
        // Em produção, apenas 'self' + origens externas legítimas.
        var devConnectSrc = env.IsDevelopment()
            ? "http://localhost:5164 http://localhost:5194 "
            : string.Empty;

        _csp =
            "default-src 'self'; " +
            "script-src 'self' https://sdk.mercadopago.com https://mpgsdk.mercadopago.com; " +
            "style-src 'self' 'unsafe-inline' https://mpgsdk.mercadopago.com; " +
            "img-src 'self' data: blob:; " +
            $"connect-src 'self' {devConnectSrc}https://api.mercadopago.com https://sdk.mercadopago.com; " +
            "frame-src 'self' https://mpgsdk.mercadopago.com; " +
            "frame-ancestors 'none'; " +
            "form-action 'self'; " +
            "base-uri 'self';";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // 'unsafe-inline' removido de script-src para fortalecer proteção XSS.
        // O SDK do MercadoPago é permitido por origem (https://sdk.mercadopago.com).
        // 'unsafe-inline' mantido em style-src por compatibilidade com Blazor
        // (estilos inline gerados pelo framework em tempo de execução).
        context.Response.Headers["Content-Security-Policy"] = _csp;

        await _next(context);
    }
}
