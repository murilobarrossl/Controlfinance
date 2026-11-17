using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services.Investments;

// Investimentos e caixinhas são só CPF, mas o consentimento CNPJ também pede INVESTMENTS
// (PolpService.DefaultProducts): sem este filtro a Polp avisa e a importação roda para CNPJ.
// Tipo desconhecido segue a experiência CPF, como no frontend.
public static class InvestmentHolder
{
    public static Task<bool> AllowsAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.DocumentType != DocumentTypes.Cnpj, ct);

    public static Task<bool> AllowsIntegrationAsync(AppDbContext db, Guid integrationId, CancellationToken ct) =>
        db.PolpIntegrations.AnyAsync(p => p.Id == integrationId
            && db.Users.Any(u => u.Id == p.UserId && u.DocumentType != DocumentTypes.Cnpj), ct);
}
