using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ControlFinance.API.Data;

public static class InvestmentModelConfiguration
{
    public static void Configure(ModelBuilder model, ValueConverter<string, string> text, ValueConverter<string?, string?> optionalText)
    {
        model.Entity<InvestmentSyncSchedule>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.IntegrationId, x.SourceConsentId, x.Resource, x.ExternalId }).IsUnique().HasDatabaseName("UX_InvSchedule_Resource");
            e.HasOne<PolpIntegration>().WithMany().HasForeignKey(x => new { x.UserId, x.IntegrationId })
                .HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_InvSchedule_Integration");
            e.Property(x => x.Resource).HasMaxLength(64);
            e.Property(x => x.ExternalId).HasMaxLength(160);
        });
        model.Entity<ImportedInvestment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.IntegrationId, x.Id });
            e.HasIndex(x => new { x.UserId, x.FirstObservedAt, x.Id });
            e.HasIndex(x => new { x.Family, x.ExternalId }).IsUnique();
            e.HasOne<PolpIntegration>().WithMany().HasForeignKey(x => new { x.UserId, x.IntegrationId })
                .HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Inv_Integration");
            e.Property(x => x.Family).HasMaxLength(32);
            e.Property(x => x.ExternalId).HasMaxLength(160);
            e.Property(x => x.ProductType).HasMaxLength(80);
            e.Property(x => x.Name).HasConversion(optionalText).HasColumnName("NameEncrypted");
            e.Property(x => x.IdentityJson).HasConversion(text).HasColumnName("IdentityEncrypted");
            e.Property(x => x.IdentityAvailabilityJson).HasConversion(text).HasColumnName("IdentityAvailabilityEncrypted");
        });
        model.Entity<InvestmentContract>(e =>
        {
            e.HasKey(x => x.InvestmentId);
            e.HasOne<ImportedInvestment>().WithOne().HasForeignKey<InvestmentContract>(x => x.InvestmentId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_InvContract_Investment");
            e.Property(x => x.ValuesJson).HasConversion(text).HasColumnName("ValuesEncrypted");
            e.Property(x => x.AvailabilityJson).HasConversion(text).HasColumnName("AvailabilityEncrypted");
            e.Property(x => x.ProvenanceJson).HasConversion(text).HasColumnName("ProvenanceEncrypted");
        });
        model.Entity<InvestmentPosition>(e =>
        {
            e.HasKey(x => x.InvestmentId);
            e.HasOne<ImportedInvestment>().WithOne().HasForeignKey<InvestmentPosition>(x => x.InvestmentId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_InvPosition_Investment");
            e.Property(x => x.ValuesJson).HasConversion(text).HasColumnName("ValuesEncrypted");
            e.Property(x => x.AvailabilityJson).HasConversion(text).HasColumnName("AvailabilityEncrypted");
            e.Property(x => x.ProvenanceJson).HasConversion(text).HasColumnName("ProvenanceEncrypted");
        });
        model.Entity<InvestmentPositionObservation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<ImportedInvestment>().WithMany().HasForeignKey(x => x.InvestmentId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_InvObservation_Investment");
            e.HasIndex(x => new { x.InvestmentId, x.ContentHash }).IsUnique();
            e.HasIndex(x => new { x.InvestmentId, x.ObservedAt, x.Id });
            e.HasIndex(x => new { x.InvestmentId, x.ReferenceAt, x.ObservedAt, x.Id }).HasDatabaseName("IX_InvObservation_Reference");
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.PayloadJson).HasConversion(text).HasColumnName("PayloadEncrypted");
        });
        model.Entity<InvestmentMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<ImportedInvestment>().WithMany().HasForeignKey(x => x.InvestmentId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_InvMovement_Investment");
            e.HasIndex(x => new { x.InvestmentId, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.InvestmentId, x.TransactionDate, x.Id });
            e.HasIndex(x => new { x.InvestmentId, x.FirstObservedAt, x.Id });
            e.Property(x => x.ExternalId).HasMaxLength(160);
            e.Property(x => x.Direction).HasMaxLength(32);
            e.Property(x => x.MovementType).HasMaxLength(80);
            e.Property(x => x.ValuesJson).HasConversion(text).HasColumnName("ValuesEncrypted");
            e.Property(x => x.AvailabilityJson).HasConversion(text).HasColumnName("AvailabilityEncrypted");
            e.Property(x => x.ProvenanceJson).HasConversion(text).HasColumnName("ProvenanceEncrypted");
        });
        model.Entity<ImportedReservedBalance>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<PolpIntegration>().WithMany().HasForeignKey(x => new { x.UserId, x.IntegrationId })
                .HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Reserved_Integration");
            e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Reserved_Account");
            e.HasIndex(x => new { x.BankAccountId, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.BankAccountId, x.ReservedIdentification }).IsUnique().HasDatabaseName("UX_Reserved_OriginalIdentity");
            e.HasIndex(x => new { x.UserId, x.IntegrationId, x.Id });
            e.HasIndex(x => new { x.UserId, x.FirstObservedAt, x.Id });
            e.Property(x => x.ExternalId).HasMaxLength(160);
            e.Property(x => x.ReservedIdentification).HasMaxLength(160);
            e.Property(x => x.Name).HasConversion(optionalText).HasColumnName("NameEncrypted");
            e.Property(x => x.ValuesJson).HasConversion(text).HasColumnName("ValuesEncrypted");
            e.Property(x => x.AvailabilityJson).HasConversion(text).HasColumnName("AvailabilityEncrypted");
            e.Property(x => x.ProvenanceJson).HasConversion(text).HasColumnName("ProvenanceEncrypted");
        });
        model.Entity<ReservedBalanceObservation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<ImportedReservedBalance>().WithMany().HasForeignKey(x => x.ReservedBalanceId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ReservedObservation_Reserved");
            e.HasIndex(x => new { x.ReservedBalanceId, x.ContentHash }).IsUnique();
            e.HasIndex(x => new { x.ReservedBalanceId, x.ObservedAt, x.Id });
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.PayloadJson).HasConversion(text).HasColumnName("PayloadEncrypted");
        });
        model.Entity<InvestmentSyncState>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<PolpIntegration>().WithMany().HasForeignKey(x => new { x.UserId, x.IntegrationId })
                .HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_InvSync_Integration");
            e.HasIndex(x => new { x.IntegrationId, x.SourceConsentId, x.Family, x.Kind, x.ParentExternalId }).IsUnique().HasDatabaseName("UX_InvSync_Stream");
            e.HasIndex(x => new { x.Status, x.LastAttemptAt });
            e.Property(x => x.Family).HasMaxLength(32);
            e.Property(x => x.Kind).HasMaxLength(24);
            e.Property(x => x.ParentExternalId).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Availability).HasMaxLength(48);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.Cursor).HasConversion(optionalText).HasColumnName("CursorEncrypted");
            e.Property(x => x.WindowQuery).HasConversion(optionalText).HasColumnName("WindowEncrypted");
            e.Property(x => x.SeenCursorsJson).HasConversion(text).HasColumnName("SeenCursorsEncrypted");
        });
    }
}
