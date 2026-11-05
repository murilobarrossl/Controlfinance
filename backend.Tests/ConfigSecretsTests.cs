using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace ControlFinance.API.Tests;

public class ConfigSecretsTests
{
    private const string SandboxUrl = "https://api.polp.com.br/api/v2/Sandbox";

    private static IConfiguration Config(string baseUrl, bool polpEnabled = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=cf",
            ["JwtSettings:SecretKey"] = TestEncryption.RandomSecret(),
            ["Encryption:Key"] = TestEncryption.RandomSecret(),
            ["Polp:BaseUrl"] = baseUrl,
            ["Polp:ClientId"] = polpEnabled ? "client-id" : "SEU_CLIENT_ID",
            ["Polp:ClientSecret"] = polpEnabled ? "client-secret" : "SEU_CLIENT_SECRET",
            ["Polp:WebhookToken"] = TestEncryption.RandomSecret(),
            ["Polp:WebhookSigningSecret"] = TestEncryption.RandomSecret(),
        };
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Fact]
    public void Development_com_sandbox_sobe()
    {
        Assert.Empty(ConfigSecrets.Validate(Config(SandboxUrl), isDevelopment: true));
    }

    [Fact]
    public void Producao_com_sandbox_e_Polp_ligada_recusa_citando_so_a_chave()
    {
        var errors = ConfigSecrets.Validate(Config(SandboxUrl), isDevelopment: false);

        var error = Assert.Single(errors);
        Assert.Contains("Polp:BaseUrl", error);
        Assert.DoesNotContain("polp.com.br", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/sandbox", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Producao_com_url_de_producao_sobe()
    {
        Assert.Empty(ConfigSecrets.Validate(Config("https://api.polp.com.br/api/v2"), isDevelopment: false));
    }

    [Fact]
    public void Producao_com_sandbox_e_Polp_desligada_sobe()
    {
        Assert.Empty(ConfigSecrets.Validate(Config(SandboxUrl, polpEnabled: false), isDevelopment: false));
    }
}
