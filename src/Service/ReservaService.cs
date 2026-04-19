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
        var evento = await _eventoRepository.ObterPorIdAsync(eventoId);
 
        if (evento == null)
            throw new InvalidOperationException("Evento não encontrado.");
 
        if (evento.CapacidadeTotal <= 0)
            throw new InvalidOperationException("Não há mais vagas disponíveis para este evento.");
 
        if (evento.DataEvento <= DateTime.Now)
            throw new InvalidOperationException("Este evento já aconteceu.");
 
        var diminuiu = await _eventoRepository.DiminuirCapacidadeAsync(eventoId);
 
        if (!diminuiu)
            throw new InvalidOperationException("Não foi possível reservar a vaga. Tente novamente.");
 
        var reserva = new Reserva
        {
            UsuarioCpf = usuarioCpf,
            EventoId = eventoId
        };
 
        return await _reservaRepository.CriarAsync(reserva);
    }
 
    public async Task<IEnumerable<ReservaDetalhadaDTO>> ListarReservasUsuarioAsync(string cpf)
    {
        return await _reservaRepository.ListarPorUsuarioAsync(cpf);
    }
 
    public async Task CancelarIngressoAsync(int reservaId, string usuarioCpf)
    {
        // Busca a reserva para pegar o eventoId antes de deletar
        var reservas = await _reservaRepository.ListarPorUsuarioAsync(usuarioCpf);
        var reserva = reservas.FirstOrDefault(r => r.Id == reservaId);
 
        if (reserva == null)
            throw new InvalidOperationException("Reserva não encontrada.");
 
        // Cancela a reserva
        var cancelou = await _reservaRepository.CancelarAsync(reservaId, usuarioCpf);
 
        if (!cancelou)
            throw new InvalidOperationException("Não foi possível cancelar a reserva.");
 
        // Devolve a vaga ao evento
        await _eventoRepository.AumentarCapacidadeAsync(reserva.EventoId);
    }
}
 