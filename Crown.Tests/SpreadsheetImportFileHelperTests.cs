using Crown.Contracts.Common;
using Crown.Infrastructure.Import;

namespace Crown.Tests;

public sealed class SpreadsheetImportFileHelperTests
{
    [Theory]
    [InlineData(" Инв. номер ", "инв.номер")]
    [InlineData("Name", "name")]
    [InlineData(null, "")]
    public void NormalizeHeader_TrimsAndLowercases(string? raw, string expected) =>
        Assert.Equal(expected, SpreadsheetImportFileHelper.NormalizeHeader(raw));

    [Fact]
    public void FindColumn_MatchesAliases_1Based()
    {
        var headers = new[] { "A", "Инв. номер", "Name" };
        Assert.Equal(2, SpreadsheetImportFileHelper.FindColumn(headers, "инв. номер", "inventory"));
        Assert.Equal(3, SpreadsheetImportFileHelper.FindColumn(headers, "name"));
        Assert.Null(SpreadsheetImportFileHelper.FindColumn(headers, "missing"));
    }

    [Theory]
    [InlineData("1", true, true)]
    [InlineData("да", true, true)]
    [InlineData("нет", false, true)]
    [InlineData("false", false, true)]
    [InlineData("", true, true)] // пусто → default
    [InlineData("maybe", false, false)]
    public void TryParseBool_HandlesRuEn(string raw, bool expectedValue, bool expectedOk)
    {
        var ok = SpreadsheetImportFileHelper.TryParseBool(raw, defaultValue: true, out var value);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.Equal(expectedValue, value);
        }
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("3.14", 3.14)]
    public void TryParseDecimal_AcceptsCommaAndDot(string raw, decimal expected)
    {
        Assert.True(SpreadsheetImportFileHelper.TryParseDecimal(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryParseDateOnly_RuFormat()
    {
        Assert.True(SpreadsheetImportFileHelper.TryParseDateOnly("31.10.2026", out var date));
        Assert.Equal(new DateOnly(2026, 10, 31), date);
    }

    [Fact]
    public void ToExcelColumn_Null_ReturnsNull() =>
        Assert.Null(SpreadsheetImportFileHelper.ToExcelColumn(null, 5));

    [Fact]
    public void ToExcelColumn_OutOfRange_Throws()
    {
        var ex = Assert.Throws<SpreadsheetImportException>(
            () => SpreadsheetImportFileHelper.ToExcelColumn(5, 3));
        Assert.Equal(ApiErrorCodes.SpreadsheetImportMappingRequired, ex.Code);
    }

    [Fact]
    public void RequireExcelColumn_Null_Throws()
    {
        var ex = Assert.Throws<SpreadsheetImportException>(
            () => SpreadsheetImportFileHelper.RequireExcelColumn(null, 3));
        Assert.Equal(ApiErrorCodes.SpreadsheetImportMappingRequired, ex.Code);
    }
}
