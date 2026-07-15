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
    /// Hash determinístico (HMAC-SHA256) usado para permitir busca/unicidade de valores
    /// criptografados (ex: CPF/CNPJ) sem manter o valor em texto puro indexável.
    /// </summary>
    string ComputeLookupHash(string value);
}

public class EncryptionService : IEncryptionService
{
    private const int NonceSize = 12; // 96 bits, tamanho recomendado de nonce para AES-GCM
    private const int TagSize = 16;   // 128 bits, tamanho da tag de autenticação

    private readonly byte[] _key;
    private readonly byte[] _hmacKey;

    public EncryptionService(IConfiguration configuration)
    {
        var keyString = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key não configurada.");

        using var sha256 = SHA256.Create();
        _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
        // Chave derivada com contexto separado, só para o hash de busca: nunca reaproveitar
        // a mesma chave para dois propósitos criptográficos diferentes.
        _hmacKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString + "|lookup-hash"));
    }

    // ──────────────────────────────────────────
    //  ENCRYPT / DECRYPT STRING (AES-256-GCM)
    // ──────────────────────────────────────────

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        // Formato: nonce (12) + tag (16) + ciphertext, tudo em Base64
        var result = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize + TagSize, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        var fullBytes = Convert.FromBase64String(cipherText);

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherBytes = new byte[fullBytes.Length - NonceSize - TagSize];

        Buffer.BlockCopy(fullBytes, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(fullBytes, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(fullBytes, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
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

    public string ComputeLookupHash(string value)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hash);
    }
}
