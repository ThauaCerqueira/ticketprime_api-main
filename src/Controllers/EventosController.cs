using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using src.DTOs;
using src.Service;

namespace src.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventosController : ControllerBase
{
    private readonly EventoService _eventoService;

    public EventosController(EventoService eventoService)
    {
        _eventoService = eventoService;
    }

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> CriarEvento([FromBody] CriarEventoDTO eventoDTO)
    {
        try
        {
            var novoEvento = await _eventoService.CriarNovoEvento(eventoDTO);
            if (novoEvento == null)
                return BadRequest("Não foi possível criar o evento.");

            return CreatedAtAction(nameof(CriarEvento),
                new { id = novoEvento.Id },
                new { Mensagem = "Evento criado com sucesso!", Dados = novoEvento });
        }
        catch (Exception ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var eventos = await _eventoService.ListarEventos();
        return Ok(eventos);
    }

    [HttpGet("disponiveis")]
    public async Task<IActionResult> GetDisponiveis()
    {
        var eventos = await _eventoService.ListarEventosDisponiveis();
        return Ok(eventos);
    }
}