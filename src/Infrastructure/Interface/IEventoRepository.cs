using src.Models;
 
namespace src.Infrastructure.IRepository;
 
public interface IEventoRepository
{
    Task AdicionarAsync(Evento evento);
    Task<IEnumerable<Evento>> ObterTodosAsync();
    Task<IEnumerable<Evento>> ObterDisponiveisAsync();
    Task<Evento?> ObterPorIdAsync(int id);
    Task<bool> DiminuirCapacidadeAsync(int eventoId);
    Task AumentarCapacidadeAsync(int eventoId);
}