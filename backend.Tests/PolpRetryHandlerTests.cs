using System.Net;
using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class PolpRetryHandlerTests
{
    private sealed class FakeInner(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<HttpResponseMessage> Responses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = await respond(Calls, cancellationToken);
            Responses.Add(response);
            return response;
        }
    }

    private sealed class TrackedContent() : StringContent("{}")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new TrackedContent() };

    private static (HttpMessageInvoker Invoker, FakeInner Inner, List<TimeSpan> Waits) Build(
        Func<int, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? attemptTimeout = null)
    {
        var waits = new List<TimeSpan>();
        var inner = new FakeInner(respond);
        var handler = new PolpRetryHandler(
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; },
            () => 1.0,
            attemptTimeout ?? TimeSpan.FromSeconds(15),
            TimeProvider.System)
        { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), inner, waits);
    }

    private static Task<HttpResponseMessage> Send(HttpMessageInvoker invoker, HttpMethod method, CancellationToken ct = default) =>
        invoker.SendAsync(new HttpRequestMessage(method, "https://polp.test/api/v2/accounts"), ct);

    [Fact]
    public async Task Get_com_503_e_depois_200_tenta_de_novo_e_devolve_200()
    {
        var (invoker, inner, waits) = Build((n, _) => Task.FromResult(Status(n == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));

        var response = await Send(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Single(waits);
        Assert.True(((TrackedContent)inner.Responses[0].Content).Disposed);
    }

    [Fact]
    public async Task Post_com_503_faz_uma_chamada_so()
    {
        var (invoker, inner, waits) = Build((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));

        var response = await Send(invoker, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Delete_com_503_faz_uma_chamada_so()
    {
        var (invoker, inner, _) = Build((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));

        var response = await Send(invoker, HttpMethod.Delete);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Retry_After_de_2_segundos_no_429_e_a_espera_usada()
    {
        var (invoker, inner, waits) = Build((n, _) =>
        {
            if (n > 1) return Task.FromResult(Status(HttpStatusCode.OK));
            var limited = Status(HttpStatusCode.TooManyRequests);
            limited.Headers.Add("Retry-After", "2");
            return Task.FromResult(limited);
        });

        var response = await Send(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task Retry_After_acima_do_teto_devolve_o_429_sem_esperar()
    {
        var (invoker, inner, waits) = Build((_, _) =>
        {
            var limited = Status(HttpStatusCode.TooManyRequests);
            limited.Headers.Add("Retry-After", "60");
            return Task.FromResult(limited);
        });

        var response = await Send(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Retry_After_como_data_http_vira_a_espera_ate_essa_data()
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var clock = new Support.TestClock { UtcNow = now };
        var waits = new List<TimeSpan>();
        var inner = new FakeInner((n, _) =>
        {
            if (n > 1) return Task.FromResult(Status(HttpStatusCode.OK));
            var limited = Status(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(now.AddSeconds(4));
            return Task.FromResult(limited);
        });
        var invoker = new HttpMessageInvoker(new PolpRetryHandler(
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; }, () => 1.0, TimeSpan.FromSeconds(15), clock)
        { InnerHandler = inner });

        await Send(invoker, HttpMethod.Get);

        Assert.Equal([TimeSpan.FromSeconds(4)], waits);
    }

    [Fact]
    public async Task Get_com_503_sempre_desiste_apos_3_chamadas_e_devolve_503()
    {
        var (invoker, inner, waits) = Build((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));

        var response = await Send(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)], waits);
        Assert.False(((TrackedContent)response.Content).Disposed);
    }

    [Fact]
    public async Task Erro_400_nao_e_repetido()
    {
        var (invoker, inner, _) = Build((_, _) => Task.FromResult(Status(HttpStatusCode.BadRequest)));

        var response = await Send(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Timeout_da_tentativa_em_get_repete()
    {
        var (invoker, inner, _) = Build(async (n, ct) =>
        {
            if (n == 1) await Task.Delay(Timeout.Infinite, ct);
            return Status(HttpStatusCode.OK);
        }, TimeSpan.FromMilliseconds(50));

        var response = await Send(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Timeout_em_todas_as_tentativas_lanca_TaskCanceledException_tratada_como_falha_do_provedor()
    {
        var (invoker, inner, _) = Build(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Status(HttpStatusCode.OK);
        }, TimeSpan.FromMilliseconds(20));

        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => Send(invoker, HttpMethod.Get));

        Assert.Equal(3, inner.Calls);
        Assert.True(PolpFailure.IsProviderFailure(ex, CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_em_post_nao_repete()
    {
        var (invoker, inner, _) = Build(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Status(HttpStatusCode.OK);
        }, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TaskCanceledException>(() => Send(invoker, HttpMethod.Post));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_propaga_sem_repetir()
    {
        using var cts = new CancellationTokenSource();
        var (invoker, inner, waits) = Build(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Status(HttpStatusCode.OK);
        });

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(invoker, HttpMethod.Get, cts.Token));

        Assert.Equal(1, inner.Calls);
        Assert.Empty(waits);
        Assert.False(PolpFailure.IsProviderFailure(ex, cts.Token));
    }
}
