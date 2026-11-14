using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class EmergencyReserveNamesTests
{
    [Theory]
    [InlineData("Reserva", true)]
    [InlineData("Minha reserva", true)]
    [InlineData("RESERVA DE EMERGÊNCIA", true)]
    [InlineData("Reservas - casa", true)]
    [InlineData("Reservado", false)]
    [InlineData("Viagem", false)]
    [InlineData(null, false)]
    public void Caixinha_com_a_palavra_reserva_entra_sozinha(string? name, bool expected) =>
        Assert.Equal(expected, EmergencyReserveNames.IsAuto(name, reservedBalance: true));

    [Theory]
    [InlineData("Reserva de Emergência", true)]
    [InlineData("reserva   de emergencia", true)]
    [InlineData("Reserva", false)]
    [InlineData("CDB Reserva", false)]
    public void Aplicacao_so_entra_com_o_nome_exato(string name, bool expected) =>
        Assert.Equal(expected, EmergencyReserveNames.IsAuto(name, reservedBalance: false));
}
