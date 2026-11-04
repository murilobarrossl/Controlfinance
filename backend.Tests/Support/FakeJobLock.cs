using System.Collections.Concurrent;
using ControlFinance.API.Services;

namespace ControlFinance.API.Tests.Support;

// Mesma semântica do pg_try_advisory_lock numa conexão nova por tentativa: não espera, não é
// reentrante e só a liberação do handle devolve a chave.
public sealed class FakeJobLock : IJobLock
{
    private readonly ConcurrentDictionary<long, byte> _held = new();

    public bool IsHeld(long key) => _held.ContainsKey(key);

    public Task<IAsyncDisposable?> TryAcquireAsync(long key, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable?>(_held.TryAdd(key, 0) ? new Handle(this, key) : null);

    private sealed class Handle(FakeJobLock owner, long key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._held.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
