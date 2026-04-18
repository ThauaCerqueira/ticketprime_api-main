using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using src.DTOs;
using src.Infrastructure.IRepository;

namespace src.Service;

public class AuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IConfiguration _configuration;

    public AuthService(IUsuarioRepository usuarioRepository, IConfiguration configuration)
    {
        _usuarioRepository = usuarioRepository;
        _configuration = configuration;
    }

    public async Task<LoginResponseDTO?> LoginAsync(LoginDTO dto)
    {
        var usuario = await _usuarioRepository.ObterPorCpfESenha(dto.Cpf, dto.Senha);

        if (usuario == null)
            return null;

        var token = GerarToken(usuario.Cpf, usuario.Perfil);

        return new LoginResponseDTO
        {
            Cpf = usuario.Cpf,
            Nome = usuario.Nome,
            Perfil = usuario.Perfil,
            Token = token
        };
    }

    private string GerarToken(string cpf, string perfil)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? "TicketPrimeChaveSecreta2024SuperSegura!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, cpf),
            new Claim(ClaimTypes.Role, perfil),
            new Claim("perfil", perfil)
        };

        var token = new JwtSecurityToken(
            issuer: "TicketPrime",
            audience: "TicketPrime",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}