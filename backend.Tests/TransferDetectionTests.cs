using ControlFinance.API.Models;
using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class TransferDetectionTests
{
    [Theory]
    [InlineData("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", PolpMovementKind.CardBillPayment)]
    [InlineData("TRANSFER_IN_ACCOUNT_TRANSFER", PolpMovementKind.OwnTransfer)]
    [InlineData("TRANSFER_OUT_ACCOUNT_TRANSFER", PolpMovementKind.OwnTransfer)]
    public void Codigos_de_pagamento_de_fatura_e_transferencia_propria_viram_o_tipo_certo(string categoryRef, PolpMovementKind expected)
    {
        Assert.Equal(expected, TransferDetection.ToMovementKind(categoryRef));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MEDICAL_PHARMACIES_AND_SUPPLEMENTS")]
    [InlineData("TRANSFER_OUT_WITHDRAWAL")]
    [InlineData("TRANSFER_IN_DEPOSIT")]
    [InlineData("TRANSFER_OUT_ACCOUNT_TRANSFER_X")]
    [InlineData("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT_X")]
    [InlineData("transfer_out_account_transfer")]
    [InlineData("loan_payments_credit_card_payment")]
    [InlineData("LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT")]
    [InlineData("FOOD_AND_DRINK_GROCERIES")]
    [InlineData("Pagamento de fatura")]
    [InlineData("Transferência mesma titularidade")]
    public void Qualquer_outro_codigo_vira_None(string? categoryRef)
    {
        Assert.Equal(PolpMovementKind.None, TransferDetection.ToMovementKind(categoryRef));
    }

    [Theory]
    [InlineData(PolpMovementKind.CardBillPayment)]
    [InlineData(PolpMovementKind.OwnTransfer)]
    public void Pagamento_de_fatura_e_transferencia_entre_contas_proprias_sao_movimentacao_propria(PolpMovementKind kind)
    {
        Assert.True(TransferDetection.IsSelfTransfer(kind));
    }

    [Fact]
    public void None_conta_normalmente()
    {
        Assert.False(TransferDetection.IsSelfTransfer(PolpMovementKind.None));
    }
}
