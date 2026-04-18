using src.Repositories;
using src.Models;
using src.DTOs;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using src.Infrastructure;
using src.Infrastructure.Repository;
using src.Infrastructure.IRepository;
using src.Service;

namespace TicketPrime.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? "Server=localhost;Database=TicketPrime;Integrated Security=True;TrustServerCertificate=True;";

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var jwtKey = builder.Configuration["Jwt:Key"] ?? "TicketPrimeChaveSecreta2024SuperSegura!";
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = "TicketPrime",
                        ValidAudience = "TicketPrime",
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                    };
                });

            builder.Services.AddAuthorization();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
            builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
            builder.Services.AddScoped<ICupomRepository, CupomRepository>();
            builder.Services.AddScoped<IEventoRepository, EventoRepository>();
            builder.Services.AddScoped<UsuarioService>();
            builder.Services.AddScoped<CupomService>();
            builder.Services.AddScoped<EventoService>();
            builder.Services.AddScoped<AuthService>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseCors("AllowAll");
            app.UseAuthentication();
            app.UseAuthorization();

            // POST /api/eventos — Cadastra um novo evento (somente ADMIN)
            app.MapPost("/api/eventos", async (CriarEventoDTO dto, EventoService service) =>
            {
                try
                {
                    var resultado = await service.CriarNovoEvento(dto);
                    if (resultado == null)
                        return Results.BadRequest("Erro ao criar evento.");

                    return Results.Created($"/api/eventos/{resultado.Id}", resultado);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { mensagem = ex.Message });
                }
            }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

            // GET /api/eventos — Lista os eventos disponíveis
            app.MapGet("/api/eventos", async (EventoService service) =>
            {
                var eventos = await service.ListarEventos();
                return Results.Ok(eventos);
            });

            // POST /api/cupons — Cadastra um novo cupom (somente ADMIN)
            app.MapPost("/api/cupons", async (CriarCupomDTO dto, CupomService service) =>
            {
                try
                {
                    var sucesso = await service.CriarAsync(dto);
                    if (sucesso)
                        return Results.Created($"/api/cupons/{dto.Codigo}", dto);

                    return Results.BadRequest("Não foi possível criar o cupom.");
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { mensagem = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { mensagem = ex.Message });
                }
            }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

            // POST /api/usuarios — Cadastra um novo usuário
            app.MapPost("/api/usuarios", async (Usuario usuario, UsuarioService service) =>
            {
                try
                {
                    var resultado = await service.CadastrarUsuario(usuario);
                    return Results.Created($"/api/usuarios/{resultado.Cpf}", resultado);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { mensagem = ex.Message });
                }
            });

            // POST /api/auth/login — Login
            app.MapPost("/api/auth/login", async (LoginDTO dto, AuthService service) =>
            {
                var resultado = await service.LoginAsync(dto);
                if (resultado == null)
                    return Results.Unauthorized();

                return Results.Ok(resultado);
            });

            app.Run();
        }
    }
}
