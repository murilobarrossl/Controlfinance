using System.ComponentModel.DataAnnotations;

namespace ControlFinance.API.DTOs;

public record SupplierUpsertDto(
    [Required(ErrorMessage = "Informe o nome ou razão social."), MaxLength(150, ErrorMessage = "O nome pode ter até 150 caracteres.")] string LegalName,
    string? Document,
    [MaxLength(150, ErrorMessage = "O nome fantasia pode ter até 150 caracteres.")] string? TradeName,
    [MaxLength(60, ErrorMessage = "A categoria pode ter até 60 caracteres.")] string? Category);

public record SupplierListItemDto(
    Guid Id,
    string LegalName,
    string? TradeName,
    string DocumentType,
    string DocumentMasked,
    string? Category,
    bool HasPix,
    bool HasBankAccount);

public record SupplierListDto(IReadOnlyList<SupplierListItemDto> Items, int TotalCount, int Page, int PageSize);

public record SupplierSummaryDto(int TotalCount, int WithPaymentCount, IReadOnlyList<string> Categories);

public record SupplierDetailDto(
    Guid Id,
    string LegalName,
    string? TradeName,
    string DocumentType,
    string DocumentMasked,
    string? Category,
    PayeePixView? Pix,
    PayeeBankView? Bank,
    DateTime CreatedAt,
    DateTime UpdatedAt);
