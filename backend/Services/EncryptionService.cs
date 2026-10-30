using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ControlFinance.API.Services;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    string EncryptDecimal(decimal value);
    decimal DecryptDecimal(string cipherText);

    /// <summary>
    /// Hash determinístico (HMAC-SHA256) com a chave de busca ATIVA, usado para busca/unicidade
    /// de um valor que não é guardado (ex: CPF/CNPJ). É dado pseudonimizado, não anônimo.
    /// </summary>
    string ComputeLookupHash(string value);

    /// <summary>
    /// Hashes do valor com todas as chaves de busca conhecidas (ativa primeiro, legada por
    /// último). Necessário porque o CPF não é guardado: depois de trocar a chave de busca, não dá
    /// pra recalcular os hashes antigos em lote; eles são reconhecidos com a chave antiga e
    /// regravados com a nova quando o usuário digita o documento de novo (login por CPF ou
    /// conexão bancária).
    /// </summary>
    IReadOnlyList<string> ComputeLookupHashCandidates(string value);

    bool IsActiveLookupHash(string hash);
}

// Anel de chaves versionado.
//
// Antes: um segredo só (Encryption:Key) virava, por SHA-256 simples, tanto a chave AES quanto a
// chave do HMAC do CPF; o texto cifrado não dizia com qual chave foi feito, então trocar a chave
// tornava todo dado antigo ilegível — na prática, rotação impossível. E o vazamento desse único
// segredo expunha cifra e hash ao mesmo tempo.
//
// Agora (opcional, por configuração, sem quebrar nada já gravado):
// - Encryption:Keys:{id} + Encryption:ActiveKeyId — segredos de cifragem versionados. O texto
//   cifrado novo carrega o id ("cfk:{id}:base64"), então chaves antigas continuam decifrando o
//   que fizeram enquanto a rotação (EncryptionKeyRotation) regrava tudo com a ativa.
// - Encryption:LookupKeys:{id} + Encryption:ActiveLookupKeyId — segredos do HMAC de busca,
//   INDEPENDENTES dos de cifragem: comprometer um não expõe o outro. Hash novo: "cfh:{id}:base64".
// - Derivação por HKDF-SHA256 com rótulo de finalidade e id (em vez de SHA-256 cru).
// - Encryption:Key continua obrigatória enquanto existir dado no formato legado (sem prefixo):
//   decifra/reconhece o que já está gravado. Sem as seções novas configuradas, o comportamento
//   é idêntico ao anterior.
public class EncryptionService : IEncryptionService
{
    private const int NonceSize = 12; // 96 bits, tamanho recomendado de nonce para AES-GCM
    private const int TagSize = 16;   // 128 bits, tamanho da tag de autenticação

    private const string CipherPrefix = "cfk:";
    private const string HashPrefix = "cfh:";

    private readonly byte[] _legacyKey;
    private readonly byte[] _legacyHmacKey;

    private readonly Dictionary<string, byte[]> _dataKeys = new();
    private readonly string? _activeKeyId;

    private readonly Dictionary<string, byte[]> _lookupKeys = new();
    private readonly string? _activeLookupKeyId;

    public EncryptionService(IConfiguration configuration)
    {
        var keyString = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key não configurada.");

        // Derivação legada, mantida só para ler/reconhecer o que já foi gravado assim.
        using (var sha256 = SHA256.Create())
        {
            _legacyKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
            _legacyHmacKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString + "|lookup-hash"));
        }

        foreach (var entry in configuration.GetSection("Encryption:Keys").GetChildren())
            _dataKeys[ValidateId(entry.Key)] = Derive(entry.Value, "control-finance/aes-256-gcm/" + entry.Key);

        foreach (var entry in configuration.GetSection("Encryption:LookupKeys").GetChildren())
            _lookupKeys[ValidateId(entry.Key)] = Derive(entry.Value, "control-finance/lookup-hmac/" + entry.Key);

        _activeKeyId = configuration["Encryption:ActiveKeyId"];
        if (_activeKeyId is not null && !_dataKeys.ContainsKey(_activeKeyId))
            throw new InvalidOperationException($"Encryption:ActiveKeyId '{_activeKeyId}' não existe em Encryption:Keys.");

        _activeLookupKeyId = configuration["Encryption:ActiveLookupKeyId"];
        if (_activeLookupKeyId is not null && !_lookupKeys.ContainsKey(_activeLookupKeyId))
            throw new InvalidOperationException($"Encryption:ActiveLookupKeyId '{_activeLookupKeyId}' não existe em Encryption:LookupKeys.");
    }

    private static string ValidateId(string id) =>
        id.Length is > 0 and <= 16 && id.All(char.IsAsciiLetterOrDigit)
            ? id
            : throw new InvalidOperationException($"Id de chave inválido: '{id}' (use até 16 letras/dígitos).");

    // HKDF exige material com entropia de verdade: segredo curto é recusado de propósito.
    private static byte[] Derive(string? secret, string info)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < 32)
            throw new InvalidOperationException("Segredos de criptografia precisam ter pelo menos 32 caracteres.");

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32,
            info: Encoding.UTF8.GetBytes(info));
    }

    // ──────────────────────────────────────────
    //  ENCRYPT / DECRYPT STRING (AES-256-GCM)
    // ──────────────────────────────────────────

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        var key = _activeKeyId is null ? _legacyKey : _dataKeys[_activeKeyId];

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        // Formato: nonce (12) + tag (16) + ciphertext, tudo em Base64; com chave versionada,
        // prefixado por "cfk:{id}:" (Base64 nunca contém ':').
        var result = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize + TagSize, cipherBytes.Length);

        var payload = Convert.ToBase64String(result);
        return _activeKeyId is null ? payload : $"{CipherPrefix}{_activeKeyId}:{payload}";
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        var (key, payload) = ResolveCipherKey(cipherText);
        var fullBytes = Convert.FromBase64String(payload);

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherBytes = new byte[fullBytes.Length - NonceSize - TagSize];

        Buffer.BlockCopy(fullBytes, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(fullBytes, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(fullBytes, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private (byte[] Key, string Payload) ResolveCipherKey(string cipherText)
    {
        if (!cipherText.StartsWith(CipherPrefix, StringComparison.Ordinal))
            return (_legacyKey, cipherText);

        var rest = cipherText[CipherPrefix.Length..];
        var separator = rest.IndexOf(':');
        var keyId = rest[..separator];

        // Chave removida da configuração = dado ilegível de propósito (descarte criptográfico).
        if (!_dataKeys.TryGetValue(keyId, out var key))
            throw new CryptographicException($"Chave de criptografia '{keyId}' não está mais configurada.");

        return (key, rest[(separator + 1)..]);
    }

    // ──────────────────────────────────────────
    //  HELPERS PARA DECIMAL (valores financeiros)
    // ──────────────────────────────────────────

    public string EncryptDecimal(decimal value) =>
        Encrypt(value.ToString("F2", CultureInfo.InvariantCulture));

    public decimal DecryptDecimal(string cipherText)
    {
        var plain = Decrypt(cipherText);
        return decimal.Parse(plain, CultureInfo.InvariantCulture);
    }

    // ──────────────────────────────────────────
    //  HASH DE BUSCA (determinístico)
    // ──────────────────────────────────────────

    public string ComputeLookupHash(string value) =>
        _activeLookupKeyId is null
            ? Hmac(_legacyHmacKey, value)
            : $"{HashPrefix}{_activeLookupKeyId}:{Hmac(_lookupKeys[_activeLookupKeyId], value)}";

    public IReadOnlyList<string> ComputeLookupHashCandidates(string value)
    {
        var candidates = new List<string> { ComputeLookupHash(value) };
        foreach (var (id, key) in _lookupKeys)
        {
            if (id == _activeLookupKeyId) continue;
            candidates.Add($"{HashPrefix}{id}:{Hmac(key, value)}");
        }
        if (_activeLookupKeyId is not null)
            candidates.Add(Hmac(_legacyHmacKey, value));
        return candidates;
    }

    public bool IsActiveLookupHash(string hash) =>
        _activeLookupKeyId is null
            ? !hash.StartsWith(HashPrefix, StringComparison.Ordinal)
            : hash.StartsWith($"{HashPrefix}{_activeLookupKeyId}:", StringComparison.Ordinal);

    private static string Hmac(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
