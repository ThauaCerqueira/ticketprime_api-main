using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;
using TicketPrime.Web.Client;
using TicketPrime.Web.Client.Services;
using TicketPrime.Web.Client.Validators;
using FluentValidation;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// MudBlazor
builder.Services.AddMudServices();

// Session (Singleton): precisa ser compartilhada entre componentes e
// handlers do HttpClientFactory para propagar o mesmo JWT.
builder.Services.AddSingleton<SessionService>();

// CryptoService
builder.Services.AddScoped<CryptoService>();

// HealthCheckService
builder.Services.AddSingleton<HealthCheckService>();

// HTTP Client for API calls with JWT authentication and retry policy
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5164";
builder.Services.AddScoped<AuthHttpClientHandler>();

// "TicketPrimeApi" — HttpClient com Polly + AuthHttpMessageHandler
builder.Services.AddHttpClient("TicketPrimeApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<AuthHttpClientHandler>()
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromMilliseconds(400);
    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.Retry.ShouldHandle = static args =>
    {
        if (args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            return ValueTask.FromResult(false);
        if (args.Outcome.Exception != null)
            return ValueTask.FromResult(true);
        return ValueTask.FromResult(false);
    };
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    options.CircuitBreaker.MinimumThroughput = 5;
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(25);
});

// Garante que @inject HttpClient use o cliente nomeado TicketPrimeApi.
builder.Services.AddScoped<HttpClient>(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("TicketPrimeApi"));

// CupomService
builder.Services.AddScoped(sp =>
{
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var http = httpFactory.CreateClient("TicketPrimeApi");
    return new CupomService(http);
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

await builder.Build().RunAsync();
