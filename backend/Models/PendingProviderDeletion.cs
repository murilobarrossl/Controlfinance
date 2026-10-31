namespace ControlFinance.API.Models;

/// <summary>
/// Pedido de exclusão na Polp que ainda não foi aceito, sobrevivendo à exclusão da conta do
/// usuário. Guarda só o id técnico da integração na Polp, sem nenhum vínculo com a pessoa:
/// quando a conta é excluída, todo dado pessoal some na hora, mas o pedido de "apague do seu
/// lado também" não pode se perder só porque a Polp estava indisponível naquele momento.
/// </summary>
public class PendingProviderDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolpIntegrationId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAt { get; set; }
}
