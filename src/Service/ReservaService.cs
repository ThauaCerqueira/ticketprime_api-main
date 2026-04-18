using src.Models;
using src.Infrastructure.IRepository;
 
namespace src.Service;
 
public class ReservaService
{
    private readonly IReservaRepository _reservaRepository;
    private readonly IEventoRepository _eventoRepository;
 
    public ReservaService(IReservaRepository reservaRepository, IEventoRepository eventoRepository)
    {
        _reservaRepository = reservaRepository;
        _eventoRepository = eventoRepository;
    }
 
    public async Task<Reserva> ComprarIngressoAsync(string usuarioCpf, int eventoId)
    {
        // Verifica se o evento existe
        var evento = await _eventoRepository.ObterPorIdAsync(eventoId);
 
        if (evento == null)
            throw new InvalidOperationException("Evento não encontrado.");
 
        if (evento.CapacidadeTotal <= 0)
            throw new InvalidOperationException("Não há mais vagas disponíveis para este evento.");
 
        if (evento.DataEvento <= DateTime.Now)
            throw new InvalidOperationException("Este evento já aconteceu.");
 
        // Diminui a capacidade do evento
        var diminuiu = await _eventoRepository.DiminuirCapacidadeAsync(eventoId);
 
        if (!diminuiu)
            throw new InvalidOperationException("Não foi possível reservar a vaga. Tente novamente.");
 
        // Cria a reserva
        var reserva = new Reserva
        {
            UsuarioCpf = usuarioCpf,
            EventoId = eventoId
        };
 
        return await _reservaRepository.CriarAsync(reserva);
    }
 
    public async Task<IEnumerable<Reserva>> ListarReservasUsuarioAsync(string cpf)
    {
        return await _reservaRepository.ListarPorUsuarioAsync(cpf);
    }
}