namespace src.DTOs;
 
public class LoginDTO
{
    public string Cpf { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
}
 
public class LoginResponseDTO
{
    public string Cpf { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Perfil { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}