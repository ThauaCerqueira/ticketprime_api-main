using src.Models;
 
namespace src.Infrastructure.IRepository;
 
public interface IReservaRepository
{
    Task<Reserva> CriarAsync(Reserva reserva);
    Task<IEnumerable<ReservaDetalhadaDTO>> ListarPorUsuarioAsync(string cpf);
    Task<bool> CancelarAsync(int reservaId, string usuarioCpf);
}