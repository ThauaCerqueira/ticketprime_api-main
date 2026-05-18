using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using src.Infrastructure;
using src.Infrastructure.IRepository;
using src.Infrastructure.Repository;
using src.Service;

namespace src;

public class Program
{
    private static ILogger<Program>? _logger;

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ════════════════════════════════════════════════════════════
        //  Structured logging via built-in JsonConsole provider
        // ════════════════════════════════════════════════════════════
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";
        });

        // ════════════════════════════════════════════════════════════
        //  HashiCorp Vault — secrets externas (chaves cripto, etc.)
        //
        //  Carrega secrets do Vault ANTES de qualquer outro serviço,
        //  para que fiquem disponíveis via IConfiguration.
        //  Se o Vault não estiver acessível (desenvolvimento), a
        //  aplicação continua com appsettings.json / env vars.
        //
        //  Configuração necessária em appsettings.json:
        //    "Vault": {
        //      "Address": "https://vault:8200",
        //      "AuthMethod": "token",
        //      "Token": "hvs...",
        //      "MountPoint": "secret",
        //      "SecretPath": "ticketprime/crypto"
        //    }
        // ════════════════════════════════════════════════════════════
        var vaultOptions = builder.Configuration
            .GetSection(VaultOptions.SectionName)
            .Get<VaultOptions>();

        if (vaultOptions != null)
        {
            // VaultConfigurationProvider.Load() é chamado automaticamente
            // durante a construção do host, antes de qualquer serviço.
            // As secrets ficam disponíveis via IConfiguration.
            builder.Configuration.AddVaultConfiguration(vaultOptions);
        }

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' não encontrada. Configure em appsettings.json ou User Secrets.");

        // Initialize database
        InitializeDatabase(connectionString);

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                    ?? new[] { "http://localhost:5194" };
                policy.WithOrigins(origins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        var jwtKey = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Chave JWT 'Jwt:Key' não encontrada. Configure via variável de ambiente 'Jwt__Key', " +
                "User Secrets (Development) ou o arquivo appsettings.json. " +
                "Use uma chave de no mínimo 32 caracteres (256 bits) para HMAC-SHA256.");

        if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
            throw new InvalidOperationException(
                "A chave JWT ('Jwt:Key') deve ter no mínimo 32 caracteres (256 bits) para HMAC-SHA256. " +
                "Configure-a via variável de ambiente 'Jwt__Key', User Secrets (dotnet user-secrets) " +
                "ou o arquivo appsettings.Development.json (que é ignorado pelo git). " +
                "NUNCA utilize a chave padrão do repositório em produção.");

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "TicketPrime",
                    ValidAudience = builder.Configuration["Jwt:Audience"] ?? "TicketPrime",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };

                // Suporta token vindo de cookie httpOnly (ticketprime_token) como fallback
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Prioriza explicitamente o token Bearer enviado no header.
                        // O cookie httpOnly é apenas fallback para clientes que não
                        // conseguem enviar Authorization.
                        var authHeader = context.Request.Headers.Authorization.ToString();
                        if (!string.IsNullOrWhiteSpace(authHeader) &&
                            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            return Task.CompletedTask;
                        }

                        var token = context.Request.Cookies["ticketprime_token"];
                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var blacklist = context.HttpContext.RequestServices
                            .GetRequiredService<JwtBlacklistService>();
                        var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                        if (!string.IsNullOrEmpty(jti) && blacklist.IsRevoked(jti))
                        {
                            context.Fail("Token revogado.");
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();

        // ── CSRF ────────────────────────────────────────────────────────────
        // Nota: Não usamos UseAntiforgery() global porque a API usa JWT Bearer
        // Authentication (via header Authorization: Bearer <token>) e cookie
        // httpOnly com SameSite=Strict, que são inerentemente imunes a CSRF.
        // O AddAntiforgery fica registrado para uso opcional em endpoints
        // específicos que possam usar autenticação baseada em cookie puro.
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.SuppressXFrameOptionsHeader = false;
        });

        // ── Rate Limiters com particionamento por usuário ───────────────────
        //
        // ANTES (problemático): FixedWindowLimiter particionava apenas por IP.
        //   Um atacante com botnet de múltiplos IPs conseguia fazer scalping
        //   de ingressos sem limitação efetiva por conta.
        //
        // AGORA (corrigido): Usamos AddPerUserPolicy que particiona por:
        //   - Usuário autenticado: chave = "user_{CPF}" (via ClaimTypes.NameIdentifier)
        //   - Anônimo: chave = "ip_{endereço}" (fallback)
        //   - Admin: limite mais alto (não afeta operações administrativas)
        //
        // Isso garante que mesmo com múltiplos IPs, cada conta fica
        // limitada individualmente.
        builder.Services.AddRateLimiter(options =>
        {
            // Login: 5 tentativas/minuto (por IP — endpoint não autenticado)
            options.AddFixedWindowLimiter("login", o =>
            {
                o.Window = TimeSpan.FromMinutes(1);
                o.PermitLimit = 5;
                o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 0;
            });

            // Compra de ingressos: 3/min por usuário, 1/min anônimo, 30/min admin
            // Policy per-user — protege contra scalping via botnet
            options.AddPerUserCompraPolicy("compra-ingresso");

            // Escrita (criação de eventos, cupons): 10/min por usuário, 100/min admin
            // Policy per-user — evita abuso mesmo com IPs diferentes
            options.AddPerUserPolicy("escrita",
                anonymousLimit: 3,
                authenticatedLimit: 10,
                adminLimit: 100,
                window: TimeSpan.FromMinutes(1));

            // Geral (leituras, listagens): 60/min por usuário, 300/min admin
            // Policy per-user — distribui carga entre contas genuínas
            options.AddPerUserPolicy("geral",
                anonymousLimit: 30,
                authenticatedLimit: 60,
                adminLimit: 300,
                window: TimeSpan.FromMinutes(1));

            // Webhook MercadoPago: 300/min por IP — proteção DoS sem bloquear retries
            // legítimos. A segurança real vem da validação HMAC-SHA256 do header X-Signature.
            options.AddFixedWindowLimiter("webhook", o =>
            {
                o.Window = TimeSpan.FromMinutes(1);
                o.PermitLimit = 300;
                o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 0;
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        // ════════════════════════════════════════════════════════════
        //  Swagger / OpenAPI — documentação disponível em todos os
        //  ambientes para facilitar debugging e integração.
        // ════════════════════════════════════════════════════════════
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            // Define metadata da API
            options.SwaggerDoc("v1", new()
            {
                Title = "TicketPrime API",
                Version = "v1",
                Description = "API de gerenciamento de eventos e ingressos da TicketPrime. " +
                              "Fornece endpoints para autenticação, criação de eventos, " +
                              "compra de ingressos, gerenciamento de cupons e muito mais."
            });

            // Inclui XML comments dos controllers/endpoints (se existirem)
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        // ── DI Registrations ───────────────────────────────────────────────
        builder.Services.AddSingleton(new DbConnectionFactory(connectionString));

        // ═══════════════════════════════════════════════════════════════════
        //  Cache Distribuído (Redis com fallback para memória local)
        //
        //  ANTES: AddMemoryCache() — single-instance apenas.
        //    Cada réplica da API tinha seu próprio cache, causando
        //    inconsistência de dados em deployments multi-instância.
        //
        //  AGORA: AddTicketPrimeCache()
        //    Usa Redis se configurado (Redis:Connection ou Redis__Connection).
        //    Faz fallback para DistributedMemoryCache se Redis não configurado.
        //    Compatível com escalabilidade horizontal (múltiplas réplicas).
        //
        //  Configuração via appsettings.json ou variável de ambiente:
        //    "Redis:Connection": "localhost:6379"
        //    Redis__Connection=localhost:6379  (Docker)
        // ═══════════════════════════════════════════════════════════════════
        builder.Services.AddTicketPrimeCache(builder.Configuration);
        // Mantém IMemoryCache para caches locais de curta duração (ex: thumbnails)
        builder.Services.AddMemoryCache();

        // ═══════════════════════════════════════════════════════════════════
        //  OpenTelemetry — observabilidade estruturada
        //
        //  ANTES: Apenas logs JSON no console. Sem métricas exportáveis.
        //    Em produção, time de DevOps opera no escuro — sem alertas,
        //    sem dashboards, sem rastreamento de lentidão.
        //
        //  AGORA: OpenTelemetry com:
        //    - Métricas HTTP (taxa de requisição, latência, erros)
        //    - Métricas de runtime (GC, memória, threads)
        //    - Métricas de HttpClient (chamadas externas)
        //    - Exportação Prometheus em /metrics (já existente)
        //    - Resource attributes para identificar a instância
        //
        //  Futuro: Adicionar OTLP exporter para enviar para Grafana/NewRelic/etc.
        // ═══════════════════════════════════════════════════════════════════
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("TicketPrime",
                    serviceVersion: "1.0.0",
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()   // Métricas HTTP (duração, taxa, status)
                .AddHttpClientInstrumentation()    // Métricas de chamadas externas (MercadoPago, etc.)
                .AddRuntimeInstrumentation()       // Métricas .NET runtime (GC, CPU, memória)
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddMeter("System.Net.Http")
                // Prometheus exporter — expõe em /metrics (administrativo)
                .AddPrometheusExporter());

        builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        builder.Services.AddScoped<ICupomRepository, CupomRepository>();
        builder.Services.AddScoped<IEventoRepository, EventoRepository>();
        builder.Services.AddScoped<IReservaRepository, ReservaRepository>();
        builder.Services.AddScoped<IFilaEsperaRepository, FilaEsperaRepository>();
        builder.Services.AddScoped<IFavoritoRepository, FavoritoRepository>();
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<CupomService>();
        builder.Services.AddScoped<EventoService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<ITransacaoCompraExecutor, TransacaoCompraExecutor>();
        builder.Services.AddScoped<ReservationService>();
        builder.Services.AddScoped<IWaitingQueueService, WaitingQueueService>();
        builder.Services.AddSingleton<CryptoKeyService>();
        builder.Services.AddSingleton<PixCryptoService>();
        builder.Services.AddSingleton<MetricsService>();
        builder.Services.AddSingleton<JwtBlacklistService>();
        builder.Services.AddHostedService<RefreshTokenCleanupService>();
        builder.Services.AddSingleton<BackgroundEmailService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundEmailService>());

        // ── Webhook Security ────────────────────────────────────────────────
        // Validador de assinatura HMAC-SHA256 para webhooks do MercadoPago.
        // Configuração: MercadoPago:WebhookSecret (ou MercadoPago__WebhookSecret)
        builder.Services.AddSingleton<MercadoPagoWebhookValidator>();

        // ── Admin Security ──────────────────────────────────────────────────
        // ═══════════════════════════════════════════════════════════════════
        // Verifica na inicialização se o admin padrão ainda está com senha
        // original ('admin123'). Em produção, bloqueia endpoints admin até
        // que a senha seja trocada.
        // ═══════════════════════════════════════════════════════════════════
        builder.Services.AddScoped<AdminSecurityService>();

        // ── Gateway de pagamento ────────────────────────────────────────────
        var mpToken = builder.Configuration["MercadoPago:AccessToken"];

        // ═══════════════════════════════════════════════════════════════════
        // SEGURANÇA: Validação obrigatória em produção
        // Se estamos em produção (ASPNETCORE_ENVIRONMENT=Production) e o
        // token do MercadoPago não foi configurado, a aplicação NÃO INICIA.
        // Isso evita o uso acidental do SimulatedPaymentGateway em produção.
        // ═══════════════════════════════════════════════════════════════════
        if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(mpToken))
        {
            throw new InvalidOperationException(
                "MercadoPago AccessToken é OBRIGATÓRIO em produção. " +
                "Configure a variável de ambiente 'MercadoPago__AccessToken' " +
                "com o token de acesso da sua conta Mercado Pago. " +
                "NUNCA utilize SimulatedPaymentGateway em ambiente de produção.");
        }

        if (!string.IsNullOrWhiteSpace(mpToken))
        {
            builder.Services.AddHttpClient("MercadoPago", c =>
            {
                c.BaseAddress = new Uri("https://api.mercadopago.com/");
                c.Timeout = TimeSpan.FromSeconds(30);
                c.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", mpToken);
            });
            builder.Services.AddScoped<IPaymentGateway, MercadoPagoPaymentGateway>();
        }
        else
        {
            builder.Services.AddSingleton<IPaymentGateway>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SimulatedPaymentGateway>>();
                return new SimulatedPaymentGateway(logger);
            });
        }

        // ── Armazenamento de arquivos ───────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Minio:Endpoint"]))
        {
            builder.Services.AddScoped<IStorageService, MinioStorageService>();
            Console.WriteLine($"[Storage] MinIO ({builder.Configuration["Minio:Endpoint"]})");
        }
        else
        {
            builder.Services.AddScoped<IStorageService, LocalFileStorageService>();
            Console.WriteLine("[Storage] LocalFileStorageService (fallback)");
        }

        // ── Meia-entrada (Lei 12.933/2013) ─────────────────────────────────
        builder.Services.AddScoped<IMeiaEntradaRepository, MeiaEntradaRepository>();
        builder.Services.AddScoped<IMeiaEntradaStorageService, LocalMeiaEntradaStorageService>();

        // ── Audit Log (Financial Audit Trail) ──────────────────────────────
        builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        builder.Services.AddScoped<AuditLogService>();

        // ── Avaliações ──────────────────────────────────────────────────────
        builder.Services.AddScoped<IAvaliacaoRepository, AvaliacaoRepository>();
        builder.Services.AddScoped<AvaliacaoService>();

        // Email template service para emails transacionais (depende de IEmailService)
        builder.Services.AddSingleton<EmailTemplateService>();

        // Email service: usa SmtpEmailService se configurado, senão ConsoleEmailService (dev)
        var smtpHost = builder.Configuration["EmailSettings:SmtpHost"];

        // ═══════════════════════════════════════════════════════════════════
        // SEGURANÇA: Validação de SMTP em produção
        // Se estamos em produção e o SMTP não foi configurado, a aplicação
        // NÃO INICIA. Emails transacionais são essenciais para:
        //   - Confirmação de cadastro (verificação de email)
        //   - Confirmação de compra
        //   - Notificação de cancelamento
        //   - Notificação de vaga na fila de espera
        // ═══════════════════════════════════════════════════════════════════
        if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(smtpHost))
        {
            throw new InvalidOperationException(
                "SMTP é OBRIGATÓRIO em produção. " +
                "Configure as variáveis de ambiente 'EmailSettings__SmtpHost', " +
                "'EmailSettings__SmtpPort', 'EmailSettings__SmtpUsername', " +
                "'EmailSettings__SmtpPassword' e 'EmailSettings__FromEmail' " +
                "com os dados do seu servidor SMTP. " +
                "O ConsoleEmailService não é adequado para ambientes de produção.");
        }

        // ═══════════════════════════════════════════════════════════════════
        // SEGURANÇA: InMemoryEmailStore só é registrado em não-produção
        //   Para evitar vazamento de dados de email em produção, o store
        //   em memória não é registrado quando SMTP está configurado.
        // ═══════════════════════════════════════════════════════════════════
        if (!builder.Environment.IsProduction() || string.IsNullOrWhiteSpace(smtpHost))
        {
            builder.Services.AddSingleton<InMemoryEmailStore>();
        }

        if (!string.IsNullOrEmpty(smtpHost))
        {
            builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
        }
        else
        {
            builder.Services.AddSingleton<IEmailService, ConsoleEmailService>();
        }

        // ── Controllers ────────────────────────────────────────────────────
        builder.Services.AddControllers();

        var app = builder.Build();

        // Logger disponível a partir daqui para logs estruturados
        _logger = app.Services.GetRequiredService<ILogger<Program>>();

        // ═══════════════════════════════════════════════════════════════════
        //  Admin Security Check — Startup
        //
        //  Verifica se o admin padrão (CPF 00000000191) ainda está com a
        //  senha original 'admin123'. Se estiver, emite um alerta nos logs
        //  e, em produção, o middleware AdminPasswordChangeMiddleware
        //  bloqueia endpoints administrativos até a troca.
        // ═══════════════════════════════════════════════════════════════════
        try
        {
            var adminSecurity = app.Services.GetRequiredService<AdminSecurityService>();
            await adminSecurity.CheckAndForcePasswordChangeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar segurança do admin na inicialização (não crítico).");
        }

        // ════════════════════════════════════════════════════════════
        //  Swagger — apenas em Development e Staging.
        //  Em produção (ASPNETCORE_ENVIRONMENT=Production) o endpoint
        //  /swagger não é exposto para evitar vazamento de contratos.
        // ════════════════════════════════════════════════════════════
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "TicketPrime API v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "TicketPrime API — Documentação Swagger";
            });
        }

        // Startup diagnostics via ILogger estruturado
        _logger.LogInformation(
            "PaymentGateway: {Gateway}",
            mpToken is { Length: > 0 } ? "MercadoPago (produção)" : "SimulatedPaymentGateway (desenvolvimento)");
        _logger.LogInformation(
            "EmailService: {Service}",
            smtpHost is { Length: > 0 } ? "SmtpEmailService (SMTP configurado)" : "ConsoleEmailService (modo dev — emails exibidos no console)");

        // HTTPS enforcement (apenas em produção — em staging/dev, pode não haver TLS)
        if (app.Environment.IsProduction())
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }

        // ── CORS (deve vir antes de SecurityHeaders, RateLimiter e Routing) ──
        app.UseCors("AllowFrontend");

        // ── Preflight CORS: retorna 204 para OPTIONS (CORS middleware não short-circuit) ──
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = 204;
                return;
            }
            await next();
        });

        // ── Security Headers ─────────────────────────────────────────────────
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        // ═══════════════════════════════════════════════════════════════════
        //  Admin Password Change Middleware
        //
        //  Em produção, BLOQUEIA endpoints administrativos se o admin
        //  ainda estiver com a senha padrão ('admin123').
        //  O admin é forçado a trocar a senha em /trocar-senha antes
        //  de acessar qualquer funcionalidade administrativa.
        // ═══════════════════════════════════════════════════════════════════
        app.UseMiddleware<AdminPasswordChangeMiddleware>();

        // ── Metrics Middleware (extraído para MetricsMiddleware.cs) ──────────
        app.UseMiddleware<MetricsMiddleware>();

        // ═══════════════════════════════════════════════════════════════════
        //  OpenTelemetry Prometheus Scraping Endpoint
        //
        //  Expõe métricas no formato Prometheus em GET /metrics.
        //  Requer autenticação ADMIN (já configurada no HealthController).
        //
        //  ANTES: /metrics gerava texto manualmente via MetricsService.
        //  AGORA: /metrics usa OpenTelemetry + MetricsService combinados.
        // ═══════════════════════════════════════════════════════════════════
        app.UseOpenTelemetryPrometheusScrapingEndpoint(
            app.Services.GetRequiredService<MeterProvider>(),
            predicate: null,
            path: "/metrics",
            configureBranchedPipeline: branch =>
            {
                branch.Use(async (context, next) =>
                {
                    // Só expõe métricas para ADMIN autenticado.
                    if (context.User?.Identity?.IsAuthenticated != true ||
                        !context.User.IsInRole("ADMIN"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsync("Forbidden");
                        return;
                    }

                    await next();
                });
            },
            optionsName: null);

        // ── Controllers ────────────────────────────────────────────────────
        app.MapControllers();

        app.Run();
    }

    private static void InitializeDatabase(string connectionString)
    {
        // ═══════════════════════════════════════════════════════════════
        // SEGURANÇA: Não executa script.sql em produção.
        //   Em produção, o banco já deve estar provisionado via
        //   migrations versionadas (DbUp/Flyway) ou scripts manuais.
        //   Este método só executa em Development.
        // ═══════════════════════════════════════════════════════════════
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (env == "Production")
        {
            Console.WriteLine("[DB Init] Production mode detected. Skipping database initialization.");
            return;
        }

        // Antes do builder.Build(), usamos Console.Write como fallback
        // (ILogger ainda não está disponível)
        // Try multiple candidate paths — the relative ../db path works for local dev
        // (cwd = src/), but breaks in Docker where WORKDIR is /app and db/ is a sibling.
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "db", "script.sql"),              // Docker: /app/db/script.sql
            Path.Combine(AppContext.BaseDirectory, "..", "db", "script.sql"),        // fallback
            Path.Combine(Directory.GetCurrentDirectory(), "db", "script.sql"),       // cwd/db
            Path.Combine(Directory.GetCurrentDirectory(), "..", "db", "script.sql"), // local dev (src/ -> db/)
        };

        var scriptPath = candidatePaths.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

        Console.WriteLine($"[DB Init] Looking for database script...");
        foreach (var p in candidatePaths.Select(Path.GetFullPath))
            Console.WriteLine($"[DB Init]   {(File.Exists(p) ? "FOUND" : "     ")} {p}");

        if (scriptPath == null)
        {
            Console.WriteLine("[DB Init] Warning: Database script not found in any candidate path. Skipping init.");
            return;
        }

        Console.WriteLine($"[DB Init] Using: {scriptPath}");

        try
        {
            var sqlScript = File.ReadAllText(scriptPath);

            // Split on lines that contain only "GO" (case-insensitive, with optional whitespace/semicolons).
            // This is far more robust than hardcoding newline variants and catches edge cases like
            // trailing whitespace, mixed line endings, or blank lines between GO and the next statement.
            var statements = Regex.Split(
                sqlScript,
                @"^\s*GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            // Change connection string to use master database for initial setup
            var masterConnectionString = connectionString.Replace("Database=TicketPrime", "Database=master");

            using var connection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString);
            connection.Open();
            Console.WriteLine("[DB Init] ✓ Connected to SQL Server for database initialization");

            foreach (var statement in statements)
            {
                var trimmedStatement = statement.Trim();
                if (trimmedStatement.Length == 0)
                    continue;

                // ── Skip standalone "GO" statements that the regex split didn't catch ──
                // Edge cases like trailing "GO" at end-of-file or "GO" inside dynamic SQL
                // can leave a stray "GO" token. Sending it to SQL Server would cause:
                //   "Could not find stored procedure 'GO'".
                if (string.Equals(trimmedStatement, "GO", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmedStatement.TrimEnd(';'), "GO", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var command = connection.CreateCommand();
                command.CommandText = trimmedStatement;
                command.CommandTimeout = 60;

                try
                {
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB Init] ⚠ Database setup notice: {ex.Message}");
                }
            }

            connection.Close();
            Console.WriteLine("[DB Init] ✓ Database initialization completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB Init] ⚠ Warning during database setup: {ex.Message}");
        }
    }
}
