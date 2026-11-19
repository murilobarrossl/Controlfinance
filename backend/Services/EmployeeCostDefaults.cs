using ControlFinance.API.Data;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Gastos iniciais de todo funcionário, sem valor e sem regra própria. Employee.CostsSeeded é gravada no mesmo
// SaveChanges: um gasto excluído depois nunca é recriado.
public static class EmployeeCostDefaults
{
    public static readonly string[] Names = ["13º salário", "Férias"];

    public static void Seed(AppDbContext db, Employee employee)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < Names.Length; i++)
        {
            var cost = new PayrollObligation
            {
                UserId = employee.UserId, EmployeeId = employee.Id, Kind = PayrollObligationKind.EmployeeCost, Description = Names[i],
                CreatedAt = now.AddMilliseconds(i), UpdatedAt = now,
            };
            db.PayrollObligations.Add(cost);
            PayrollObligations.Audit(db, employee.UserId, cost, "Created");
        }
        employee.CostsSeeded = true;
    }
}
