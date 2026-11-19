namespace ControlFinance.API.Models;

// Tipos de vínculo comuns, só para organizar o cadastro. Nenhum cálculo depende do valor escolhido
// ainda: encargos, regime tributário e alíquotas não são presumidos a partir dele.
public enum EmploymentType { Clt, Temporario, Estagio, Aprendiz, Autonomo, PrestadorPj, Outro }

public enum EmployeeStatus { Active, Terminated }

public enum PixKeyType { Cpf, Cnpj, Email, Phone, Random }

public enum PayeeAccountType { Corrente, Poupanca, Pagamento, Salario }

public enum PayeePaymentKind { Pix, Bank }

// Método de pagamento cadastrado (Pix ou conta para TED), para reconhecer pagamentos no futuro.
// Não prova titularidade e não dispara transferência nenhuma. Funcionário e fornecedor podem ter
// vários; cada um tem a própria tabela (base não mapeada, colunas iguais).
public abstract class PayeePaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PayeePaymentKind Kind { get; set; }

    public PixKeyType? PixKeyType { get; set; }
    public string? PixKey { get; set; }
    public string? PixKeyHash { get; set; }

    public string? BankCode { get; set; }
    public string? Branch { get; set; }
    public string? Account { get; set; }
    public PayeeAccountType? AccountType { get; set; }

    /// <summary>CPF ou CNPJ de quem recebe nesse método (declarado pela empresa; pode não ser o do
    /// cadastro). Só para TED e chaves Pix e-mail, celular ou aleatória: chave CPF/CNPJ já é o documento
    /// do titular. Cifrado. Nulo em métodos antigos, que nunca recebem documento presumido.</summary>
    public string? HolderDocumentType { get; set; }
    public string? HolderDocument { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EmployeePaymentMethod : PayeePaymentMethod
{
    public Guid EmployeeId { get; set; }
}

public class SupplierPaymentMethod : PayeePaymentMethod
{
    public Guid SupplierId { get; set; }
}

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

    public List<EmployeePaymentMethod> PaymentMethods { get; set; } = [];

    /// <summary>Gastos iniciais (13º salário e Férias) já criados: um gasto excluído nunca volta.</summary>
    public bool CostsSeeded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
