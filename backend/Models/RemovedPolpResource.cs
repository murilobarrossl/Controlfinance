namespace ControlFinance.API.Models;

/// <summary>
/// Id da Polp (consentimento, conta ou cartão) cujos dados locais foram apagados por desconexão ou
/// exclusão da conta. Serve para o processor encerrar avisos atrasados desses recursos em vez de
/// tentar de novo como se fossem recursos que ainda não chegaram. Só o id técnico, sem usuário.
/// </summary>
public class RemovedPolpResource
{
    public Guid PolpId { get; set; }
    public DateTime RemovedAt { get; set; }
}
