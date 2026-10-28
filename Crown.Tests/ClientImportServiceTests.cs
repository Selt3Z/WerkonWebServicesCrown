using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using WerkonWebServicesRatchet.Contracts.Clients;
using WerkonWebServicesRatchet.Contracts.Common;
using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Infrastructure.Import;

namespace WerkonWebServicesRatchet.Tests;

public sealed class ClientImportServiceTests
{
    [Fact]
    public void Preview_ReturnsHeadersAndRows()
    {
        var service = CreateService(out var dbContext);
        using var _ = dbContext;

        var preview = service.Preview(BuildWorkbook(
            ["Name", "Phone", "Notes"],
            ["Ivan", "+79161234567", "vip"],
            ["Petr", "+79161234568", ""]), "clients.xlsx");

        Assert.Equal(["Name", "Phone", "Notes"], preview.Headers);
        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(2, preview.SampleRows.Count);
    }

    [Fact]
    public void Preview_XlsExtension_Throws()
    {
        var service = CreateService(out var dbContext);
        using var _ = dbContext;

        var exception = Assert.Throws<ClientImportException>(() =>
            service.Preview(BuildWorkbook(["A"], ["B"]), "clients.xls"));

        Assert.Equal(ApiErrorCodes.ClientImportXlsNotSupported, exception.Code);
    }

    [Fact]
    public void Preview_LegacyXlsSignature_Throws()
    {
        var service = CreateService(out var dbContext);
        using var _ = dbContext;

        byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00];

        var exception = Assert.Throws<ClientImportException>(() => service.Preview(ole, "clients.xlsx"));

        Assert.Equal(ApiErrorCodes.ClientImportXlsNotSupported, exception.Code);
    }

    [Fact]
    public void Preview_TextRenamedToXlsx_Throws()
    {
        var service = CreateService(out var dbContext);
        using var _ = dbContext;

        var exception = Assert.Throws<ClientImportException>(() =>
            service.Preview("name,phone"u8.ToArray(), "clients.xlsx"));

        Assert.Equal(ApiErrorCodes.ClientImportInvalidFile, exception.Code);
    }

    [Fact]
    public void Preview_TooLarge_Throws()
    {
        var service = CreateService(out var dbContext);
        using var _ = dbContext;

        var data = new byte[ClientImportService.MaxFileBytes + 1];
        data[0] = 0x50;
        data[1] = 0x4B;

        var exception = Assert.Throws<ClientImportException>(() => service.Preview(data, "clients.xlsx"));

        Assert.Equal(ApiErrorCodes.ClientImportFileTooLarge, exception.Code);
    }

    [Fact]
    public async Task Commit_CreatesClients_SkipsDuplicatesAndReportsErrors()
    {
        var service = CreateService(out var dbContext);
        await using var _ = dbContext;

        dbContext.Clients.Add(new Client
        {
            Id = Guid.NewGuid(),
            FullName = "Existing",
            PhoneNumber = "+79161234567",
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var file = BuildWorkbook(
            ["Name", "Phone", "Notes"],
            ["Duplicate in db", "8 (916) 123-45-67", ""],
            ["New client", "+7 916 123 45 68", "note"],
            ["Duplicate in file", "89161234568", ""],
            ["No phone", "", ""],
            ["", "+79161234569", ""]);

        var result = await service.CommitAsync(
            new ClientImportCommitRequest
            {
                FileBase64 = Convert.ToBase64String(file),
                FullNameColumn = 0,
                PhoneNumberColumn = 1,
                NotesColumn = 2
            },
            culture: "ru-RU");

        // Созданы: New client + No phone (телефон необязателен). Ошибка только у пустого ФИО.
        Assert.Equal(2, result.Created);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1 + 2, await dbContext.Clients.CountAsync());
    }

    [Fact]
    public async Task Commit_SameColumnForNameAndPhone_Throws()
    {
        var service = CreateService(out var dbContext);
        await using var _ = dbContext;

        var exception = await Assert.ThrowsAsync<ClientImportException>(() =>
            service.CommitAsync(
                new ClientImportCommitRequest
                {
                    FileBase64 = Convert.ToBase64String(BuildWorkbook(["A", "B"], ["x", "y"])),
                    FullNameColumn = 0,
                    PhoneNumberColumn = 0
                },
                culture: "ru-RU"));

        Assert.Equal(ApiErrorCodes.ClientImportMappingRequired, exception.Code);
    }

    private static ClientImportService CreateService(out WerkonWebServicesRatchet.Infrastructure.Persistence.AppDbContext dbContext)
    {
        dbContext = TestHelpers.CreateDbContext();
        return new ClientImportService(dbContext);
    }

    private static byte[] BuildWorkbook(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Clients");

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (!string.IsNullOrEmpty(rows[r][c]))
                {
                    sheet.Cell(r + 1, c + 1).Value = rows[r][c];
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
