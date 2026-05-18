using MudBlazor.Services;
using TicketPrime.Web.Client.Services;
using TicketPrime.Web.Components;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();

// Serviços compartilhados com o Client (necessários para pré-renderização)
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<CryptoService>();
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddScoped<AuthHttpClientHandler>();

var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5164";
builder.Services.AddHttpClient("TicketPrimeApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler(options =>
{
    options.CircuitBreaker.MinimumThroughput = 5;
});

// HttpClient padrão para componentes Blazor (pré-renderização)
builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri(apiBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    return client;
});

// FluentValidation (mesmos validators do Client, necessários para pré-renderização)
builder.Services.AddValidatorsFromAssemblyContaining<TicketPrime.Web.Client.Validators.RegistrationRequestValidator>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Nota: Não usar UseStatusCodePagesWithReExecute("/not-found") pois conflita
// com a rota @page "/not-found" do Blazor, causando AmbiguousMatchException.
// O Blazor já gerencia páginas não encontradas via Router + NotFound.razor.
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(TicketPrime.Web.Client._Imports).Assembly);

app.Run();
