using System.ComponentModel.DataAnnotations;

namespace ControlFinance.API.DTOs;

// Cadastro rápido e edição dos dados principais. Na edição, Cpf vazio mantém o atual (o CPF nunca
// volta inteiro para a tela). Departamento, vínculo e dados de pagamento têm rotas próprias.
public record EmployeeCoreDto(
    [Required(ErrorMessage = "Informe o nome completo."), MaxLength(150, ErrorMessage = "O nome pode ter até 150 caracteres.")] string FullName,
    string? Cpf,
    [Required(ErrorMessage = "Informe o cargo ou função."), MaxLength(100, ErrorMessage = "O cargo pode ter até 100 caracteres.")] string Role,
    decimal GrossSalary,
    DateOnly AdmissionDate);

// EmploymentType nulo = não informado.
public record EmployeeClassificationDto(
    [MaxLength(80, ErrorMessage = "O departamento pode ter até 80 caracteres.")] string? Department,
    string? EmploymentType);

public record EmployeeTerminationDto(DateOnly TerminationDate);

public record EmployeeListItemDto(
    Guid Id,
    string FullName,
    string Role,
    string? Department,
    DateOnly AdmissionDate,
    string? EmploymentType,
    decimal GrossSalary,
    string Status,
    DateOnly? TerminationDate,
    bool HasPix,
    bool HasBankAccount,
    string? PaymentStatus,
    // Soma das saídas identificadas pelo banco no mês; null = nenhuma. Nunca é o salário cadastrado.
    decimal? BankPaidAmount = null);

public record EmployeeListDto(IReadOnlyList<EmployeeListItemDto> Items, int TotalCount, int Page, int PageSize);

/// <summary>ActiveGrossSalaryTotal é só a soma dos salários brutos informados dos ativos, não o
/// custo da folha: encargos ainda não são calculados.</summary>
public record EmployeeSummaryDto(int ActiveCount, int TotalCount, decimal ActiveGrossSalaryTotal, IReadOnlyList<string> Departments);

public record EmployeeDetailDto(
    Guid Id,
    string FullName,
    string CpfMasked,
    string Role,
    string? Department,
    DateOnly AdmissionDate,
    string? EmploymentType,
    decimal GrossSalary,
    string Status,
    DateOnly? TerminationDate,
    IReadOnlyList<PayeePaymentMethodView> PaymentMethods,
    DateTime CreatedAt,
    DateTime UpdatedAt);
