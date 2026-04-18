using System.ComponentModel.DataAnnotations;
namespace src.Models;
 
public class Usuario
{
    [Required(ErrorMessage = "O CPF é obrigatório")]
    public string Cpf { get; set; } = string.Empty;
 
    [Required(ErrorMessage = "O Nome é obrigatório")]
    public string Nome { get; set; } = string.Empty;
 
    [Required(ErrorMessage = "O Email é obrigatório")]
    [EmailAddress(ErrorMessage = "Email inválido")]
    public string Email { get; set; } = string.Empty;
 
    [Required(ErrorMessage = "A Senha é obrigatória")]
    [MinLength(6, ErrorMessage = "A senha deve ter no mínimo 6 caracteres")]
    public string Senha { get; set; } = string.Empty;
 
    public string Perfil { get; set; } = "CLIENTE"; // "ADMIN" ou "CLIENTE"
}