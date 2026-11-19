using System.Collections.Concurrent;
using ControlFinance.API.Data;

namespace ControlFinance.API.Services;

// Uma consulta por chave em voo: requisições simultâneas do mesmo usuário esperam a mesma leitura em vez de
// abrir N conexões. A leitura roda em escopo próprio e sem o cancelamento de nenhuma requisição, porque o
// cliente que abortou não pode derrubar a validação dos outros; cada um espera com o próprio token. Erro não
// é guardado: a próxima requisição tenta de novo, e quem esperava falha fechado (sem autorizar).
public sealed class SharedDbLoad<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _inFlight = new();

    public Task<TValue> GetAsync(TKey key, IServiceScopeFactory scopes, Func<AppDbContext, Task<TValue>> load,
        Action<TValue> store, CancellationToken waiter)
    {
        Lazy<Task<TValue>>? created = null;
        var entry = _inFlight.GetOrAdd(key, _ => created = new Lazy<Task<TValue>>(() => RunAsync(key, created!, scopes, load, store)));
        return entry.Value.WaitAsync(waiter);
    }

    // Quem invalida (logout, revogação) descarta a leitura em voo: o resultado dela, anterior à mudança,
    // não volta para o cache.
    public void Forget(TKey key) => _inFlight.TryRemove(key, out _);

    private async Task<TValue> RunAsync(TKey key, Lazy<Task<TValue>> self, IServiceScopeFactory scopes,
        Func<AppDbContext, Task<TValue>> load, Action<TValue> store)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var value = await load(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, self)) store(value);
            return value;
        }
        finally
        {
            _inFlight.TryRemove(KeyValuePair.Create(key, self));
        }
    }
}
