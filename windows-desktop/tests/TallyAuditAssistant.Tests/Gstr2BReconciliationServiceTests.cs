using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class Gstr2BReconciliationServiceTests
{
    [Fact]
    public void ParseCsv_MapsGstr2BHeadersAndQuotedFields()
    {
        const string csv = "GSTIN of supplier,Trade/Legal name,Invoice number,Invoice Date,Invoice Value,Taxable Value,Integrated Tax,Central Tax,State/UT Tax,Cess\n" +
            "27AAACB2222D1Z9,\"Bharat Plates, Ltd\",INV-001,10-05-2025,118000,100000,0,9000,9000,0\n";
        var rows = Gstr2BReconciliationService.ParseCsv(csv);
        var invoice = Assert.Single(rows);
        Assert.Equal("27AAACB2222D1Z9", invoice.SupplierGstin);
        Assert.Equal("INV-001", invoice.InvoiceNumber);
        Assert.Equal(new DateTime(2025,5,10), invoice.InvoiceDate);
        Assert.Equal(118000m, invoice.InvoiceValue);
        Assert.Equal(100000m, invoice.TaxableValue);
        Assert.Equal(9000m, invoice.Cgst);
        Assert.Equal(9000m, invoice.Sgst);
    }

    [Fact]
    public void ParseCsv_RejectsMissingRequiredColumns()
    {
        var ex=Assert.Throws<InvalidDataException>(()=>Gstr2BReconciliationService.ParseCsv("GSTIN,Invoice Date\n27AAACB2222D1Z9,10-05-2025"));
        Assert.Contains("Required columns",ex.Message);
    }

    [Fact]
    public void ParseCsv_RejectsInvalidSupplierGstin()
    {
        const string csv="GSTIN of supplier,Invoice number,Invoice Value\nBAD,INV-1,1000";
        var ex=Assert.Throws<InvalidDataException>(()=>Gstr2BReconciliationService.ParseCsv(csv));
        Assert.Contains("Invalid supplier GSTIN",ex.Message);
    }

    [Fact]
    public async Task ReconcileCsv_ClassifiesMatchedValueGstinAndUnmatchedInvoices()
    {
        var dbPath=Path.Combine(Path.GetTempPath(),$"gstr2b_{Guid.NewGuid():N}.db");
        var csvPath=Path.Combine(Path.GetTempPath(),$"gstr2b_{Guid.NewGuid():N}.csv");
        try
        {
            var factory=new SqliteConnectionFactory(dbPath);
            var initializer=new DatabaseInitializer(factory,NullLogger<DatabaseInitializer>.Instance,dbPath);
            await initializer.InitializeAsync();
            using(var db=await factory.CreateConnectionAsync())
            {
                await db.ExecuteAsync(@"INSERT INTO Companies(Id,TallyCompanyName,BooksFromDate,IsActive)
                    VALUES('C1','Test Company','2025-04-01',1);
                    INSERT INTO Ledgers(Id,CompanyId,Name,ParentGroup,GSTIN)
                    VALUES('L1','C1','Supplier A','Sundry Creditors','27AAACB2222D1Z9'),
                          ('L2','C1','Supplier B','Sundry Creditors','29BBBCD3333E1Z4');
                    INSERT INTO Vouchers(Id,CompanyId,VoucherTypeId,VoucherTypeName,VoucherNumber,ReferenceNumber,VoucherDate,TotalAmount,PartyLedgerName,AlterId)
                    VALUES('V1','C1','Purchase','Purchase','P-1','INV-1','2025-05-10',118000,'Supplier A',1),
                          ('V2','C1','Purchase','Purchase','P-2','INV-2','2025-05-11',59000,'Supplier A',2),
                          ('V3','C1','Purchase','Purchase','P-3','INV-3','2025-05-12',23600,'Supplier B',3);");
            }
            await File.WriteAllTextAsync(csvPath,
                "GSTIN of supplier,Invoice number,Invoice Date,Invoice Value,Taxable Value,Integrated Tax,Central Tax,State/UT Tax,Cess\n"+
                "27AAACB2222D1Z9,INV-1,10-05-2025,118000,100000,0,9000,9000,0\n"+
                "27AAACB2222D1Z9,INV-2,11-05-2025,60000,50847.46,0,4576.27,4576.27,0\n"+
                "29BBBCD3333E1Z4,INV-3,12-05-2025,23600,20000,3600,0,0,0\n"+
                "27AAACB2222D1Z9,INV-4,13-05-2025,11800,10000,0,900,900,0\n");
            var report=await new Gstr2BReconciliationService(factory).ReconcileCsvAsync("C1",
                new DateTime(2025,4,1),new DateTime(2026,3,31),csvPath);
            Assert.Equal(4,report.ImportedRows);
            Assert.Single(report.Items.Where(x=>x.Status==Gstr2BMatchStatus.Matched));
            Assert.Single(report.Items.Where(x=>x.Status==Gstr2BMatchStatus.ValueMismatch));
            Assert.Single(report.Items.Where(x=>x.Status==Gstr2BMatchStatus.GstinMismatch));
            Assert.Single(report.Items.Where(x=>x.Status==Gstr2BMatchStatus.Gstr2BOnly));
            Assert.Equal(3,report.ExceptionCount);
        }
        finally
        {
            if(File.Exists(csvPath))File.Delete(csvPath);
            if(File.Exists(dbPath))File.Delete(dbPath);
        }
    }
}
