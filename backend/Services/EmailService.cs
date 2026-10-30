using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ControlFinance.API.Services;

// E-mails passam por um provedor externo (Resend, com processamento nos EUA). Por isso nenhum
// e-mail leva nome da pessoa, nome de transação, valor ou saldo: só o endereço de destino (sem
// ele não há e-mail) e um aviso genérico pra abrir o painel. Antes os lembretes levavam nome e
// valor de cada conta, e o resumo mensal levava receitas, despesas e saldo do mês.
public class EmailService
{
    private readonly HttpClient _httpClient;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public EmailService(IConfiguration configuration, HttpClient httpClient)
    {
        _httpClient = httpClient;

        var apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Resend:ApiKey não configurada.");

        _fromEmail = configuration["Resend:FromEmail"] ?? "noreply@controlfinance.com.br";
        _fromName  = configuration["Resend:FromName"]  ?? "Control Finance";

        _httpClient.BaseAddress = new Uri("https://api.resend.com/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ──────────────────────────────────────────
    //  EMAILS
    // ──────────────────────────────────────────

    public Task SendWelcomeEmailAsync(string toEmail) =>
        SendAsync(toEmail, "Sua conta no Control Finance foi criada", WelcomeHtml());

    public Task SendDueReminderAsync(string toEmail, int pendingCount, DateTime nearestDueDate) =>
        SendAsync(toEmail, "Você tem contas perto do vencimento", DueReminderHtml(pendingCount, nearestDueDate));

    public Task SendMonthlyNoticeAsync(string toEmail) =>
        SendAsync(toEmail, "Seu mês fechou: veja o resumo no Control Finance", MonthlyNoticeHtml());

    // ──────────────────────────────────────────
    //  CORE
    // ──────────────────────────────────────────

    private async Task SendAsync(string toEmail, string subject, string html)
    {
        var payload = new
        {
            from    = $"{_fromName} <{_fromEmail}>",
            to      = new[] { toEmail },
            subject,
            html
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("emails", content);

        // Só o status na exceção: o corpo de erro do provedor pode repetir o destinatário, e essa
        // mensagem acaba em log.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Falha no envio de e-mail (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
    }

    // ──────────────────────────────────────────
    //  TEMPLATES HTML
    // ──────────────────────────────────────────

    private static string BaseHtml(string title, string body) => $"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>{title}</title>
        </head>
        <body style="margin:0;padding:0;background:#f4f6f8;font-family:Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:40px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;overflow:hidden;">
                <tr>
                  <td style="background:#1a1a2e;padding:24px 32px;">
                    <h1 style="margin:0;color:#ffffff;font-size:20px;font-weight:700;">Control Finance</h1>
                  </td>
                </tr>
                <tr><td style="padding:32px;">{body}</td></tr>
                <tr>
                  <td style="background:#f4f6f8;padding:16px 32px;text-align:center;">
                    <p style="margin:0 0 6px;color:#999;font-size:12px;">
                      Por privacidade, este e-mail não traz nomes nem valores. Os detalhes ficam só no painel.
                    </p>
                    <p style="margin:0;color:#999;font-size:12px;">
                      Você pode desligar os avisos automáticos em Privacidade e dados, no painel.
                    </p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string WelcomeHtml() => BaseHtml("Conta criada", """
        <h2 style="color:#1a1a2e;margin-top:0;">Olá!</h2>
        <p style="color:#444;line-height:1.6;">
          Sua conta no <strong>Control Finance</strong> foi criada.
        </p>
        <p style="color:#444;line-height:1.6;">
          No painel você pode conectar seus bancos (via Open Finance, com a sua autorização),
          ver receitas, despesas, cartões e relatórios, e acompanhar metas.
        </p>
        <p style="color:#444;line-height:1.6;">
          Se não foi você quem criou esta conta, responda a este e-mail para avisar o suporte.
        </p>
        """);

    private static string DueReminderHtml(int pendingCount, DateTime nearestDueDate)
    {
        var countText = pendingCount == 1 ? "1 conta pendente" : $"{pendingCount} contas pendentes";
        return BaseHtml("Lembrete de vencimento", $"""
            <h2 style="color:#1a1a2e;margin-top:0;">Lembrete de vencimento</h2>
            <p style="color:#444;line-height:1.6;">
              Você tem <strong>{countText}</strong> com vencimento nos próximos dias. A mais próxima
              vence em <strong>{nearestDueDate:dd/MM/yyyy}</strong>.
            </p>
            <p style="color:#444;">Abra o painel do Control Finance para ver quais são.</p>
            """);
    }

    private static string MonthlyNoticeHtml() => BaseHtml("Resumo do mês", """
        <h2 style="color:#1a1a2e;margin-top:0;">Seu mês fechou</h2>
        <p style="color:#444;line-height:1.6;">
          O resumo de receitas, despesas e categorias do mês passado já está disponível no painel
          do Control Finance.
        </p>
        """);
}
