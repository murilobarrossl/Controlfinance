namespace ControlFinance.API.DTOs;

public record PayeePixDto(string? Type, string? Key);

public record PayeeBankDto(string? BankCode, string? Branch, string? Account, string? AccountType);

public record PayeePixView(string Type, string KeyMasked);

public record PayeeBankView(string BankCode, string Branch, string AccountMasked, string AccountType);

/// <summary>HolderDocumentMasked: titular declarado (TED e Pix sem chave de documento). HolderMissing:
/// método que deveria ter titular e ainda não tem (cadastrado antes do campo existir).</summary>
public record PayeePaymentMethodView(Guid Id, string Kind, PayeePixView? Pix, PayeeBankView? Bank,
    string? HolderDocumentMasked = null, bool HolderMissing = false);

/// <summary>Kind "Pix" usa Pix; "Bank" usa Bank (conta para TED). O outro é ignorado.
/// HolderDocument: CPF ou CNPJ de quem recebe, obrigatório para "Bank" e para Pix e-mail, celular ou
/// aleatória; ignorado para chave Pix CPF/CNPJ (o documento é a própria chave).</summary>
public record PayeePaymentMethodDto(string? Kind, PayeePixDto? Pix, PayeeBankDto? Bank, string? HolderDocument = null);

/// <summary>Estado do mês de referência (yyyy-MM). Source: "Bank" (identificado pelo banco),
/// "ManualPaid" ou "ManualUnpaid" (decisão da empresa, prevalece no mês), ou null.
/// BankIdentified mostra a identificação bancária mesmo quando a decisão manual a contraria.
/// BankPaidAmount: soma das saídas identificadas pelo banco no mês; null quando não há nenhuma. É o
/// único valor pago: a marcação manual é só status.</summary>
public record PayeePaymentMonthDto(string ReferenceMonth, bool Paid, string? Source, bool BankIdentified,
    decimal? BankPaidAmount, IReadOnlyList<PayeeBankPaymentDto> BankPayments);

/// <summary>AccountNumber já vem mascarado (só os 4 últimos dígitos).</summary>
public record PayeeBankPaymentDto(DateOnly Date, decimal Amount, string? AccountName, string? AccountNumber);

public record PayeePaymentMarkDto(string? ReferenceMonth, bool Paid);
