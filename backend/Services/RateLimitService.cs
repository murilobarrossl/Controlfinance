using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ControlFinance.API.Services;

// Bloqueia tanto por identificador (email/CPF) quanto por IP, com propósitos diferentes:
// - Por identificador: trava força bruta vertical (uma conta, muitas senhas).
// - Por IP: trava um script martelando várias contas de uma máquina/rede só.
// Nenhum dos dois sozinho (nem os dois juntos) impede um "password spraying" de verdade
// (atacante rotacionando IPs, uma tentativa por conta). Isso exige outra camada (detecção de
// senha vazada, CAPTCHA, WAF), fora do escopo deste serviço. O limite de IP é propositalmente
// generoso (bem mais alto que o de identificador) pra não travar rede compartilhada legítima
// (escritório, NAT, várias contas de teste na mesma rede).
// ConcurrentDictionary porque esse serviço é singleton e atende requisições concorrentes.
public class RateLimitService : IDisposable
{
    private readonly ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> _byIdentifier = new();
    private readonly ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> _byIp = new();

    private const int MaxAttemptsPerIdentifier = 5;
    private const int MaxAttemptsPerIp = 100;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // Entradas só são removidas em RegisterSuccess ou quando a própria chave é consultada de
    // novo depois da janela (IsBlocked). Quem falha uma vez e nunca mais volta fica esquecido
    // no dicionário para sempre; essa varredura periódica evita esse vazamento lento de memória.
    private readonly Timer _cleanupTimer;

    // As chaves dos dicionários eram o e-mail/CPF e o IP digitados, em texto puro, na memória do
    // processo por até 15 min (inclusive identificadores de quem nem tem conta). Agora são HMAC
    // com uma chave aleatória gerada a cada inicialização e nunca gravada: a contagem funciona
    // igual, e um dump de memória não revela quem tentou entrar. A chave some quando o processo
    // reinicia, o que só zera os contadores (mesmo efeito de antes, que já eram só em memória).
    private readonly byte[] _keyHmacSecret = RandomNumberGenerator.GetBytes(32);

    private string Key(string value)
    {
        using var hmac = new HMACSHA256(_keyHmacSecret);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    public RateLimitService()
    {
        _cleanupTimer = new Timer(_ => PurgeStale(), null, Window, Window);
    }

    // De propósito, não existe um "RemainingAttempts" exposto pra API: sites grandes não avisam
    // quantas tentativas restam nem que você foi bloqueado. A resposta de erro é sempre a mesma
    // esteja você só errando a senha ou já travado, senão dá pra um atacante calibrar o ataque
    // pela própria mensagem de erro.
    public bool IsBlocked(string identifier, string ipAddress) =>
        IsBlocked(_byIdentifier, Key(identifier), MaxAttemptsPerIdentifier) ||
        IsBlocked(_byIp, Key(ipAddress), MaxAttemptsPerIp);

    public void RegisterFailure(string identifier, string ipAddress)
    {
        RegisterFailure(_byIdentifier, Key(identifier));
        RegisterFailure(_byIp, Key(ipAddress));
    }

    public void RegisterSuccess(string identifier, string ipAddress)
    {
        _byIdentifier.TryRemove(Key(identifier), out _);
        _byIp.TryRemove(Key(ipAddress), out _);
    }

    private static bool IsBlocked(ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> store, string key, int maxAttempts)
    {
        if (!store.TryGetValue(key, out var entry)) return false;

        if (DateTime.UtcNow - entry.lastAttempt > Window)
        {
            store.TryRemove(key, out _);
            return false;
        }

        return entry.attempts >= maxAttempts;
    }

    private static void RegisterFailure(ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> store, string key)
    {
        store.AddOrUpdate(
            key,
            _ => (1, DateTime.UtcNow),
            (_, entry) => (entry.attempts + 1, DateTime.UtcNow));
    }

    private void PurgeStale()
    {
        PurgeStale(_byIdentifier);
        PurgeStale(_byIp);
    }

    private static void PurgeStale(ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> store)
    {
        var cutoff = DateTime.UtcNow - Window;
        foreach (var (key, entry) in store)
        {
            if (entry.lastAttempt < cutoff)
                store.TryRemove(key, out _);
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}
