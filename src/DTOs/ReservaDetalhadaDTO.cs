namespace src.DTOs;

public class ReservaDetalhadaDTO
{
    public int Id { get; set; }
    public string UsuarioCpf { get; set; } = string.Empty;
    public int EventoId { get; set; }
    public DateTime DataCompra { get; set; }
    public string Nome { get; set; } = string.Empty;       // Nome do evento
    public DateTime DataEvento { get; set; }               // Data do evento
    public decimal PrecoPadrao { get; set; }               // Preço do evento
}
 