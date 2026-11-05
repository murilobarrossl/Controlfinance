using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

// Receptor dos webhooks da Polp (API v2). Duas barreiras independentes, nesta ordem:
//  - token no caminho da URL (Polp:WebhookToken, rotacionável com Polp:WebhookTokenPrevious): 404
//    se errado, para não confirmar que o endpoint existe;
//  - assinatura HMAC-SHA256 do corpo bruto em X-Webhook-Signature (Polp:WebhookSigningSecret): 401
//    se ausente ou inválida. A URL vaza em log de proxy; o signing secret nunca trafega.
//    Desligável (Polp:WebhookRequireSignature=false) porque a Polp não documenta assinatura: o
//    aviso não carrega dado, então um aviso forjado só custa uma consulta a mais à Polp.
// 200 só depois de o aviso estar gravado na inbox: com 500 a Polp reenvia em vez de ele se perder.
// Corpo, token e assinatura nunca são logados.
[ApiController]
[Route("api/polp/webhook")]
[AllowAnonymous]
public class PolpWebhookController(
    IConfiguration config,
    IPolpInbox inbox,
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

        var body = await ReadBodyAsync(ct);

        if (ConfigSecrets.IsWebhookSignatureRequired(config))
        {
            var signingSecret = config["Polp:WebhookSigningSecret"];
            if (!ConfigSecrets.IsStrongSecret(signingSecret) || signingSecret == config["Polp:WebhookToken"])
            {
                logger.LogError("Webhook da Polp recusado: Polp:WebhookSigningSecret ausente ou inválido");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!SignatureMatches(body, signingSecret!))
            {
                logger.LogWarning("Webhook da Polp recusado: assinatura ausente ou inválida");
                return Unauthorized();
            }
        }

        if (!TryParse(body, out var evt)) return BadRequest();
        if (evt is null) return Ok();

        try
        {
            await inbox.EnqueueAsync(evt, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Falha ao gravar webhook da Polp na inbox ({ErrorType}, evento {Event})", ex.GetType().Name, evt.Event);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    // evt nulo com retorno true: aviso sem "event", que nunca teve o que processar.
    internal static bool TryParse(byte[] body, out PolpInboxEvent? evt)
    {
        evt = null;
        WebhookBody? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookBody>(body);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null
            || payload.Event?.Length > PolpWebhookEvent.EventMaxLength
            || payload.Resource?.Length > PolpWebhookEvent.ResourceMaxLength
            || payload.QueryParameters?.Length > PolpWebhookEvent.QueryParametersMaxLength)
            return false;

        if (string.IsNullOrWhiteSpace(payload.Event)) return true;

        // Guid normalizado: o mesmo recurso em maiúsculas não pode escapar da deduplicação.
        evt = new PolpInboxEvent(payload.Event, payload.Resource, payload.ResourceId?.ToString("D"), payload.QueryParameters);
        return true;
    }

    private sealed class WebhookBody
    {
        [JsonPropertyName("event")] public string? Event { get; set; }
        [JsonPropertyName("resource")] public string? Resource { get; set; }
        [JsonPropertyName("resource_id")] public Guid? ResourceId { get; set; }
        [JsonPropertyName("query_parameters")] public string? QueryParameters { get; set; }
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
