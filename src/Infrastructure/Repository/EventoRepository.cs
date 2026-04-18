using Dapper;
using src.Models;
using src.Infrastructure.IRepository;
 
namespace src.Infrastructure.Repository;
 
public class EventoRepository : IEventoRepository
{
    private readonly DbConnectionFactory _connectionFactory;
 
    public EventoRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }
 
    public async Task AdicionarAsync(Evento evento)
    {
        const string sql = @"
            INSERT INTO Eventos (Nome, CapacidadeTotal, DataEvento, PrecoPadrao)
            VALUES (@Nome, @CapacidadeTotal, @DataEvento, @PrecoPadrao)";
 
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, evento);
    }
 
    public async Task<IEnumerable<Evento>> ObterTodosAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        string sql = "SELECT * FROM Eventos ORDER BY DataEvento ASC";
        return await conn.QueryAsync<Evento>(sql);
    }
 
    public async Task<IEnumerable<Evento>> ObterDisponiveisAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        string sql = @"SELECT * FROM Eventos
                       WHERE DataEvento > GETDATE()
                       AND CapacidadeTotal > 0
                       ORDER BY DataEvento ASC";
        return await conn.QueryAsync<Evento>(sql);
    }
 
    public async Task<Evento?> ObterPorIdAsync(int id)
    {
        using var conn = _connectionFactory.CreateConnection();
        string sql = "SELECT * FROM Eventos WHERE Id = @Id";
        return await conn.QueryFirstOrDefaultAsync<Evento>(sql, new { Id = id });
    }
 
    public async Task<bool> DiminuirCapacidadeAsync(int eventoId)
    {
        using var conn = _connectionFactory.CreateConnection();
        string sql = @"UPDATE Eventos 
                       SET CapacidadeTotal = CapacidadeTotal - 1
                       WHERE Id = @EventoId AND CapacidadeTotal > 0";
 
        var rows = await conn.ExecuteAsync(sql, new { EventoId = eventoId });
        return rows > 0;
    }
}