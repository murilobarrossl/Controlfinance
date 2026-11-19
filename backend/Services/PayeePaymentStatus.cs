using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public enum PayeePaymentSource { Bank, ManualPaid, ManualUnpaid }

// Saída bancária ligada ao cadastro só pelo documento do destinatário (nível identificado).
public sealed record PayeeBankPayment(Guid TransactionId, DateOnly Date, decimal Amount, string? AccountName, string? AccountNumber);

// States: estado final por cadastro (sem entrada = não pago). Bank: pagamentos identificados pelo
// banco no mês, independentes da decisão manual. O único valor pago é o bancário: a marcação manual
// é só status. Lista, detalhe e resumo leem sempre daqui.
public sealed record PayeeMonthStatus(Dictionary<Guid, PayeePaymentSource> States, Dictionary<Guid, List<PayeeBankPayment>> Bank)
{
    public decimal? BankTotal(Guid id) => Bank.TryGetValue(id, out var payments) ? payments.Sum(p => p.Amount) : null;
}

// Quem recebeu ao menos uma saída identificada no mês corrente. "Pago" aqui é pagamento bancário
// identificado, não folha ou fatura quitada. Só saídas efetivadas vindas do banco da própria empresa:
// lançamento futuro, transferência entre contas próprias, pagamento de fatura e empréstimo recebido
// ficam de fora. Filtro barato no banco (índice UserId+DueDate, evidências não nulas); a comparação
// roda em memória porque CNPJ e nome são cifrados e CPF e conta existem só como hash.
public static class PayeePaymentStatus
{
    private static IQueryable<Transaction> MonthOutgoing(AppDbContext db, Guid userId, DateTime today)
    {
        var start = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return Outgoing(db, userId, start, start.AddMonths(1));
    }

    private static IQueryable<Transaction> Outgoing(AppDbContext db, Guid userId, DateTime start, DateTime end)
    {
        return db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.BankAccountId != null && t.BankAccount!.IsActive
                && t.Type == TransactionType.Expense && t.Status == TransactionStatus.Paid
                && t.PolpMovementKind == PolpMovementKind.None
                // Cartão fica de fora: sem partie_* do banco, o CNPJ dele vem só do enriquecimento da
                // Polp (fonte e método não documentados), que não é evidência bancária do destinatário.
                && t.BankAccount!.PolpAccountType != CreditCardAccountDetector.CardType
                && t.DueDate >= start && t.DueDate < end);
    }

    // Só linhas com documento ou conta completa do destinatário entram; o resto não tem como ser ligado.
    private static Task<List<PaymentEvidence>> EvidenceAsync(IQueryable<Transaction> query, CancellationToken ct) =>
        query.Where(t => t.CounterpartyCnpj != null || t.CounterpartyCpfHash != null || t.CounterpartyAccountHash != null)
            .Select(t => new PaymentEvidence(t.CounterpartyCnpj, t.CounterpartyCpfHash, t.CounterpartyAccountHash,
                t.CounterpartyPersonType, t.CounterpartyCnpjFromBank ? t.CounterpartyCnpj : null,
                t.Id, t.Amount, t.DueDate, t.BankAccount!.Name, t.BankAccount.Number))
            .ToListAsync(ct);

    public static bool IsValidFilter(string? filter) => filter is null or "paid" or "unpaid";

    public static DateOnly ReferenceMonth(DateTime today) => new(today.Year, today.Month, 1);

    public static string MonthKey(DateOnly month) => month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    // O cliente informa o mês que estava vendo: na virada do mês, uma marcação feita na tela do mês
    // anterior é recusada em vez de cair silenciosamente no mês novo.
    public static bool IsCurrentMonth(string? key, DateOnly month) => key == MonthKey(month);

    public static bool IsPaid(PayeePaymentSource? source) => source is PayeePaymentSource.Bank or PayeePaymentSource.ManualPaid;

    // Lista: só o estado final. "Bank" e "Manual" ficam distintos para nunca apresentar uma
    // declaração da empresa como confirmação do banco; null = não pago neste mês.
    public static string? Label(Dictionary<Guid, PayeePaymentSource> states, Guid id) =>
        states.TryGetValue(id, out var source) ? source switch
        {
            PayeePaymentSource.Bank => "Bank",
            PayeePaymentSource.ManualPaid => "Manual",
            _ => null,
        } : null;

    public static bool Passes(string? filter, string? status) => filter switch
    {
        "paid" => status is not null,
        "unpaid" => status is null,
        _ => true,
    };

    // A decisão manual do mês prevalece sobre a identificação bancária; sem decisão, vale o banco.
    // As duas fontes continuam separadas: a identificação nunca é gravada e a decisão nunca muda transação.
    private static PayeeMonthStatus Combine(Dictionary<Guid, List<PayeeBankPayment>> bank, Dictionary<Guid, bool> marks)
    {
        var states = bank.Keys.ToDictionary(id => id, _ => PayeePaymentSource.Bank);
        foreach (var (id, paid) in marks)
            states[id] = paid ? PayeePaymentSource.ManualPaid : PayeePaymentSource.ManualUnpaid;
        return new PayeeMonthStatus(states, bank);
    }

    private static PayeePaymentSource? Resolve(bool? manual, bool bankIdentified) => manual switch
    {
        true => PayeePaymentSource.ManualPaid,
        false => PayeePaymentSource.ManualUnpaid,
        null => bankIdentified ? PayeePaymentSource.Bank : null,
    };

    private static string StatusName(PayeePaymentSource? source) => source?.ToString() ?? "None";

    // Só identificadores exatos do banco (CPF do titular ou conta completa; ver SupplierPaymentMatcher)
    // contam como pagamento bancário. Cada
    // transação entra uma vez (o matcher devolve no máximo um cadastro por transação) e o valor é o
    // registrado pelo banco, sem estimativa; a soma é em decimal.
    private static Dictionary<Guid, List<PayeeBankPayment>> Identified(List<PaymentEvidence> evidence, Func<PaymentEvidence, PayeeMatch?> match)
    {
        var result = new Dictionary<Guid, List<PayeeBankPayment>>();
        foreach (var e in evidence.DistinctBy(e => e.TransactionId))
        {
            if (match(e) is not { Level: PayeePaymentLevel.Identified } m) continue;
            if (!result.TryGetValue(m.Id, out var payments)) result[m.Id] = payments = [];
            payments.Add(new PayeeBankPayment(e.TransactionId, DateOnly.FromDateTime(e.Date), e.Amount, e.AccountName, e.AccountNumber));
        }
        foreach (var payments in result.Values)
            payments.Sort((a, b) => a.Date != b.Date ? a.Date.CompareTo(b.Date) : a.TransactionId.CompareTo(b.TransactionId));
        return result;
    }

    public static PayeePaymentMonthDto Describe(PayeeMonthStatus status, Guid id, DateOnly month)
    {
        var source = status.States.TryGetValue(id, out var s) ? s : (PayeePaymentSource?)null;
        var payments = status.Bank.GetValueOrDefault(id) ?? [];
        return new PayeePaymentMonthDto(MonthKey(month), IsPaid(source), source?.ToString(), payments.Count > 0,
            status.BankTotal(id),
            payments.Select(p => new PayeeBankPaymentDto(p.Date, p.Amount, p.AccountName, p.AccountNumber)).ToList());
    }

    // Grava a decisão do mês e, no mesmo SaveChanges (uma transação), o evento de auditoria. Version é
    // token de concorrência: se outra gravação mudou a marcação entre a leitura e a escrita, esta falha,
    // relê o estado e tenta de novo, então o "estado anterior" de cada evento é sempre o efetivo e a
    // ordem das versões é a ordem real. Insert simultâneo esbarra no índice único e cai no mesmo caminho.
    // Repetir a decisão atual não gera evento. Uma conta = um titular, por isso o autor é o próprio UserId.
    public static async Task SaveMarkAsync<TMark>(AppDbContext db, Func<Task<TMark?>> find, Func<TMark> create,
        PayeeKind kind, Guid userId, Guid payeeId, DateOnly month, bool paid, IReadOnlyList<PayeeBankPayment>? bankPayments, CancellationToken ct)
        where TMark : PayeePaymentMark
    {
        var bankIdentified = bankPayments is { Count: > 0 };
        for (var attempt = 0; ; attempt++)
        {
            var mark = await find();
            if (mark is not null && mark.Paid == paid) return;
            var previous = Resolve(mark?.Paid, bankIdentified);
            if (mark is null)
            {
                mark = create();
                db.Add(mark);
            }
            var now = DateTime.UtcNow;
            mark.Paid = paid;
            mark.Version++;
            mark.UpdatedAt = now;
            db.PayeePaymentMarkEvents.Add(new PayeePaymentMarkEvent
            {
                UserId = userId,
                ActorUserId = userId,
                PayeeKind = kind,
                PayeeId = payeeId,
                ReferenceMonth = month,
                Version = mark.Version,
                PreviousStatus = StatusName(previous),
                NewStatus = StatusName(Resolve(paid, bankIdentified)),
                BankIdentified = bankIdentified,
                BankPaidAmount = bankIdentified ? bankPayments!.Sum(p => p.Amount) : null,
                OccurredAt = now,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private static HashSet<string> CpfHashes(IEncryptionService encryption, Guid userId, string cpf) =>
        encryption.ComputeLookupHashCandidates(EmployeeKeys.CpfLookupInput(userId, PayeeKeys.Digits(cpf))).ToHashSet();

    // Documentos que identificam quem recebe: o CPF do funcionário e o titular de cada método (chave Pix
    // CPF/CNPJ ou titular declarado). O titular de uma TED fica preso à conta dela.
    private static EmployeeIdentity Identity(IEncryptionService encryption, Guid userId, Employee e)
    {
        var holders = new List<PayeeHolder> { new(CpfHashes(encryption, userId, e.Cpf), null) };
        var accounts = new List<PayeeAccount>();
        foreach (var m in e.PaymentMethods)
        {
            HashSet<string>? accountHashes = null;
            if (m.Kind == PayeePaymentKind.Bank)
            {
                if (PayeeKeys.AccountLookupInput(userId, m.BankCode, m.Branch, m.Account) is not { } input) continue;
                accountHashes = encryption.ComputeLookupHashCandidates(input).ToHashSet();
                accounts.Add(new PayeeAccount(accountHashes, m.HolderDocumentType));
            }
            if (PayeePayment.Holder(m) is { } holder)
                holders.Add(holder.Type == DocumentTypes.Cpf
                    ? new PayeeHolder(CpfHashes(encryption, userId, holder.Digits), null, accountHashes)
                    : new PayeeHolder(null, holder.Digits, accountHashes));
        }
        return new EmployeeIdentity(e.Id, holders, accounts);
    }

    public static async Task<PayeeMonthStatus> EmployeesAsync(
        AppDbContext db, IEncryptionService encryption, Guid userId, DateTime today, CancellationToken ct)
    {
        var month = ReferenceMonth(today);
        var marks = await db.EmployeePaymentMarks.AsNoTracking()
            .Where(m => m.UserId == userId && m.ReferenceMonth == month)
            .ToDictionaryAsync(m => m.EmployeeId, m => m.Paid, ct);
        var bank = await EmployeesBankAsync(db, encryption, userId, MonthOutgoing(db, userId, today), ct);
        return Combine(bank, marks);
    }

    /// <summary>Estado do mês de um funcionário, com o mesmo resultado de EmployeesAsync para ele.</summary>
    public static async Task<PayeeMonthStatus> EmployeeAsync(
        AppDbContext db, IEncryptionService encryption, Guid userId, Guid employeeId, DateTime today, CancellationToken ct)
    {
        var bank = await EmployeeBankAsync(db, encryption, userId, employeeId, MonthOutgoing(db, userId, today), ct);
        return await WithMarkAsync(db, userId, employeeId, ReferenceMonth(today), bank, ct);
    }

    /// <summary>Relê só a marcação do mês e a combina com a parte bancária já calculada.</summary>
    public static async Task<PayeeMonthStatus> WithMarkAsync(
        AppDbContext db, Guid userId, Guid employeeId, DateOnly month, Dictionary<Guid, List<PayeeBankPayment>> bank, CancellationToken ct)
    {
        var marks = await db.EmployeePaymentMarks.AsNoTracking()
            .Where(m => m.UserId == userId && m.EmployeeId == employeeId && m.ReferenceMonth == month)
            .ToDictionaryAsync(m => m.EmployeeId, m => m.Paid, ct);
        return Combine(bank, marks);
    }

    // O ramo do matcher (CPF, CNPJ do banco ou conta) depende só da transação, então o funcionário é candidato
    // numa saída com a empresa inteira se e só se for candidato sozinho. Sem saída candidata ele não é
    // identificado e nenhum outro cadastro precisa ser lido; com ela, os demais entram só para checar a
    // ambiguidade (mais de um candidato = nada) nessas saídas.
    private static async Task<Dictionary<Guid, List<PayeeBankPayment>>> EmployeeBankAsync(
        AppDbContext db, IEncryptionService encryption, Guid userId, Guid employeeId, IQueryable<Transaction> outgoing, CancellationToken ct)
    {
        var evidence = await EvidenceAsync(outgoing
            .Where(t => t.CounterpartyCpfHash != null || t.CounterpartyCnpjFromBank || t.CounterpartyCnpj == null), ct);
        if (evidence.Count == 0) return [];

        var target = await db.Employees.AsNoTracking().Include(e => e.PaymentMethods)
            .SingleOrDefaultAsync(e => e.UserId == userId && e.Id == employeeId, ct);
        if (target is null) return [];
        var self = Identity(encryption, userId, target);
        var candidates = evidence.Where(e => SupplierPaymentMatcher.MatchEmployee(e, [self]) is not null).ToList();
        if (candidates.Count == 0) return [];

        var everyone = (await db.Employees.AsNoTracking().Include(e => e.PaymentMethods)
                .Where(e => e.UserId == userId && e.Id != employeeId).ToListAsync(ct))
            .Select(e => Identity(encryption, userId, e))
            .Append(self)
            .ToList();
        var bank = Identified(candidates, e => SupplierPaymentMatcher.MatchEmployee(e, everyone));
        return bank.TryGetValue(employeeId, out var payments) ? new() { [employeeId] = payments } : [];
    }

    private static async Task<Dictionary<Guid, List<PayeeBankPayment>>> EmployeesBankAsync(
        AppDbContext db, IEncryptionService encryption, Guid userId, IQueryable<Transaction> outgoing, CancellationToken ct)
    {
        // CNPJ só do enriquecimento sem CPF do banco não é evidência; CNPJ do banco pode ser titular de um método.
        var evidence = await EvidenceAsync(outgoing
            .Where(t => t.CounterpartyCpfHash != null || t.CounterpartyCnpjFromBank || t.CounterpartyCnpj == null), ct);
        if (evidence.Count == 0) return [];

        var employees = (await db.Employees.AsNoTracking().Include(e => e.PaymentMethods).Where(e => e.UserId == userId).ToListAsync(ct))
            .Select(e => Identity(encryption, userId, e))
            .ToList();
        return Identified(evidence, e => SupplierPaymentMatcher.MatchEmployee(e, employees));
    }
}
