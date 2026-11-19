using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
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
    /// configurado (em vez de um texto fixo que pode não bater com a configuração). Público porque a
    /// Política de Privacidade também mostra esses valores a quem ainda não tem conta; só expõe configuração.</summary>
    /// GET /api/account/data-policy
    [HttpGet("data-policy")]
    [AllowAnonymous]
    public IActionResult DataPolicy() => Ok(new
    {
        syncedHistoryMonths = DataRetentionPolicy.SyncedHistoryMonths(config),
        accessLogEnabled = string.Equals(config["AccessLog:Mode"], "Authentication", StringComparison.OrdinalIgnoreCase),
        accessLogRetentionDays = DataRetentionPolicy.AccessLogRetentionDays(config),
    });

    /// <summary>Cópia legível de todos os dados que o Control Finance guarda sobre o usuário.</summary>
    /// GET /api/account/export
    [HttpGet("export")]
    [EnableRateLimiting(UserOperationRateLimit.Export)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return NotFound();

        var accounts = await db.BankAccounts.AsNoTracking().Where(b => b.UserId == UserId).ToListAsync(ct);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);
        var categories = await db.Categories.AsNoTracking().Where(c => c.UserId == UserId).ToListAsync(ct);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.UserId == UserId).OrderBy(t => t.DueDate).ToListAsync(ct);

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
                fusoHorario = user.TimeZone,
                ultimaAtividadeEm = user.LastActivityAt,
                exclusaoPorInatividadePrevistaPara = user.InactivityNoticeEpoch == user.LastActivityAt ? user.InactivityDeletionAt : null,
            },
            aceites = (await db.ConsentRecords
                .AsNoTracking()
                .Where(c => c.UserId == UserId)
                .OrderBy(c => c.AcceptedAt)
                .Select(c => new { tipo = c.Kind, versao = c.Version, referencia = c.Reference, aceitoEm = c.AcceptedAt })
                .ToListAsync(ct))
                .Select(c => new { c.tipo, manifestacao = Models.ConsentKinds.Manifestation(c.tipo), c.versao, c.referencia, registradoEm = c.aceitoEm })
                .ToList(),
            conexoesBancarias = await db.PolpIntegrations
                .AsNoTracking()
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
                saldoAtualizadoEm = a.BalanceUpdatedAt,
                tipo = a.PolpAccountType,
                subtipo = a.PolpAccountSubtype,
                numero = a.Number,
                bandeira = a.Brand,
                categoriaCartao = a.ProductType,
                limite = a.CreditLimit,
                limiteUsado = a.UsedLimit,
                limiteDisponivel = a.AvailableLimit,
                diaFechamento = a.ClosingDay,
                diaVencimento = a.DueDay,
                faturaAtual = a.CurrentInvoiceAmount,
                vencimentoFatura = a.InvoiceDueDate,
                contaPadrao = a.IsDefault,
                titularidade = a.Ownership.ToString(),
            }),
            // Nome guardado só como HMAC (RecurrenceKey): código técnico sem sentido para o titular, fica de fora.
            decisoesDeRecorrencia = await db.RecurrenceDecisions.AsNoTracking()
                .Where(r => r.UserId == UserId).OrderBy(r => r.CreatedAt)
                .Select(r => new
                {
                    contaId = r.BankAccountId, situacao = r.Status.ToString(), frequenciaAssumida = r.AssumedFrequency.ToString(),
                    lembrete = r.ReminderRequested, criadoEm = r.CreatedAt, atualizadoEm = r.UpdatedAt,
                })
                .ToListAsync(ct),
            historicoSaldosDisponiveis = await db.BankBalanceObservations.AsNoTracking()
                .Where(x => x.UserId == UserId).OrderBy(x => x.ObservedAt)
                .Select(x => new { contaId = x.BankAccountId, dataInformadaPeloBanco = x.ObservedAt, saldoDisponivel = x.Balance })
                .ToListAsync(ct),
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
                dataHoraNoBanco = t.BankDateTime,
                tipoOperacao = t.BankOperationType,
                descricaoNoBanco = t.BankDescription,
                contraparteRazaoSocial = t.CounterpartyName,
                contraparteNomeComercial = t.CounterpartyAlias,
                contraparteCnpj = t.CounterpartyCnpj,
                contraparteTipoPessoa = t.CounterpartyPersonType,
                bancoContraparte = t.CounterpartyBankCode,
                contaContraparte = t.CounterpartyAccount,
                cartao = t.CardNumber,
                parcela = t.InstallmentNumber,
                totalParcelas = t.InstallmentTotal,
                valorOriginal = t.OriginalAmount,
                moedaOriginal = t.OriginalCurrency,
                mesFatura = t.BillMonth,
                classificadaComoTributoPelaPolp = t.PolpTaxPayment,
                orgaoArrecadador = t.TaxAuthority,
            }),
            // Registros de acesso só existem se AccessLog:Mode estiver ligado. São dados do
            // titular, então entram na cópia (LGPD art. 18, II).
            registrosDeAcesso = (await db.AccessLogs
                .AsNoTracking()
                    .Where(a => a.UserId == UserId)
                    .OrderBy(a => a.OccurredAt)
                    .ToListAsync(ct))
                .Select(a => new { evento = a.Event, em = a.OccurredAt, ip = a.IpAddress }),
            reservaDeEmergencia = (await db.EmergencyReserves
                .AsNoTracking()
                .Where(r => r.UserId == UserId)
                .ToListAsync(ct))
                .Select(r => new { contaVinculadaId = r.BankAccountId, atualizadaEm = r.UpdatedAt })
                .FirstOrDefault(),
            vinculosDePlanejamento = await db.PlanningAllocations
                .AsNoTracking()
                .Where(a => a.UserId == UserId)
                .OrderBy(a => a.CreatedAt)
                .Select(a => new { destino = a.GoalId == null ? "reserva" : "meta", metaId = a.GoalId,
                    investimentoId = a.ImportedInvestmentId, caixinhaId = a.ImportedReservedBalanceId, criadoEm = a.CreatedAt })
                .ToListAsync(ct),
            contratosDeEmprestimo = (await db.LoanContracts
                .AsNoTracking()
                .Where(l => l.UserId == UserId)
                .OrderBy(l => l.FirstObservedAt)
                .ToListAsync(ct))
                .Select(l => new { id = l.Id, integracaoId = l.IntegrationId, dados = JsonSerializer.Deserialize<LoanContractValues>(l.ValuesJson),
                    atualizadoNaInstituicaoEm = l.ProviderUpdatedAt, importadoEm = l.FirstObservedAt }),
            regrasDeRendimento = await db.InvestmentRateProfiles
                .AsNoTracking()
                .Where(p => p.UserId == UserId)
                .Select(p => new { contaId = p.BankAccountId, tipo = p.Kind, valor = p.Value, isentoDeIR = p.IncomeTaxExempt, atualizadaEm = p.UpdatedAt })
                .ToListAsync(ct),
            metasDeEconomia = (await db.SavingsGoals
                .AsNoTracking()
                .Where(g => g.UserId == UserId)
                .OrderBy(g => g.CreatedAt)
                .ToListAsync(ct))
                .Select(g => new { id = g.Id, nome = g.Name, valorAlvo = g.TargetAmount, prazo = g.Deadline, criadaEm = g.CreatedAt }),
            // Cadastro digitado pela própria empresa: vai completo, porque esta exportação é a cópia
            // dos dados da conta para o titular dela.
            funcionarios = (await db.Employees
                .AsNoTracking()
                .Include(e => e.PaymentMethods)
                .Where(e => e.UserId == UserId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct))
                .Select(e => new
                {
                    id = e.Id, nome = e.FullName, cpf = e.Cpf, cargo = e.Role, departamento = e.Department,
                    admissao = e.AdmissionDate, vinculo = e.EmploymentType?.ToString(), salarioBruto = e.GrossSalary,
                    situacao = e.Status.ToString(), desligamento = e.TerminationDate,
                    metodosDePagamento = ExportPaymentMethods(e.PaymentMethods),
                    criadoEm = e.CreatedAt, atualizadoEm = e.UpdatedAt,
                }),
            fornecedores = (await db.Suppliers
                .AsNoTracking()
                .Include(x => x.PaymentMethods)
                .Where(x => x.UserId == UserId)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct))
                .Select(x => new
                {
                    id = x.Id, razaoSocial = x.LegalName, nomeFantasia = x.TradeName, tipoDocumento = x.DocumentType,
                    documento = x.Document, categoria = x.Category,
                    metodosDePagamento = ExportPaymentMethods(x.PaymentMethods),
                    criadoEm = x.CreatedAt, atualizadoEm = x.UpdatedAt,
                }),
            marcacoesDePagamentoFuncionarios = await db.EmployeePaymentMarks
                .AsNoTracking()
                .Where(m => m.UserId == UserId)
                .OrderBy(m => m.ReferenceMonth)
                .Select(m => new { funcionarioId = m.EmployeeId, mes = m.ReferenceMonth, pago = m.Paid, atualizadoEm = m.UpdatedAt })
                .ToListAsync(ct),
            obrigacoesDaFolha = (await db.PayrollObligations
                .AsNoTracking()
                .Where(o => o.UserId == UserId)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync(ct))
                .Select(o => new
                {
                    id = o.Id, tipo = o.Kind.ToString(), funcionarioId = o.EmployeeId,
                    descricao = o.Description, valorInformado = o.Amount,
                    criadoEm = o.CreatedAt, atualizadoEm = o.UpdatedAt,
                }),
            historicoDaFolha = (await db.PayrollObligationEvents
                .AsNoTracking()
                .Where(x => x.UserId == UserId)
                .OrderBy(x => x.OccurredAt)
                .ToListAsync(ct))
                .Select(x => new
                {
                    obrigacaoId = x.ObligationId, tipo = x.Kind.ToString(), funcionarioId = x.EmployeeId, acao = x.Action, descricao = x.Description,
                    valorInformado = x.Amount, vencimento = x.DueDate, transacaoId = x.TransactionId, valorVinculado = x.PaymentAmount,
                    autorId = x.ActorUserId, registradoEm = x.OccurredAt,
                }),
            fechamentosDeCaixa = (await db.CashClosings
                .AsNoTracking()
                .Include(c => c.Items)
                .Where(c => c.UserId == UserId)
                .OrderBy(c => c.Day).ThenBy(c => c.ClosedAt)
                .ToListAsync(ct))
                .Select(c => new
                {
                    dia = c.Day, contaId = c.ScopeAccountId == Guid.Empty ? (Guid?)null : c.ScopeAccountId,
                    saldoInicial = c.OpeningBalance, entradas = c.Income, saidas = c.Expense, saldoFinal = c.ClosingBalance,
                    movimentacoes = c.TransactionCount, fechadoEm = c.ClosedAt, reabertoEm = c.ReopenedAt,
                    itens = c.Items.OrderBy(i => i.TransactionId).Select(i => new
                    {
                        transacaoId = i.TransactionId, tipo = i.Type.ToString(), classificacao = i.MovementKind.ToString(), valor = i.Amount,
                    }),
                }),
            historicoDeMarcacoesDePagamento = (await db.PayeePaymentMarkEvents
                .AsNoTracking()
                .Where(x => x.UserId == UserId)
                .OrderBy(x => x.OccurredAt).ThenBy(x => x.Version)
                .ToListAsync(ct))
                .Select(x => new
                {
                    funcionarioId = x.PayeeId,
                    mes = x.ReferenceMonth, versao = x.Version, statusAnterior = x.PreviousStatus, novoStatus = x.NewStatus,
                    valorInformadoAnterior = x.PreviousManualAmount, novoValorInformado = x.NewManualAmount,
                    identificadoPeloBanco = x.BankIdentified, valorIdentificadoPeloBanco = x.BankPaidAmount,
                    autorId = x.ActorUserId, registradoEm = x.OccurredAt,
                }),
        };

        Response.Headers.Append("Cache-Control", "no-store");
        Response.ContentType = "application/json";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"control-finance-meus-dados-{DateTime.UtcNow:yyyy-MM-dd}.json\"");
        using var writer = new System.Text.Json.Utf8JsonWriter(Response.BodyWriter, new() { Indented = true });
        async Task Flush()
        {
            writer.Flush();
            await Response.BodyWriter.FlushAsync(ct);
        }
        writer.WriteStartObject();
        // Preserve existing export keys. Stream new financial histories instead of buffering
        // every imported position/movement for a large portfolio in a single byte array.
        foreach (var property in export.GetType().GetProperties())
        {
            writer.WritePropertyName(property.Name);
            System.Text.Json.JsonSerializer.Serialize(writer, property.GetValue(export), property.PropertyType);
            await Flush();
        }
        await new ControlFinance.API.Services.Investments.InvestmentDataExport(db).WriteAsync(writer, UserId, Flush, ct);
        writer.WriteEndObject(); await Flush();
        return new EmptyResult();
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

    /// <summary>Registra a ciência (não consentimento nem aceite contratual) da versão vigente da
    /// Política de Privacidade: append-only, com usuário, versão exata, data e tipo (privacy_notice).
    /// Versão diferente da vigente é recusada: só se confirma o texto exibido. Versão marcada como
    /// exigindo consentimento não aceita ciência; precisa de autorização específica.</summary>
    /// POST /api/account/privacy-acknowledgement
    [HttpPost("privacy-acknowledgement")]
    public async Task<IActionResult> AcknowledgePrivacy([FromBody] PrivacyAcknowledgementDto dto, CancellationToken ct)
    {
        if (dto.Version != LegalDocuments.PrivacyVersion)
            return BadRequest(new { message = "A Política de Privacidade foi atualizada. Recarregue a página." });
        if (LegalDocuments.PrivacyVersionRequiresConsent)
            return Conflict(new { message = "Esta atualização exige uma autorização específica, que não é dada pelo aviso." });
        if (!await LegalDocuments.HasCurrentPrivacyRecordAsync(db, UserId, LegalDocuments.PrivacyVersion, ct))
        {
            db.ConsentRecords.Add(new Models.ConsentRecord
            {
                UserId = UserId, Kind = Models.ConsentKinds.PrivacyNotice, Version = LegalDocuments.PrivacyVersion,
            });
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>
    /// Exclui a conta e todos os dados associados, de forma definitiva. Pede a senha de novo
    /// (uma sessão esquecida aberta não deveria bastar pra apagar tudo).
    ///
    /// O que acontece, nesta ordem:
    /// 1. pede à Polp a revogação de cada consentimento do usuário, em qualquer status;
    /// 2. numa transação: enfileira a revogação de todo consentimento que a Polp não confirmou (fila
    ///    sem dado pessoal, só o id técnico, refeita de hora em hora até a Polp confirmar) e apaga
    ///    do banco de dados tudo que pertence ao usuário;
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

        // Depois da senha conferida, a exclusão vai até o fim mesmo se o navegador fechar a conexão.
        var providerPending = await new AccountDeletionService(db, disconnectService, sessionValidator).DeleteAsync(UserId);
        SessionCookie.Delete(HttpContext, config);

        return Ok(new
        {
            accountDeleted = true,
            providerDeletion = providerPending == 0 ? "requested" : "pending",
        });
    }

    private static IEnumerable<object> ExportPaymentMethods(IEnumerable<Models.PayeePaymentMethod> methods) =>
        methods.OrderBy(m => m.CreatedAt).Select(m => m.Kind == Models.PayeePaymentKind.Pix
            ? (object)new { tipo = "Pix", tipoDeChave = m.PixKeyType.ToString(), chave = m.PixKey, documentoDoTitular = m.HolderDocument, criadoEm = m.CreatedAt }
            : new { tipo = "Transferencia", banco = m.BankCode, agencia = m.Branch, conta = m.Account, tipoDeConta = m.AccountType.ToString(), documentoDoTitular = m.HolderDocument, criadoEm = m.CreatedAt });
}
