using System;
using TallyAuditAssistant.Engine.Tds.Reconciliation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TdsChallanReturnReconciliationServiceTests
{
    private readonly TdsChallanReturnReconciliationService _service = new();
    private static readonly DateOnly ChallanDate = new(2026, 7, 7);

    private static TdsChallanRecord Challan(decimal amount = 10000m, string serial = "12345") =>
        new("MUMB12345A", "2026-27", 1, "194C", "0001234", ChallanDate, serial, amount);

    private static TdsReturnLine Line(decimal amount = 10000m, string pan = "ABCPE1234F", string serial = "12345", string tan = "MUMB12345A") =>
        new(tan, pan, "2026-27", 1, "194C", "0001234", ChallanDate, serial, amount, "Test Deductee");

    [Fact]
    public void Reconcile_MatchesChallanAndReturnAllocations()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Line() }));
        Assert.Equal("Matched", result.Status);
        Assert.Equal(10000m, result.StatementAllocatedAmount);
    }

    [Fact]
    public void Reconcile_FlagsAmountMismatch()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan(10000m) }, new[] { Line(9500m) }));
        Assert.Equal("Amount mismatch", result.Status);
    }

    [Fact]
    public void Reconcile_FlagsMissingAndUnallocatedChallans()
    {
        Assert.Equal("Missing challan", Assert.Single(_service.Reconcile(Array.Empty<TdsChallanRecord>(), new[] { Line() })).Status);
        Assert.Equal("Unallocated challan", Assert.Single(_service.Reconcile(new[] { Challan() }, Array.Empty<TdsReturnLine>())).Status);
    }

    [Fact]
    public void Reconcile_FlagsDuplicateChallanAndInvalidPan()
    {
        Assert.Equal("Duplicate challan", Assert.Single(_service.Reconcile(new[] { Challan(), Challan() }, new[] { Line() })).Status);
        Assert.Equal("Invalid deductee PAN", Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Line(pan: "BADPAN") })).Status);
    }

    [Fact]
    public void Reconcile_UsesFullCinAndPeriodKey()
    {
        var result = _service.Reconcile(new[] { Challan() }, new[] { Line(serial: "99999") });
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Status == "Missing challan");
        Assert.Contains(result, x => x.Status == "Unallocated challan");
    }

    [Fact]
    public void Reconcile_DoesNotMatchDifferentDeductorTan()
    {
        var result = _service.Reconcile(new[] { Challan() }, new[] { Line(tan: "DELD54321B") });
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Status == "Missing challan");
        Assert.Contains(result, x => x.Status == "Unallocated challan");
    }

    [Fact]
    public void Reconcile_RejectsNullAndInvalidQuarter()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(null!, Array.Empty<TdsReturnLine>()));
        var invalid = Line() with { Quarter = 5 };
        Assert.Throws<ArgumentException>(() => _service.Reconcile(new[] { Challan() }, new[] { invalid }));
    }
    [Fact]
    public void CsvReaders_ParseNormalizedImports()
    {
        var challans = _service.ReadChallansCsv("TAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,SerialNumber,TaxAmount\nMUMB12345A,2026-27,1,194C,0001234,2026-07-07,12345,10000");
        var lines = _service.ReadReturnCsv("TAN,DeducteePAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,ChallanSerialNumber,DeductedAmount\nMUMB12345A,ABCPE1234F,2026-27,1,194C,0001234,2026-07-07,12345,10000");
        Assert.Equal("Matched", Assert.Single(_service.Reconcile(challans, lines)).Status);
    }


    [Fact]
    public void CsvReaders_PreserveQuotedCommasEscapedQuotesAndOptionalDeducteeName()
    {
        var csv = "TAN,DeducteePAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,ChallanSerialNumber,DeductedAmount,DeducteeName\r\n" +
                  "MUMB12345A,ABCPE1234F,2026-27,1,194C,0001234,2026-07-07,12345,10000,\"Acme, \"\"North\"\", Ltd.\"";
        var result = Assert.Single(_service.ReadReturnCsv(csv));
        Assert.Equal("Acme, \"North\", Ltd.", result.DeducteeName);
    }

    [Fact]
    public void CsvReaders_KeepDeducteeNameOptionalForExistingImportLayouts()
    {
        var csv = "TAN,DeducteePAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,ChallanSerialNumber,DeductedAmount\n" +
                  "MUMB12345A,ABCPE1234F,2026-27,1,194C,0001234,2026-07-07,12345,10000";
        var result = Assert.Single(_service.ReadReturnCsv(csv));
        Assert.Null(result.DeducteeName);
    }

    [Theory]
    [InlineData("TAN,Value\nA,\"unterminated")]
    [InlineData("TAN,Value\nA,\"quoted\"tail")]
    [InlineData("TAN,Value\nA,unquoted\"quote")]
    public void CsvReaders_RejectMalformedQuoting(string csv)
    {
        Assert.Throws<ArgumentException>(() => _service.ReadReturnCsv(csv));
    }


    [Theory]
    [InlineData("TAN,TAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,SerialNumber,TaxAmount\\nMUMB12345A,MUMB12345A,2026-27,1,194C,0001234,2026-07-07,12345,10000")]
    [InlineData("TAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,SerialNumber,TaxAmount\\nMUMB12345A,2026-27,1,194C,0001234,2026-07-07,12345,10000,IGNORED")]
    [InlineData("TAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,SerialNumber,TaxAmount\\nMUMB12345A,2026-27,1,194C,0001234,2026-07-07,12345")]
    public void ReadChallansCsv_RejectsDuplicateHeadersAndMisalignedRows(string csv)
    {
        Assert.Throws<ArgumentException>(() => _service.ReadChallansCsv(csv));
    }

    [Theory]
    [InlineData("TAN,DeducteePAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,ChallanSerialNumber,DeductedAmount\\nMUMB12345A,ABCPE1234F,2026-27,1,194C,0001234,2026-07-07,12345,10000,IGNORED")]
    [InlineData("TAN,DeducteePAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,ChallanSerialNumber,DeductedAmount\\nMUMB12345A,ABCPE1234F,2026-27,1,194C,0001234,2026-07-07,12345")]
    public void ReadReturnCsv_RejectsExtraAndMissingFields(string csv)
    {
        Assert.Throws<ArgumentException>(() => _service.ReadReturnCsv(csv));
    }

    [Fact]
    public void ReadReturnCsv_RejectsDuplicateHeaders()
    {
        var csv = "TAN,DeducteePAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,ChallanSerialNumber,DeductedAmount,DeductedAmount\\n" +
                  "MUMB12345A,ABCPE1234F,2026-27,1,194C,0001234,2026-07-07,12345,10000,10000";
        Assert.Throws<ArgumentException>(() => _service.ReadReturnCsv(csv));
    }

    [Fact]
    public void ReadChallansCsv_RejectsEmptyHeaderNames()
    {
        var csv = "TAN,FinancialYear,Quarter,Section,BSRCode,ChallanDate,SerialNumber,\\n" +
                  "MUMB12345A,2026-27,1,194C,0001234,2026-07-07,12345,10000";
        Assert.Throws<ArgumentException>(() => _service.ReadChallansCsv(csv));
    }


    [Fact]
    public void Reconcile_FlagsPanWithUnsupportedEntityTypeCode()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Line(pan: "AB CDE1234F".Replace(" ", "")) }));
        Assert.Equal("Invalid deductee PAN", result.Status);
    }



    [Theory]
    [InlineData("BADTAN")]
    [InlineData("MUMB1234A")]
    [InlineData("MUMB12345")]
    public void Reconcile_RejectsStructurallyInvalidTan(string tan)
    {
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            new[] { Challan() with { Tan = tan } }, Array.Empty<TdsReturnLine>()));
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            Array.Empty<TdsChallanRecord>(), new[] { Line() with { Tan = tan } }));
    }

    [Theory]
    [InlineData("2026-26")]
    [InlineData("26-27")]
    [InlineData("2026/27")]
    [InlineData("")]
    public void Reconcile_RejectsInvalidFinancialYear(string financialYear)
    {
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            new[] { Challan() with { FinancialYear = financialYear } }, Array.Empty<TdsReturnLine>()));
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            Array.Empty<TdsChallanRecord>(), new[] { Line() with { FinancialYear = financialYear } }));
    }

    [Fact]
    public void Reconcile_RejectsMissingChallanDate()
    {
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            new[] { Challan() with { ChallanDate = default } }, Array.Empty<TdsReturnLine>()));
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            Array.Empty<TdsChallanRecord>(), new[] { Line() with { ChallanDate = default } }));
    }

}
