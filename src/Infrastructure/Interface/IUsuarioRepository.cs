using Dapper;
using src.Models;
using src.Infrastructure.IRepository;

namespace src.Infrastructure;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public UsuarioRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async virtual Task<Usuario?> ObterPorCpf(string cpf)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = "SELECT * FROM Usuarios WHERE Cpf = @Cpf";
        return await connection.QueryFirstOrDefaultAsync<Usuario>(sql, new { Cpf = cpf });
    }

    public async virtual Task<Usuario?> ObterPorCpfESenha(string cpf, string senha)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = "SELECT * FROM Usuarios WHERE Cpf = @Cpf AND Senha = @Senha";
        return await connection.QueryFirstOrDefaultAsync<Usuario>(sql, new { Cpf = cpf, Senha = senha });
    }

    public async virtual Task CriarUsuario(Usuario usuario)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"INSERT INTO Usuarios (Cpf, Nome, Email, Senha, Perfil)
                    VALUES (@Cpf, @Nome, @Email, @Senha, @Perfil)";
        await connection.ExecuteAsync(sql, usuario);
    }

    public async virtual Task<IEnumerable<Usuario>> ListarUsuarios()
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = "SELECT * FROM Usuarios";
        return await connection.QueryAsync<Usuario>(sql);
    }
}