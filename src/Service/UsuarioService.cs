using src.Models;
using src.Infrastructure.IRepository;
 
namespace src.Service;
 
public class UsuarioService
{
    private readonly IUsuarioRepository _repository;
 
    public UsuarioService(IUsuarioRepository repository)
    {
        _repository = repository;
    }
 
    public async Task<Usuario> CadastrarUsuario(Usuario novoUsuario)
    {
        var usuarioExistente = await _repository.ObterPorCpf(novoUsuario.Cpf);
 
        if (usuarioExistente != null)
            throw new InvalidOperationException("Erro: O CPF informado já está cadastrado.");
 
        await _repository.CriarUsuario(novoUsuario);
        return novoUsuario;
    }
 
    public async Task<Usuario?> BuscarPorCpf(string cpf)
    {
        return await _repository.ObterPorCpf(cpf);
    }
}
 