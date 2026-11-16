using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public static class EmployeeKeys
{
    public static string CpfLookupInput(Guid companyId, string cpf) => PayeeKeys.LookupInput("employee-cpf", companyId, cpf);

    public static string PixLookupInput(Guid companyId, PixKeyType type, string normalizedKey) =>
        PayeeKeys.LookupInput("employee-pix", companyId, $"{type}:{normalizedKey}");
}
