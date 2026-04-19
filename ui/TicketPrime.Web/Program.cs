using TicketPrime.Web.Components;
using TicketPrime.Web.Services;
using src.Service;
using src.Repositories;
using src.Infrastructure;
using src.Infrastructure.IRepository;
 
var builder = WebApplication.CreateBuilder(args);
 
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost;Database=TicketPrime;Integrated Security=True;TrustServerCertificate=True;";
 
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
 
builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddScoped<ICupomRepository, CupomRepository>();
builder.Services.AddScoped<CupomService>();
 
// SessionService como Scoped para manter estado por sessão do usuário
builder.Services.AddSingleton<SessionService>();
 
builder.Services.AddTransient(sp =>
{
    var session = sp.GetRequiredService<SessionService>();
    var client = new HttpClient { BaseAddress = new Uri("http://localhost:5164") };

    if (!string.IsNullOrEmpty(session.Token))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);

    return client;
});
 
var app = builder.Build();
 
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
 
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
 
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
 
app.Run();