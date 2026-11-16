using System.Globalization;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
[BusinessHolderOnly]
public class SuppliersController(AppDbContext db, IEncryptionService encryption) : ApiControllerBase
{
    internal const int MaxSuppliersPerCompany = 5000;
    private static readonly CompareInfo PtBr = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var rows = (await db.Suppliers.AsNoTracking().Where(s => s.UserId == UserId)
            .Select(s => new
            {
                s.Id, s.LegalName, s.TradeName, s.DocumentType, s.Document, s.Category,
                HasPix = s.PixKeyHash != null, HasBank = s.PayeeBankCode != null
            })
            .ToListAsync(ct))
            .Select(s => new SupplierListItemDto(s.Id, s.LegalName, s.TradeName, s.DocumentType, PayeeKeys.MaskDocument(s.Document),
                s.Category, s.HasPix, s.HasBank));

        if (!string.IsNullOrWhiteSpace(search))
            rows = rows.Where(s => TextSearch.Matches(s.LegalName, search) || (s.TradeName is not null && TextSearch.Matches(s.TradeName, search)));

        var ordered = rows
            .OrderBy(s => s.LegalName, Comparer<string>.Create((a, b) => PtBr.Compare(a, b, CompareOptions.IgnoreCase)))
            .ThenBy(s => s.Id)
            .ToList();

        return Ok(new SupplierListDto(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count, page, pageSize));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var own = db.Suppliers.AsNoTracking().Where(s => s.UserId == UserId);
        var total = await own.CountAsync(ct);
        var withPayment = await own.CountAsync(s => s.PixKeyHash != null || s.PayeeBankCode != null, ct);
        var categories = await own.Where(s => s.Category != null).Select(s => s.Category!).Distinct().ToListAsync(ct);
        categories.Sort((a, b) => PtBr.Compare(a, b, CompareOptions.IgnoreCase));
        return Ok(new SupplierSummaryDto(total, withPayment, categories));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == UserId, ct);
        return supplier is null ? NotFound() : Ok(ToDetail(supplier));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SupplierUpsertDto dto, CancellationToken ct)
    {
        if (await db.Suppliers.CountAsync(s => s.UserId == UserId, ct) >= MaxSuppliersPerCompany)
            return BadRequest(new { message = $"Limite de {MaxSuppliersPerCompany} fornecedores por empresa atingido." });

        var supplier = new Supplier { UserId = UserId };
        var error = await ApplyAsync(supplier, dto, isNew: true, ct);
        if (error is not null) return BadRequest(new { message = error });

        db.Suppliers.Add(supplier);
        var conflict = await SaveAsync(ct);
        if (conflict is not null) return Conflict(new { message = conflict });
        return CreatedAtAction(nameof(Get), new { id = supplier.Id }, ToDetail(supplier));
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] SupplierUpsertDto dto, CancellationToken ct) =>
        ChangeAsync(id, supplier => ApplyAsync(supplier, dto, isNew: false, ct), ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.UserId == UserId, ct);
        if (supplier is null) return NotFound();

        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/pix")]
    public Task<IActionResult> SetPix(Guid id, [FromBody] PayeePixDto dto, CancellationToken ct) =>
        ChangeAsync(id, async supplier =>
        {
            var error = PayeePayment.ParsePix(dto, out var type, out var key);
            if (error is not null) return error;
            var candidates = encryption.ComputeLookupHashCandidates(SupplierKeys.PixLookupInput(UserId, type, key));
            if (await db.Suppliers.AnyAsync(s => s.UserId == UserId && s.Id != supplier.Id && s.PixKeyHash != null && candidates.Contains(s.PixKeyHash), ct))
                return "Esta chave Pix já está cadastrada para outro fornecedor.";

            supplier.PixKeyType = type;
            supplier.PixKey = key;
            supplier.PixKeyHash = encryption.ComputeLookupHash(SupplierKeys.PixLookupInput(UserId, type, key));
            return null;
        }, ct);

    [HttpDelete("{id:guid}/pix")]
    public Task<IActionResult> RemovePix(Guid id, CancellationToken ct) =>
        ChangeAsync(id, supplier =>
        {
            supplier.PixKeyType = null;
            supplier.PixKey = null;
            supplier.PixKeyHash = null;
            return Task.FromResult<string?>(null);
        }, ct);

    [HttpPut("{id:guid}/bank-account")]
    public Task<IActionResult> SetBankAccount(Guid id, [FromBody] PayeeBankDto dto, CancellationToken ct) =>
        ChangeAsync(id, supplier =>
        {
            var error = PayeePayment.ParseBank(dto, out var bank);
            if (error is not null) return Task.FromResult<string?>(error);
            supplier.PayeeBankCode = bank!.BankCode;
            supplier.PayeeBranch = bank.Branch;
            supplier.PayeeAccount = bank.Account;
            supplier.PayeeAccountType = bank.AccountType;
            return Task.FromResult<string?>(null);
        }, ct);

    [HttpDelete("{id:guid}/bank-account")]
    public Task<IActionResult> RemoveBankAccount(Guid id, CancellationToken ct) =>
        ChangeAsync(id, supplier =>
        {
            supplier.PayeeBankCode = null;
            supplier.PayeeBranch = null;
            supplier.PayeeAccount = null;
            supplier.PayeeAccountType = null;
            return Task.FromResult<string?>(null);
        }, ct);

    private async Task<IActionResult> ChangeAsync(Guid id, Func<Supplier, Task<string?>> change, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.UserId == UserId, ct);
        if (supplier is null) return NotFound();

        var error = await change(supplier);
        if (error is not null) return BadRequest(new { message = error });

        supplier.UpdatedAt = DateTime.UtcNow;
        var conflict = await SaveAsync(ct);
        return conflict is not null ? Conflict(new { message = conflict }) : Ok(ToDetail(supplier));
    }

    private async Task<string?> ApplyAsync(Supplier supplier, SupplierUpsertDto dto, bool isNew, CancellationToken ct)
    {
        var legalName = string.Join(' ', (dto.LegalName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (legalName.Length < 2) return "Informe o nome ou razão social.";
        if (legalName.Length > 150) return "O nome pode ter até 150 caracteres.";

        var tradeName = string.IsNullOrWhiteSpace(dto.TradeName) ? null : dto.TradeName.Trim();
        if (tradeName is { Length: > 150 }) return "O nome fantasia pode ter até 150 caracteres.";

        var category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        if (category is { Length: > 60 }) return "A categoria pode ter até 60 caracteres.";

        var document = PayeeKeys.Digits(dto.Document);
        string? documentHash = null;
        if (isNew || document.Length > 0)
        {
            if (!DocumentValidator.IsValid(document)) return "CPF ou CNPJ inválido.";
            var candidates = encryption.ComputeLookupHashCandidates(SupplierKeys.DocumentLookupInput(UserId, document));
            if (await db.Suppliers.AnyAsync(s => s.UserId == UserId && s.Id != supplier.Id && candidates.Contains(s.DocumentHash), ct))
                return "Já existe um fornecedor com este CPF ou CNPJ.";
            documentHash = encryption.ComputeLookupHash(SupplierKeys.DocumentLookupInput(UserId, document));
        }

        supplier.LegalName = legalName;
        supplier.TradeName = tradeName;
        supplier.Category = category;
        if (documentHash is not null)
        {
            supplier.Document = document;
            supplier.DocumentType = DocumentTypes.FromDigits(document);
            supplier.DocumentHash = documentHash;
        }
        return null;
    }

    private async Task<string?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return "Já existe um fornecedor com este CPF/CNPJ ou com esta chave Pix.";
        }
    }

    private static SupplierDetailDto ToDetail(Supplier s) => new(
        s.Id, s.LegalName, s.TradeName, s.DocumentType, PayeeKeys.MaskDocument(s.Document), s.Category,
        PayeePayment.PixView(s.PixKeyType, s.PixKey),
        PayeePayment.BankView(s.PayeeBankCode, s.PayeeBranch, s.PayeeAccount, s.PayeeAccountType),
        s.CreatedAt, s.UpdatedAt);
}
