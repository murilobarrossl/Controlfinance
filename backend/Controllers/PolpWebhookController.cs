using System.Security.Cryptography;
using System.Text;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

// Receptor dos webhooks da Polp (API v2). A doc da Polp não prevê assinatura nem header
// customizado, então o segredo vai no caminho da URL cadastrada no Dashboard. Fraquezas
// conhecidas, e por que são aceitáveis aqui:
//  - URL aparece em log de acesso de proxy/gateway: o token precisa ser rotacionável
//    (Polp:WebhookToken + Polp:WebhookTokenPrevious, para trocar sem perder eventos).
//  - sem proteção a replay: o pior efeito de um aviso reenviado é um sync a mais, e
//    PolpWebhookProcessor deduplica e só aceita ids que já existem no banco.
// O corpo nunca é logado e a resposta é sempre 200 depois do token válido, para não revelar quais
// ids existem.
[ApiController]
[Route("api/polp/webhook")]
[AllowAnonymous]
public class PolpWebhookController(
    IConfiguration config,
    IServiceScopeFactory scopeFactory,
    ILogger<PolpWebhookController> logger) : ControllerBase
{
    [HttpPost("{token}")]
    public IActionResult Receive(string token, [FromBody] PolpWebhookPayload payload)
    {
        // 404 (e não 401) para token errado ou ausente: não confirma que o endpoint existe.
        if (!IsValidToken(token)) return NotFound();

        if (PolpWebhookProcessor.IsDuplicate(payload)) return Ok();

        // Responde na hora e processa em background, com escopo de DI próprio (o DbContext desta
        // requisição é descartado quando ela responde). A Polp não precisa esperar o sync.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PolpWebhookProcessor>();
                await processor.ProcessAsync(payload, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Só o tipo do evento: nunca o payload.
                logger.LogError(ex, "Falha ao processar webhook da Polp (evento {Event})", payload.Event);
            }
        });

        return Ok();
    }

    private bool IsValidToken(string provided)
    {
        // Compara o hash dos dois lados em tempo constante: não vaza, pelo tempo de resposta,
        // quantos caracteres iniciais do token estavam certos.
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var valid = false;

        foreach (var key in new[] { "Polp:WebhookToken", "Polp:WebhookTokenPrevious" })
        {
            var expected = config[key];
            if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32) continue;

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            valid |= CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
        }

        return valid;
    }
}
