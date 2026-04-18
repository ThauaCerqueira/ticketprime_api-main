using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using src.DTOs;
using src.Service;
 
namespace src.Controllers;
 
[ApiController]
[Route("api/[controller]")]
public class CupomController : ControllerBase
{
    private readonly CupomService _cupomService;
 
    public CupomController(CupomService cupomService)
    {
        _cupomService = cupomService;
    }
 
    [HttpPost]
    [Authorize(Roles = "ADMIN")] // Somente ADMIN pode criar cupons
    public async Task<IActionResult> Criar([FromBody] CriarCupomDTO dto)
    {
        try
        {
            var sucesso = await _cupomService.CriarAsync(dto);
 
            if (!sucesso)
                return BadRequest("Não foi possível criar o cupom.");
 
            return CreatedAtAction(nameof(ObterPorCodigo), new { codigo = dto.Codigo }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Erro interno no servidor.");
        }
    }
 
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var cupons = await _cupomService.ListarAsync();
        return Ok(cupons);
    }
 
    [HttpGet("{codigo}")]
    public async Task<IActionResult> ObterPorCodigo(string codigo)
    {
        var cupom = await _cupomService.ObterPorCodigoAsync(codigo);
 
        if (cupom == null)
            return NotFound($"Cupom com código '{codigo}' não encontrado.");
 
        return Ok(cupom);
    }
 
    [HttpDelete("{codigo}")]
    [Authorize(Roles = "ADMIN")] // Somente ADMIN pode deletar cupons
    public async Task<IActionResult> Deletar(string codigo)
    {
        var removido = await _cupomService.DeletarAsync(codigo);
 
        if (!removido)
            return NotFound("Cupom não encontrado ou não pôde ser removido.");
 
        return NoContent();
    }
}