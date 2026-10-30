using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

// Controle do titular sobre os próprios dados: cópia (LGPD art. 18, II e V; art. 19, § 3º),
// preferências de comunicação (art. 18, § 2º) e exclusão da conta (art. 18, VI; Marco Civil
// art. 7º, X). Antes nenhuma dessas operações existia: a exclusão prometida nos Termos só podia
// ser feita direto no banco de dados.
[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController(
    AppDbContext db,
    PolpDisconnectService disconnectService,
    SessionValidator sessionValidator,
    RateLimitService rateLimit,
    IConfiguration config) : ApiControllerBase
{
    /// <summary>Prazos de retenção em vigor, pra tela de Privacidade e dados mostrar o valor real
    /// configurado (em vez de um texto fixo que pode não bater com a configuração).</summary>
    /// GET /api/account/data-policy
    [HttpGet("data-policy")]
    public IActionResult DataPolicy() => Ok(new
    {
        syncedHistoryMonths = DataRetentionPolicy.SyncedHistoryMonths(config),
        accessLogEnabled = string.Equals(config["AccessLog:Mode"], "Authentication", StringComparison.OrdinalIgnoreCase),
        accessLogRetentionDays = DataRetentionPolicy.AccessLogRetentionDays(config),
    });

    /// <summary>Cópia legível de todos os dados que o Control Finance guarda sobre o usuário.</summary>
    /// GET /api/account/export
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return NotFound();

        var accounts = await db.BankAccounts.Where(b => b.UserId == UserId).ToListAsync(ct);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);
        var categories = await db.Categories.Where(c => c.UserId == UserId).ToListAsync(ct);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);
        var transactions = await db.Transactions.Where(t => t.UserId == UserId).OrderBy(t => t.DueDate).ToListAsync(ct);

        var export = new
        {
            geradoEm = DateTime.UtcNow,
            observacao = "Cópia dos dados guardados pelo Control Finance sobre você. O CPF/CNPJ não aparece porque não é guardado: só um código de verificação (hash) usado no login e na conexão bancária. Senha não é exportada (só existe em forma de hash irreversível).",
            cadastro = new
            {
                user.Name,
                user.Email,
                tipoDocumento = user.DocumentType,
                criadoEm = user.CreatedAt,
                lembretesDeVencimento = user.DueRemindersEnabled,
                avisoMensal = user.MonthlySummaryEnabled,
            },
            aceites = await db.ConsentRecords
                .Where(c => c.UserId == UserId)
                .OrderBy(c => c.AcceptedAt)
                .Select(c => new { tipo = c.Kind, versao = c.Version, referencia = c.Reference, aceitoEm = c.AcceptedAt })
                .ToListAsync(ct),
            conexoesBancarias = await db.PolpIntegrations
                .Where(p => p.UserId == UserId)
                .Select(p => new { instituicaoId = p.InstitutionId, status = p.Status, criadaEm = p.CreatedAt, ultimaSincronizacao = p.LastSyncedAt })
                .ToListAsync(ct),
            contas = accounts.Select(a => new
            {
                a.Id,
                a.Name,
                instituicaoId = a.BankCode,
                origem = a.PolpAccountId.HasValue ? "sincronizada" : "manual",
                saldo = a.Balance,
                tipo = a.PolpAccountType,
                subtipo = a.PolpAccountSubtype,
                numero = a.Number,
                bandeira = a.Brand,
                limite = a.CreditLimit,
                limiteUsado = a.UsedLimit,
                diaFechamento = a.ClosingDay,
                diaVencimento = a.DueDay,
                faturaAtual = a.CurrentInvoiceAmount,
                vencimentoFatura = a.InvoiceDueDate,
            }),
            categorias = categories.Select(c => new { c.Name, cor = c.Color }),
            transacoes = transactions.Select(t => new
            {
                nome = t.Name,
                descricao = t.Description,
                tipo = t.Type.ToString(),
                status = t.Status.ToString(),
                valor = t.Amount,
                data = t.DueDate,
                pagoEm = t.PaidAt,
                categoria = t.CategoryId.HasValue ? categoryNames.GetValueOrDefault(t.CategoryId.Value) : null,
                conta = t.BankAccountId.HasValue ? accountNames.GetValueOrDefault(t.BankAccountId.Value) : null,
                fixa = t.IsFixed,
            }),
            cartoesManuais = await db.CreditCards
                .Where(c => c.UserId == UserId)
                .Select(c => new { c.Name, bandeira = c.Brand, limite = c.CreditLimit, limiteUsado = c.UsedLimit, diaFechamento = c.ClosingDay, diaVencimento = c.DueDay })
                .ToListAsync(ct),
            // Registros de acesso só existem se AccessLog:Mode estiver ligado. São dados do
            // titular, então entram na cópia (LGPD art. 18, II).
            registrosDeAcesso = (await db.AccessLogs
                    .Where(a => a.UserId == UserId)
                    .OrderBy(a => a.OccurredAt)
                    .ToListAsync(ct))
                .Select(a => new { evento = a.Event, em = a.OccurredAt, ip = a.IpAddress }),
            parcelamentos = (await db.Installments
                .Where(i => i.UserId == UserId)
                .ToListAsync(ct))
                .Select(i => new { descricao = i.Description, valorTotal = i.TotalAmount, parcelas = i.TotalInstallments, parcelaAtual = i.CurrentInstallment, valorParcela = i.InstallmentAmount, proximoVencimento = i.NextDueDate }),
        };

        Response.Headers.Append("Cache-Control", "no-store");
        return File(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            "application/json",
            $"control-finance-meus-dados-{DateTime.UtcNow:yyyy-MM-dd}.json");
    }

    /// <summary>Liga/desliga os e-mails automáticos.</summary>
    /// PUT /api/account/notifications
    [HttpPut("notifications")]
    public async Task<IActionResult> UpdateNotifications([FromBody] NotificationPreferencesDto dto, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return NotFound();

        user.DueRemindersEnabled = dto.DueRemindersEnabled;
        user.MonthlySummaryEnabled = dto.MonthlySummaryEnabled;
        await db.SaveChangesAsync(ct);

        return Ok(AuthService.ToUserInfo(user));
    }

    /// <summary>
    /// Exclui a conta e todos os dados associados, de forma definitiva. Pede a senha de novo
    /// (uma sessão esquecida aberta não deveria bastar pra apagar tudo).
    ///
    /// O que acontece, nesta ordem:
    /// 1. pede à Polp a exclusão de cada conexão bancária (o que falhar fica numa fila sem dado
    ///    pessoal — só o id técnico da integração — e é refeito depois);
    /// 2. apaga do banco de dados, numa transação, tudo que pertence ao usuário;
    /// 3. derruba as sessões abertas e apaga o cookie.
    /// Cópias de segurança (backups) da hospedagem não são alteradas aqui: o dado sai delas
    /// conforme o ciclo de retenção do provedor (ver Política de Privacidade).
    /// </summary>
    /// POST /api/account/deletion
    [HttpPost("deletion")]
    public async Task<IActionResult> DeleteAccount([FromBody] ConfirmPasswordDto dto, CancellationToken ct)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var limiterKey = $"account-deletion:{UserId}";
        if (rateLimit.IsBlocked(limiterKey, $"account-deletion:{ipAddress}"))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Muitas tentativas. Tente novamente mais tarde." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            rateLimit.RegisterFailure(limiterKey, $"account-deletion:{ipAddress}");
            return BadRequest(new { message = "Senha incorreta." });
        }

        var providerPending = await disconnectService.RequestProviderDeletionForAccountAsync(UserId, ct);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Installment não tem FK pra Users (só UserId solto), então não sai no cascade.
            await db.Installments.Where(i => i.UserId == UserId).ExecuteDeleteAsync(ct);
            // O resto (contas, transações, categorias, cartões, decisões de recorrência,
            // integrações e registros de aceite) sai pelo cascade de Users no próprio Postgres.
            await db.Users.Where(u => u.Id == UserId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }

        sessionValidator.Invalidate(UserId);
        SessionCookie.Delete(HttpContext, config);

        return Ok(new
        {
            accountDeleted = true,
            providerDeletion = providerPending == 0 ? "requested" : "pending",
        });
    }
}
