using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class TransferDetectionTests
{
    [Fact]
    public void Nome_do_titular_abreviado_no_banco_e_reconhecido()
    {
        Assert.True(TransferDetection.IsSelfTransfer("Transferência enviada|IGOR L ACCIOLY LINS LIMA", null, "Igor Luis Accioly Lins Lima"));
    }

    [Fact]
    public void Contraparte_com_outro_sobrenome_nao_e_transferencia_propria()
    {
        Assert.False(TransferDetection.IsSelfTransfer("Transferência enviada|Igor Souza", null, "Igor Lima"));
    }

    [Fact]
    public void Categoria_de_mesma_titularidade_basta_sem_nome_do_dono()
    {
        Assert.True(TransferDetection.IsSelfTransfer("Pix enviado", "Transferência mesma titularidade", null));
    }

    [Fact]
    public void Sem_nome_do_dono_e_sem_categoria_propria_nao_exclui_nada()
    {
        Assert.False(TransferDetection.IsSelfTransfer("Transferência enviada|Igor Lima", null, null));
    }

    [Fact]
    public void Compra_comum_nao_e_transferencia()
    {
        Assert.False(TransferDetection.IsSelfTransfer("Mercado Igor Lima", "Alimentação", "Igor Lima"));
    }

    [Fact]
    public void Nome_vazio_nunca_e_transferencia()
    {
        Assert.False(TransferDetection.IsSelfTransfer("  ", "Transferência mesma titularidade", "Igor Lima"));
    }

    [Fact]
    public void Acentos_e_caixa_diferentes_ainda_casam()
    {
        Assert.True(TransferDetection.IsSelfTransfer("TED enviada|JOSÉ DA SILVA", null, "Jose da Silva"));
    }
}
