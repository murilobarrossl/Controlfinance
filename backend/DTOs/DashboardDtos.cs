using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;
using ControlFinance.API.Services;

namespace ControlFinance.API.DTOs;

// ── BANK ACCOUNT ──────────────────────────────────────
public record CreateBankAccountDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(100)] string Name,
    [MaxLength(20)] string? BankCode,
    [Range(-1_000_000_000, 1_000_000_000, ErrorMessage = "Saldo fora da faixa permitida.")] decimal Balance
);
public record BankAccountDto(Guid Id, string Name, string? BankCode, decimal Balance, bool IsActive, string Ownership);

// Ownership: "Personal" | "Business" | "Mixed", string na borda da API, igual Type/Status de
// TransactionDto, pra não vazar o valor numérico cru do enum (o padrão do System.Text.Json sem
// conversor: JSON já mandava "ownership":0 antes dessa mudança).
public record SetAccountOwnershipDto(string Ownership);

// ── CATEGORY ──────────────────────────────────────────
public record CreateCategoryDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    [MaxLength(20)] string? Color
);
public record CategoryDto(Guid Id, string Name, string? Color);

// ── TRANSACTION ───────────────────────────────────────
public record CreateTransactionDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(150)] string Name,
    [MaxLength(500)] string? Description,
    string Type,         // "Income" | "Expense"
    string Status,       // "Pending" | "Paid" | "Overdue"
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor deve ser maior que zero.")] decimal Amount,
    DateTime DueDate,
    Guid? BankAccountId,
    Guid? CategoryId,
    bool IsFixed = false
);

public record SetTransactionFixedDto(bool IsFixed);

// Edição rápida de nome/categoria (ex.: a Polp categorizou errado, "Multa" virou "Multas de
// trânsito" sem ter carro) sem precisar montar o CreateTransactionDto inteiro. A lista de
// transações não expõe BankAccountId/CategoryId como Guid (só os nomes), então não dá pra
// reconstruir esse DTO no cliente a partir do que a tela já tem.
public record SetTransactionDetailsDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(150)] string Name,
    Guid? CategoryId
);

public record TransactionReportDto(
    IEnumerable<TransactionDto> Items,
    int TotalCount,
    decimal TotalIncome,
    decimal TotalExpense
);

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
    // ownerName só é conhecido em quem lista várias transações (GetAll/GetReport/GetSummary), que
    // já busca o nome do usuário uma vez; nula aqui deixa IsTransfer em false (comportamento atual).
    public static TransactionDto FromEntity(Transaction t, string? ownerName = null) => new(
        t.Id, t.Name, t.Description,
        t.Type.ToString(), EffectiveStatus(t.Status, t.DueDate),
        t.Amount, t.DueDate, t.PaidAt,
        t.Category?.Name,
        t.BankAccount?.Name,
        t.IsFixed,
        TransferDetection.IsSelfTransfer(t.Name, t.Category?.Name, ownerName)
    );

    // Não existe job de fundo que varre pendências vencidas e grava Overdue no banco: calcula na
    // leitura em vez disso, senão uma conta "Pending" cuja data já passou ficava mostrando
    // "Pendente" pra sempre em vez de "Atrasado". Só se aplica a Pending: Paid/Overdue (setado à
    // mão pelo usuário) continuam como estão.
    // Compara por dia (não pelo instante exato): uma conta que vence hoje não pode virar "Atrasado"
    // só porque já passou da meia-noite UTC, continua Pending até o dia do vencimento acabar.
    public static string EffectiveStatus(TransactionStatus status, DateTime dueDate) =>
        status == TransactionStatus.Pending && dueDate.Date < DateTime.UtcNow.Date
            ? nameof(TransactionStatus.Overdue)
            : status.ToString();
}

// ── CREDIT CARD ───────────────────────────────────────
public record CreateCreditCardDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    [MaxLength(50)] string? Brand,
    [Range(0, double.MaxValue, ErrorMessage = "Limite não pode ser negativo.")] decimal CreditLimit,
    [Range(1, 31, ErrorMessage = "Dia de fechamento deve ser entre 1 e 31.")] int ClosingDay,
    [Range(1, 31, ErrorMessage = "Dia de vencimento deve ser entre 1 e 31.")] int DueDay,
    Guid? BankAccountId = null
);

public record CreditCardDto(
    Guid Id,
    string Name,
    string? Brand,
    decimal CreditLimit,
    decimal UsedLimit,
    decimal AvailableLimit,
    int ClosingDay,
    int DueDay,
    Guid? BankAccountId,
    string? BankAccountName
)
{
    public static CreditCardDto FromEntity(CreditCard c) => new(
        c.Id, c.Name, c.Brand,
        c.CreditLimit, c.UsedLimit,
        c.CreditLimit - c.UsedLimit,
        c.ClosingDay, c.DueDay,
        c.BankAccountId, c.BankAccount?.Name
    );
}

// ── INSTALLMENT ───────────────────────────────────────
public record CreateInstallmentDto(
    Guid? CreditCardId,
    [Required(ErrorMessage = "Descrição é obrigatória."), MaxLength(150)] string Description,
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor total deve ser maior que zero.")] decimal TotalAmount,
    [Range(1, int.MaxValue, ErrorMessage = "Total de parcelas deve ser maior que zero.")] int TotalInstallments,
    [Range(1, int.MaxValue, ErrorMessage = "Parcela atual deve ser maior que zero.")] int CurrentInstallment,
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor da parcela deve ser maior que zero.")] decimal InstallmentAmount,
    DateTime NextDueDate
);

public record InstallmentDto(
    Guid Id,
    string Description,
    decimal InstallmentAmount,
    int CurrentInstallment,
    int TotalInstallments,
    DateTime NextDueDate,
    string? CreditCardName
)
{
    public static InstallmentDto FromEntity(Installment i) => new(
        i.Id, i.Description, i.InstallmentAmount,
        i.CurrentInstallment, i.TotalInstallments, i.NextDueDate,
        i.CreditCard?.Name
    );
}

// ── DASHBOARD ─────────────────────────────────────────
public record DashboardSummaryDto(
    BankAccountDto ActiveAccount,
    decimal TotalIncome,
    decimal TotalExpense,
    IEnumerable<TransactionDto> PendingTransactions,
    IEnumerable<CategoryExpenseDto> CategoryExpenses,
    CreditCardDashboardDto? ActiveCard
);

public record CategoryExpenseDto(string CategoryName, decimal Amount, decimal Percentage);

public record CreditCardDashboardDto(
    CreditCardDto Card,
    decimal CurrentInvoice,
    DateTime InvoiceDueDate,
    IEnumerable<InstallmentDto> Installments
);
