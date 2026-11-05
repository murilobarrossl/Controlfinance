namespace ControlFinance.API.Services;

// Versões vigentes dos textos que o usuário aceita. O frontend manda a versão que exibiu e o
// cadastro/conexão recusa versão diferente: assim o registro de aceite (ConsentRecord) sempre
// aponta pro texto que a pessoa realmente viu. Mudou o texto, muda a versão aqui E em
// frontend/src/legal/versions.js.
public static class LegalDocuments
{
    public const string TermsVersion = "2026-11-05";
    public const string PrivacyVersion = "2026-10-31";
    public const string BankConnectionNoticeVersion = "2026-11-04";
}
