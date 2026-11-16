namespace ControlFinance.API.Models;

// Tipos de vínculo comuns, só para organizar o cadastro. Nenhum cálculo depende do valor escolhido
// ainda: encargos, regime tributário e alíquotas não são presumidos a partir dele.
public enum EmploymentType { Clt, Temporario, Estagio, Aprendiz, Autonomo, PrestadorPj, Outro }

public enum EmployeeStatus { Active, Terminated }

public enum PixKeyType { Cpf, Cnpj, Email, Phone, Random }

public enum PayeeAccountType { Corrente, Poupanca, Pagamento, Salario }

/// <summary>
/// Funcionário de uma empresa (usuário CNPJ). Cadastro administrativo digitado pela empresa: não
/// gera movimentação financeira (essas continuam vindo só da Polp). Desligar não apaga: o
/// histórico fica para custos e para o reconhecimento de pagamentos que vêm depois.
/// </summary>
public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // Cifrados (ver AppDbContext): dados pessoais do funcionário.
    public string FullName { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;

    /// <summary>HMAC do CPF com o id da empresa no texto (ver EmployeeKeys): impede o mesmo CPF
    /// duas vezes na empresa sem decifrar nada, e não liga o mesmo CPF entre empresas diferentes
    /// nem ao DocumentHash de um usuário.</summary>
    public string CpfHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
    public string? Department { get; set; }
    public DateOnly AdmissionDate { get; set; }
    /// <summary>Nulo quando a empresa ainda não informou: nunca se presume CLT.</summary>
    public EmploymentType? EmploymentType { get; set; }

    /// <summary>Salário bruto mensal informado. Não é custo total: encargos ainda não são calculados.</summary>
    public decimal GrossSalary { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public DateOnly? TerminationDate { get; set; }

    // Método de pagamento, para reconhecer pagamentos no futuro. Não prova titularidade e não
    // dispara transferência nenhuma.
    public PixKeyType? PixKeyType { get; set; }
    public string? PixKey { get; set; }
    public string? PixKeyHash { get; set; }

    public string? PayeeBankCode { get; set; }
    public string? PayeeBranch { get; set; }
    public string? PayeeAccount { get; set; }
    public PayeeAccountType? PayeeAccountType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
