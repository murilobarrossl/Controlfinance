namespace ControlFinance.API.Services;

public sealed class PolpWebhookProcessorHandler(PolpWebhookProcessor processor) : IPolpEventHandler
{
    public Task HandleAsync(PolpInboxEvent evt, CancellationToken ct) => processor.ProcessAsync(evt, ct);
}
