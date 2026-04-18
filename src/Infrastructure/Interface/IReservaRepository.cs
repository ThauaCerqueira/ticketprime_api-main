using src.Models;
 
namespace src.Infrastructure.IRepository;
 
public interface IReservaRepository
{
    Task<Reserva> CriarAsync(Reserva reserva);
    Task<IEnumerable<Reserva>> ListarPorUsuarioAsync(string cpf);
}