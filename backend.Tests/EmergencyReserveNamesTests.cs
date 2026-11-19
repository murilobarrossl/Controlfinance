using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class EmergencyReserveNamesTests
{
    [Theory]
    [InlineData("Reserva", true)]
    [InlineData("Minha reserva", true)]
    [InlineData("RESERVA DE EMERGÊNCIA", true)]
    [InlineData("Reservas - casa", true)]
    [InlineData("Emergência", true)]
    [InlineData("Colchão financeiro", true)]
    [InlineData("Reservado", false)]
    [InlineData("Viagem", false)]
    [InlineData(null, false)]
    public void Caixinha_entra_pelo_nome(string? name, bool expected) =>
        Assert.Equal(expected, EmergencyReserveNames.IsAuto(name, reservedBalance: true));

    [Theory]
    [InlineData("Reserva de Emergência")]
    [InlineData("CDB Reserva")]
    [InlineData("Fundo Emergência")]
    public void Aplicacao_nunca_entra_sozinha(string name) =>
        Assert.False(EmergencyReserveNames.IsAuto(name, reservedBalance: false));
}
