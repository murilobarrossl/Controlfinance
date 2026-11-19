namespace ControlFinance.API.DTOs;

// Gasto adicional com nome livre e valor opcional informado pela empresa.
public record PayrollObligationDto(
    Guid Id,
    string Kind,
    Guid? EmployeeId,
    string? Description,
    decimal? Amount,
    DateTime UpdatedAt);

// Total = soma dos valores informados; gasto sem valor fica fora.
public record EmployeeCostsDto(IReadOnlyList<PayrollObligationDto> Items, decimal Total);

// Amount nulo = valor ainda não informado.
public record EmployeeCostInputDto(string? Name, decimal? Amount);
