using Microsoft.AspNetCore.Mvc;
using src.DTOs;
using src.Service;
 
namespace src.Controllers;
 
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
 
    public AuthController(AuthService authService)
    {
        _authService = authService;
    }
 
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDTO dto)
    {
        var resultado = await _authService.LoginAsync(dto);
 
        if (resultado == null)
            return Unauthorized(new { mensagem = "CPF ou senha inválidos." });
 
        return Ok(resultado);
    }
}