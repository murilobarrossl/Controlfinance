using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ControlFinance.API.Services;

public interface IEmailService
{
    Task SendWelcomeEmailAsync(string toEmail, string userName);
    Task SendTransactionAlertEmailAsync(string toEmail, string userName, string transactionName, decimal amount, DateTime dueDate);
    Task SendMonthlySummaryEmailAsync(string toEmail, string userName, decimal totalIncome, decimal totalExpense, decimal balance);
}

public class EmailService : IEmailService
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

    public Task SendWelcomeEmailAsync(string toEmail, string userName) =>
        SendAsync(toEmail, $"Bem-vindo ao Control Finance, {userName}!", WelcomeHtml(userName));

    public Task SendTransactionAlertEmailAsync(string toEmail, string userName, string transactionName, decimal amount, DateTime dueDate) =>
        SendAsync(toEmail, $"Lembrete: {transactionName} vence em breve", TransactionAlertHtml(userName, transactionName, amount, dueDate));

    public Task SendMonthlySummaryEmailAsync(string toEmail, string userName, decimal totalIncome, decimal totalExpense, decimal balance) =>
        SendAsync(toEmail, $"Seu resumo financeiro de {DateTime.UtcNow.AddMonths(-1):MMMM}", MonthlySummaryHtml(userName, totalIncome, totalExpense, balance));

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

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Resend error {response.StatusCode}: {error}");
        }
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
                <!-- HEADER -->
                <tr>
                  <td style="background:#1a1a2e;padding:24px 32px;">
                    <h1 style="margin:0;color:#ffffff;font-size:20px;font-weight:700;">
                      💰 Control Finance
                    </h1>
                  </td>
                </tr>
                <!-- BODY -->
                <tr><td style="padding:32px;">{body}</td></tr>
                <!-- FOOTER -->
                <tr>
                  <td style="background:#f4f6f8;padding:16px 32px;text-align:center;">
                    <p style="margin:0;color:#999;font-size:12px;">
                      © {DateTime.UtcNow.Year} Control Finance · MI Tech
                    </p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    // Nome e nome de transação vêm de dados do próprio usuário (cadastro, transações manuais)
    // e são interpolados direto no HTML do e-mail: sem encode, um nome como "<img onerror=...>"
    // seria injetado sem tratamento no corpo enviado pela Resend.
    private static string WelcomeHtml(string name)
    {
        var safeName = WebUtility.HtmlEncode(name);
        return BaseHtml("Bem-vindo!", $"""
        <h2 style="color:#1a1a2e;margin-top:0;">Olá, {safeName}! 👋</h2>
        <p style="color:#444;line-height:1.6;">
          Sua conta no <strong>Control Finance</strong> foi criada com sucesso.
          Agora você tem controle total das suas finanças em um só lugar.
        </p>
        <ul style="color:#444;line-height:2;">
          <li>📊 Dashboard com visão geral do seu financeiro</li>
          <li>💳 Controle de receitas e despesas</li>
          <li>🔔 Alertas de vencimento</li>
          <li>📈 Relatórios mensais</li>
        </ul>
        <p style="color:#444;">Qualquer dúvida, estamos aqui.</p>
        <p style="color:#444;">Equipe Control Finance 🚀</p>
        """);
    }

    private static string TransactionAlertHtml(string name, string txName, decimal amount, DateTime due)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var safeTxName = WebUtility.HtmlEncode(txName);
        return BaseHtml("Lembrete de vencimento", $"""
        <h2 style="color:#1a1a2e;margin-top:0;">Olá, {safeName}!</h2>
        <p style="color:#444;line-height:1.6;">
          Você tem uma conta vencendo em breve:
        </p>
        <div style="background:#fff8e1;border-left:4px solid #f59e0b;padding:16px 20px;border-radius:4px;margin:16px 0;">
          <p style="margin:0;font-weight:700;color:#1a1a2e;font-size:16px;">{safeTxName}</p>
          <p style="margin:4px 0 0;color:#444;">
            Valor: <strong>R$ {amount:N2}</strong> · Vencimento: <strong>{due:dd/MM/yyyy}</strong>
          </p>
        </div>
        <p style="color:#444;">Acesse o app para registrar o pagamento e manter seu controle em dia.</p>
        """);
    }

    private static string MonthlySummaryHtml(string name, decimal income, decimal expense, decimal balance)
    {
        var safeName = WebUtility.HtmlEncode(name);
        return BaseHtml("Resumo mensal", $"""
        <h2 style="color:#1a1a2e;margin-top:0;">Olá, {safeName}! Aqui está seu resumo 📊</h2>
        <table width="100%" cellpadding="0" cellspacing="0" style="margin:16px 0;">
          <tr>
            <td style="background:#ecfdf5;border-radius:8px;padding:16px;text-align:center;width:33%;">
              <p style="margin:0;color:#666;font-size:12px;">RECEITAS</p>
              <p style="margin:4px 0 0;color:#10b981;font-size:22px;font-weight:700;">R$ {income:N2}</p>
            </td>
            <td width="8"></td>
            <td style="background:#fef2f2;border-radius:8px;padding:16px;text-align:center;width:33%;">
              <p style="margin:0;color:#666;font-size:12px;">DESPESAS</p>
              <p style="margin:4px 0 0;color:#ef4444;font-size:22px;font-weight:700;">R$ {expense:N2}</p>
            </td>
            <td width="8"></td>
            <td style="background:#eff6ff;border-radius:8px;padding:16px;text-align:center;width:33%;">
              <p style="margin:0;color:#666;font-size:12px;">SALDO</p>
              <p style="margin:4px 0 0;color:#3b82f6;font-size:22px;font-weight:700;">R$ {balance:N2}</p>
            </td>
          </tr>
        </table>
        <p style="color:#444;">Acesse o app para ver o relatório completo com detalhes por categoria.</p>
        """);
    }
}
