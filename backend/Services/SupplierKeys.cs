using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public static class SupplierKeys
{
    public static string DocumentLookupInput(Guid companyId, string document) => PayeeKeys.LookupInput("supplier-doc", companyId, document);

    public static string PixLookupInput(Guid companyId, PixKeyType type, string normalizedKey) =>
        PayeeKeys.LookupInput("supplier-pix", companyId, $"{type}:{normalizedKey}");
}
