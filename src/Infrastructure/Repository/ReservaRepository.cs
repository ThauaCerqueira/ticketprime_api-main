using Dapper;
using src.Models;
using src.Infrastructure.IRepository;
 
namespace src.Infrastructure.Repository;
 
public class ReservaRepository : IReservaRepository
{
    private readonly DbConnectionFactory _connectionFactory;
 
    public ReservaRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }
 
    public async Task<Reserva> CriarAsync(Reserva reserva)
    {
        using var connection = _connectionFactory.CreateConnection();
 
        var sql = @"INSERT INTO Reservas (UsuarioCpf, EventoId, DataCompra)
                    VALUES (@UsuarioCpf, @EventoId, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS INT)";
 
        var id = await connection.QuerySingleAsync<int>(sql, reserva);
        reserva.Id = id;
        return reserva;
    }
 
    public async Task<IEnumerable<Reserva>> ListarPorUsuarioAsync(string cpf)
    {
        using var connection = _connectionFactory.CreateConnection();
 
        var sql = @"SELECT r.Id, r.UsuarioCpf, r.EventoId, r.DataCompra,
                           e.Nome, e.DataEvento, e.PrecoPadrao
                    FROM Reservas r
                    INNER JOIN Eventos e ON e.Id = r.EventoId
                    WHERE r.UsuarioCpf = @Cpf
                    ORDER BY r.DataCompra DESC";
 
        return await connection.QueryAsync<Reserva>(sql, new { Cpf = cpf });
    }
}