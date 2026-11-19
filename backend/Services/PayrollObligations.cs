using ControlFinance.API.Data;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public static class PayrollObligations
{
    public const decimal MaxAmount = 999_999_999.99m;

    public static bool IsValidAmount(decimal value) => value > 0 && value <= MaxAmount && decimal.Round(value, 2) == value;

    public const int MaxCostsPerEmployee = 50;

    public static void Audit(AppDbContext db, Guid userId, PayrollObligation o, string action) =>
        db.PayrollObligationEvents.Add(new PayrollObligationEvent
        {
            UserId = userId,
            ActorUserId = userId,
            ObligationId = o.Id,
            Kind = o.Kind,
            EmployeeId = o.EmployeeId,
            Action = action,
            Description = o.Description,
            Amount = o.Amount,
        });
}
