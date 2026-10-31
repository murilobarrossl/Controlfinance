using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

// Receptor dos webhooks da Polp (API v2). Duas barreiras independentes, nesta ordem:
//  - token no caminho da URL (Polp:WebhookToken, rotacionável com Polp:WebhookTokenPrevious): 404
//    se errado, para não confirmar que o endpoint existe;
//  - assinatura HMAC-SHA256 do corpo bruto em X-Webhook-Signature (Polp:WebhookSigningSecret): 401
//    se ausente ou inválida. A URL vaza em log de proxy; o signing secret nunca trafega.
// Sem a configuração completa, nada é aceito. A Polp não assina timestamp, então não há proteção a
// replay além da deduplicação de PolpWebhookProcessor, que só aceita ids já existentes no banco.
// Corpo, token e assinatura nunca são logados.
[ApiController]
[Route("api/polp/webhook")]
[AllowAnonymous]
public class PolpWebhookController(
    IConfiguration config,
    IServiceScopeFactory scopeFactory,
    ILogger<PolpWebhookController> logger) : ControllerBase
{
    public const string SignatureHeader = "X-Webhook-Signature";
    private const string SignaturePrefix = "sha256=";
    private const int MaxBodyBytes = 64 * 1024;

    [HttpPost("{token}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string token, CancellationToken ct)
    {
        if (!IsValidToken(token)) return NotFound();

        var signingSecret = config["Polp:WebhookSigningSecret"];
        if (!ConfigSecrets.IsStrongSecret(signingSecret) || signingSecret == config["Polp:WebhookToken"])
        {
            logger.LogError("Webhook da Polp recusado: Polp:WebhookSigningSecret ausente ou inválido");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var body = await ReadBodyAsync(ct);

        if (!SignatureMatches(body, signingSecret!))
        {
            logger.LogWarning("Webhook da Polp recusado: assinatura ausente ou inválida");
            return Unauthorized();
        }

        PolpWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PolpWebhookPayload>(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }
        if (payload is null) return BadRequest();

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
            if (!ConfigSecrets.IsStrongSecret(expected)) continue;

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected!));
            valid |= CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
        }

        return valid;
    }

    // O HMAC precisa dos bytes exatamente como chegaram: por isso a action não usa [FromBody].
    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private bool SignatureMatches(byte[] body, string signingSecret)
    {
        var headers = Request.Headers[SignatureHeader];
        if (headers.Count != 1) return false;

        var header = headers[0];
        if (header is null
            || header.Length != SignaturePrefix.Length + 64
            || !header.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            return false;

        byte[] received;
        try
        {
            received = Convert.FromHexString(header.AsSpan(SignaturePrefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), body);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }
}
