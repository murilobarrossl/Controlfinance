using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;
using ControlFinance.API.Services;

namespace ControlFinance.API.DTOs;

// ── BANK ACCOUNT ──────────────────────────────────────
public record BankAccountDto(Guid Id, string Name, string? BankCode, decimal? Balance, bool IsActive, string Ownership)
{
    public static BankAccountDto FromEntity(BankAccount b) =>
        new(b.Id, b.Name, b.BankCode,
            b.PolpAccountId.HasValue && CreditCardAccountDetector.LooksLikeCreditCard(b) ? null : b.Balance,
            b.IsActive, b.Ownership.ToString());
}

// Ownership: "Personal" | "Business" | "Mixed", string na borda da API, igual Type/Status de
// TransactionDto, pra não vazar o valor numérico cru do enum (o padrão do System.Text.Json sem
// conversor: JSON já mandava "ownership":0 antes dessa mudança).
public record SetAccountOwnershipDto(string Ownership);

// ── CATEGORY ──────────────────────────────────────────
public record CreateCategoryDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    [MaxLength(20)] string? Color
);
public record CategoryDto(Guid Id, string Name, string? Color)
{
    public static CategoryDto FromEntity(Category c) => new(c.Id, c.Name, c.Color);
}

// ── TRANSACTION ───────────────────────────────────────
public record SetTransactionFixedDto(bool IsFixed);

// Os únicos campos que o usuário personaliza num lançamento importado (ex.: a Polp categorizou
// errado, "Multa" virou "Multas de trânsito" sem ter carro). Valor, data, tipo, conta e ids vêm só
// do banco.
public record SetTransactionDetailsDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(150)] string Name,
    Guid? CategoryId
);

public record AggregateMonthDto(int Year, int Month, decimal Income, decimal Expense, int Count);
public record AggregateCategoryDto(string? CategoryName, string Type, decimal Amount, int Count);
public record AggregateEstablishmentDto(string Key, string Label, string? CategoryName, string Type, decimal Amount, int Count, bool IsInstallmentGroup);
public record TransactionAggregatesDto(
    decimal TotalIncome,
    decimal TotalExpense,
    int Count,
    List<AggregateMonthDto> Months,
    List<AggregateCategoryDto> Categories,
    List<AggregateEstablishmentDto> Establishments,
    List<AggregateDayDto> Days);

public record AggregateDayDto(DateOnly Date, decimal Income, decimal Expense);

public record TransactionReportDto(
    IEnumerable<TransactionDto> Items,
    int TotalCount,
    decimal TotalIncome,
    decimal TotalExpense,
    IEnumerable<ReportDayTotalDto> DayTotals
);

public record ReportDayTotalDto(DateTime Date, decimal Income, decimal Expense);

public record TransactionDto(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    string Status,
    decimal Amount,
    DateTime DueDate,
    DateTime? PaidAt,
    string? CategoryName,
    string? BankAccountName,
    bool IsFixed,
    bool IsTransfer = false
)
{
    public static TransactionDto FromEntity(Transaction t, DateTime today) => new(
        t.Id, t.Name, t.Description,
        t.Type.ToString(), EffectiveStatus(t.Status, t.DueDate, today),
        t.Amount, t.DueDate, t.PaidAt,
        t.Category?.Name,
        t.BankAccount?.Name,
        t.IsFixed,
        TransferDetection.IsSelfTransfer(t.PolpMovementKind)
    );

    // Não existe job de fundo que varre pendências vencidas e grava Overdue no banco: calcula na
    // leitura em vez disso, senão uma conta "Pending" cuja data já passou ficava mostrando
    // "Pendente" pra sempre em vez de "Atrasado". Só se aplica a Pending: Paid/Overdue (setado à
    // mão pelo usuário) continuam como estão.
    // Compara por dia (não pelo instante exato): uma conta que vence hoje não pode virar "Atrasado"
    // só porque já passou da meia-noite UTC, continua Pending até o dia do vencimento acabar no
    // fuso do usuário (today vem de UserClock).
    public static string EffectiveStatus(TransactionStatus status, DateTime dueDate, DateTime today) =>
        status == TransactionStatus.Pending && dueDate.Date < today.Date
            ? nameof(TransactionStatus.Overdue)
            : status.ToString();
}

public record TransactionDetailDto(
    Guid Id,
    string Name,
    string Type,
    string Status,
    decimal Amount,
    DateTime DueDate,
    DateTime? PaidAt,
    DateTime CreatedAt,
    string? CategoryName,
    string? CategoryColor,
    bool IsFixed,
    string MovementKind,
    TransactionAccountDto Account,
    TransactionBankDataDto Bank
);

public record TransactionAccountDto(string Name, string? Number, bool IsCard, string? InstitutionName, DateTime? LastSyncedAt);

public record TransactionBankDataDto(
    DateTime? DateTime,
    string? OperationType,
    string? Description,
    string? CounterpartyAlias,
    string? CounterpartyLegalName,
    string? DescribedParty,
    string? CounterpartyCnpj,
    string? CounterpartyPersonType,
    string? CounterpartyBankCode,
    string? CounterpartyAccount,
    string? CardNumber,
    int? InstallmentNumber,
    int? InstallmentTotal,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    string? BillMonth,
    string DetailsStatus
);

// ── DASHBOARD ─────────────────────────────────────────
public record DashboardSummaryDto(
    DashboardAccountDto Account,
    decimal TotalIncome,
    decimal TotalExpense,
    IEnumerable<TransactionDto> PendingTransactions,
    IEnumerable<CategoryExpenseDto> CategoryExpenses,
    RecognizedCardDto? ActiveCard
);

// Kind: Checking | Payment | Savings | CreditCard. BalanceIncomplete: conta sincronizada (não
// cartão) cujo saldo a Polp ainda não mandou.
public record DashboardAccountDto(Guid Id, string Name, string Kind, decimal? Balance, bool BalanceIncomplete)
{
    public static DashboardAccountDto FromEntity(BankAccount b)
    {
        var kind = AccountScope.KindOf(b);
        var balance = BankAccountDto.FromEntity(b).Balance;
        return new(b.Id, b.Name, kind.ToString(), balance,
            b.PolpAccountId.HasValue && kind != AccountScopeKind.CreditCard && balance is null);
    }
}

public record CategoryExpenseDto(string CategoryName, decimal Amount, decimal Percentage);

// Um cartão "reconhecido": BankAccount identificada como cartão pela Polp
// (CreditCardAccountDetector) — sem entidade separada, é a própria conta. HasFullDetails=false é o
// estado "reconhecemos o cartão, faltam os campos que a Polp não mandou pra esse banco".
public record RecognizedCardDto(
    Guid Id,
    bool IsSynced,
    string Name,
    string? Brand,
    string? Number,
    decimal? CreditLimit,
    decimal? UsedLimit,
    int? ClosingDay,
    int? DueDay,
    decimal? CurrentInvoice,
    DateTime? InvoiceDueDate,
    bool HasFullDetails,
    string? ProductType,
    string? InstitutionColor,
    Guid? ConnectionId,
    string? BankName,
    DateTime? ConnectionLastSyncedAt
)
{
    public string? InvoiceStatus { get; init; }
    // Identity of the connected institution, not the local connection/account ID.
    public Guid? IssuerInstitutionId { get; init; }
    public string? OriginalProductName { get; init; }
    public decimal? AvailableLimit { get; init; }
}
