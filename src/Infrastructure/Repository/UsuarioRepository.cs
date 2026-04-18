using src.Models;
 
namespace src.Infrastructure.IRepository;
 
public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorCpf(string cpf);
    Task<Usuario?> ObterPorCpfESenha(string cpf, string senha);
    Task CriarUsuario(Usuario usuario);
    Task<IEnumerable<Usuario>> ListarUsuarios();
}