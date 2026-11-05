namespace ControlFinance.API.Services;

// O appsettings.json versionado traz placeholders longos o bastante para passar numa checagem só de
// tamanho: sem recusar esses textos, uma variável esquecida no servidor vira um segredo público.
public static class ConfigSecrets
{
    public const int MinSecretLength = 32;

    private static readonly string[] PlaceholderMarkers =
    [
        "SUBSTITUA", "COLOQUE", "CHANGE_ME", "CHANGEME", "YOUR_", "SEU_", "SUA_", "PLACEHOLDER", "XXXX",
    ];

    public static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !PlaceholderMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static bool IsStrongSecret(string? value) =>
        IsConfigured(value)
        && value!.Trim().Length >= MinSecretLength
        && value.Distinct().Count() > 1;

    public static bool IsPolpEnabled(IConfiguration config) =>
        IsConfigured(config["Polp:ClientId"]) && IsConfigured(config["Polp:ClientSecret"]);

    public static bool IsWebhookSignatureRequired(IConfiguration config) =>
        config.GetValue("Polp:WebhookRequireSignature", true);

    // Só nomes de chave nas mensagens, nunca valores.
    public static List<string> Validate(IConfiguration config, bool isDevelopment)
    {
        var errors = new List<string>();

        if (!IsConfigured(config.GetConnectionString("DefaultConnection")))
            errors.Add("ConnectionStrings:DefaultConnection ausente ou placeholder");

        RequireStrong(config, "JwtSettings:SecretKey", errors);
        RequireStrong(config, "Encryption:Key", errors);

        if (IsPolpEnabled(config))
        {
            RequireStrong(config, "Polp:WebhookToken", errors);
            if (IsWebhookSignatureRequired(config))
                RequireStrong(config, "Polp:WebhookSigningSecret", errors);

            var previous = config["Polp:WebhookTokenPrevious"];
            if (!string.IsNullOrWhiteSpace(previous) && !IsStrongSecret(previous))
                errors.Add($"Polp:WebhookTokenPrevious definido mas fraco ou placeholder (mínimo {MinSecretLength} caracteres)");

            RequireDistinct(config, errors,
                "Polp:WebhookToken", "Polp:WebhookTokenPrevious", "Polp:WebhookSigningSecret",
                "JwtSettings:SecretKey", "Encryption:Key");

            // O Sandbox fabrica contas e valores: em produção o usuário veria dados inventados como reais.
            if (!isDevelopment && (config["Polp:BaseUrl"] ?? "").Contains("/sandbox", StringComparison.OrdinalIgnoreCase))
                errors.Add("Polp:BaseUrl aponta para o Sandbox da Polp");
        }

        return errors;
    }

    private static void RequireStrong(IConfiguration config, string key, List<string> errors)
    {
        if (!IsStrongSecret(config[key]))
            errors.Add($"{key} ausente, placeholder ou com menos de {MinSecretLength} caracteres");
    }

    private static void RequireDistinct(IConfiguration config, List<string> errors, params string[] keys)
    {
        var values = keys
            .Select(k => (Key: k, Value: config[k]))
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToList();

        for (var i = 0; i < values.Count; i++)
            for (var j = i + 1; j < values.Count; j++)
                if (values[i].Value == values[j].Value)
                    errors.Add($"{values[i].Key} e {values[j].Key} precisam ser segredos diferentes");
    }
}
