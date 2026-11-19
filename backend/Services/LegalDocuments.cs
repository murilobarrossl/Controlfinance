using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Versões vigentes dos textos que o usuário aceita. O frontend manda a versão que exibiu e o
// cadastro/conexão recusa versão diferente: assim o registro de aceite (ConsentRecord) sempre
// aponta pro texto que a pessoa realmente viu. Mudou o texto, muda a versão aqui E em
// frontend/src/legal/versions.js.
public static class LegalDocuments
{
    public const string TermsVersion = "2026-11-20";
    public const string PrivacyVersion = "2026-11-20";
    public const string BankConnectionNoticeVersion = "2026-11-20";

    // Mudança que crie tratamento baseado em consentimento (nova finalidade sem outra base legal) não
    // pode ser resolvida pelo aviso "Entendi": marque true junto com a nova versão e colete um
    // ConsentKinds.SpecificConsent por finalidade num fluxo próprio. Com true, a ciência é recusada.
    public static readonly bool PrivacyVersionRequiresConsent = false;

    // Cadastro (privacy) ou aviso (privacy_notice) da versão vigente: o aviso só volta com outra versão.
    public static Task<bool> HasCurrentPrivacyRecordAsync(Data.AppDbContext db, Guid userId, string version, CancellationToken ct) =>
        db.ConsentRecords.AnyAsync(c => c.UserId == userId && c.Version == version
            && (c.Kind == Models.ConsentKinds.Privacy || c.Kind == Models.ConsentKinds.PrivacyNotice), ct);
}
