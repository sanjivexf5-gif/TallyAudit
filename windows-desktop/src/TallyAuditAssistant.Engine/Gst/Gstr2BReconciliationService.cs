using System.Globalization;
using System.Text;
using Dapper;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst;

public enum Gstr2BMatchStatus { Matched, ValueMismatch, GstinMismatch, BooksOnly, Gstr2BOnly }

public sealed record Gstr2BInvoice(string SupplierGstin, string InvoiceNumber, DateTime? InvoiceDate,
    decimal InvoiceValue, decimal TaxableValue, decimal Igst, decimal Cgst, decimal Sgst, decimal Cess);

public sealed record Gstr2BReconciliationItem(Gstr2BMatchStatus Status, string InvoiceNumber,
    string? SupplierGstin, string? TallyVoucherNumber, DateTime? InvoiceDate,
    decimal? BooksValue, decimal? PortalValue, decimal Difference, string Explanation);

public sealed class Gstr2BReconciliationReport
{
    public string CompanyId { get; init; } = "";
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public int ImportedRows { get; init; }
    public IReadOnlyList<Gstr2BReconciliationItem> Items { get; init; } = Array.Empty<Gstr2BReconciliationItem>();
    public int MatchedCount => Items.Count(x => x.Status == Gstr2BMatchStatus.Matched);
    public int ExceptionCount => Items.Count(x => x.Status != Gstr2BMatchStatus.Matched);
}

/// <summary>CSV-based GSTR-2B B2B invoice screening against Tally purchase vouchers. Not an ITC eligibility determination.</summary>
public sealed class Gstr2BReconciliationService
{
    private readonly SqliteConnectionFactory _factory;
    public Gstr2BReconciliationService(SqliteConnectionFactory factory) => _factory = factory;

    public async Task<Gstr2BReconciliationReport> ReconcileCsvAsync(string companyId, DateTime fromDate,
        DateTime toDate, string csvPath, decimal tolerance = 1m, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyId)) throw new ArgumentException("Company is required.", nameof(companyId));
        if (fromDate.Date > toDate.Date) throw new ArgumentException("From date must not exceed To date.");
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!File.Exists(csvPath)) throw new FileNotFoundException("GSTR-2B CSV file not found.", csvPath);

        var portal = ParseCsv(await File.ReadAllTextAsync(csvPath, Encoding.UTF8, cancellationToken));
        portal = portal.Where(x => x.InvoiceDate == null ||
            (x.InvoiceDate.Value.Date >= fromDate.Date && x.InvoiceDate.Value.Date <= toDate.Date)).ToList();

        using var db = await _factory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT COALESCE(NULLIF(TRIM(v.ReferenceNumber),''), NULLIF(TRIM(v.VoucherNumber),'')) AS InvoiceNumber,
                   v.VoucherNumber, v.VoucherDate, v.TotalAmount AS InvoiceValue, l.GSTIN AS SupplierGstin
            FROM Vouchers v
            LEFT JOIN Ledgers l ON l.CompanyId=v.CompanyId AND l.Name=v.PartyLedgerName
            WHERE v.CompanyId=@CompanyId AND v.VoucherTypeName LIKE '%Purchase%'
              AND COALESCE(v.IsCancelled,0)=0
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate)
            ORDER BY v.VoucherDate, v.VoucherNumber;";
        var rows = await db.QueryAsync(new CommandDefinition(sql, new {
            CompanyId=companyId, FromDate=fromDate.ToString("yyyy-MM-dd"), ToDate=toDate.ToString("yyyy-MM-dd")
        }, cancellationToken:cancellationToken));
        var books = rows.Select(x => new Book(NormalizeInvoice(x.InvoiceNumber?.ToString()),
            NormalizeGstin(x.SupplierGstin?.ToString()), x.VoucherNumber?.ToString(),
            ParseDate(x.VoucherDate?.ToString()), Convert.ToDecimal(x.InvoiceValue, CultureInfo.InvariantCulture)))
            .Where(x => x.Invoice.Length>0).ToList();

        var remainingBooks = new HashSet<int>(Enumerable.Range(0,books.Count));
        var remainingPortal = new HashSet<int>(Enumerable.Range(0,portal.Count));
        var portalByInvoice = remainingPortal.GroupBy(i=>NormalizeInvoice(portal[i].InvoiceNumber))
            .ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
        var output = new List<Gstr2BReconciliationItem>();

        foreach (var bi in remainingBooks.ToArray())
        {
            var book=books[bi];
            if (!portalByInvoice.TryGetValue(book.Invoice,out var candidates)) continue;
            var pi=candidates.FirstOrDefault(i=>remainingPortal.Contains(i),-1);
            if (pi<0) continue;
            var p=portal[pi]; remainingBooks.Remove(bi); remainingPortal.Remove(pi);
            var bg=book.Gstin; var pg=NormalizeGstin(p.SupplierGstin); var diff=Math.Abs(book.Value-p.InvoiceValue);
            if (bg.Length>0 && pg.Length>0 && !bg.Equals(pg,StringComparison.OrdinalIgnoreCase))
                output.Add(new(Gstr2BMatchStatus.GstinMismatch,p.InvoiceNumber,p.SupplierGstin,book.Voucher,
                    p.InvoiceDate,book.Value,p.InvoiceValue,diff,"Invoice number matches but supplier GSTIN differs. Verify supplier identity and source invoice."));
            else
                output.Add(new(diff>tolerance?Gstr2BMatchStatus.ValueMismatch:Gstr2BMatchStatus.Matched,
                    p.InvoiceNumber,p.SupplierGstin,book.Voucher,p.InvoiceDate,book.Value,p.InvoiceValue,diff,
                    diff>tolerance?"Invoice value differs. Compare taxable value, tax components, rounding and amendments."
                    :"Invoice number, supplier GSTIN and invoice value agree within tolerance."));
        }
        foreach(var i in remainingBooks){var b=books[i];output.Add(new(Gstr2BMatchStatus.BooksOnly,b.Invoice,b.Gstin,b.Voucher,b.Date,b.Value,null,b.Value,
            "Purchase voucher exists in Tally but no matching invoice number was found in imported GSTR-2B rows."));}
        foreach(var i in remainingPortal){var p=portal[i];output.Add(new(Gstr2BMatchStatus.Gstr2BOnly,p.InvoiceNumber,p.SupplierGstin,null,p.InvoiceDate,null,p.InvoiceValue,p.InvoiceValue,
            "Invoice appears in GSTR-2B but no matching purchase voucher was found in this company and period."));}

        return new(){CompanyId=companyId,FromDate=fromDate.Date,ToDate=toDate.Date,ImportedRows=portal.Count,
            Items=output.OrderBy(x=>x.Status).ThenBy(x=>x.InvoiceDate).ThenBy(x=>x.InvoiceNumber).ToArray()};
    }

    public static IReadOnlyList<Gstr2BInvoice> ParseCsv(string csv)
    {
        if(string.IsNullOrWhiteSpace(csv)) throw new InvalidDataException("CSV file is empty.");
        var rows=ReadRows(csv);
        if(rows.Count<2) throw new InvalidDataException("CSV must contain a header and at least one invoice row.");
        var headers=rows[0].Select((v,i)=>(Key:Header(v),Index:i)).Where(x=>x.Key.Length>0)
            .GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().Index,StringComparer.OrdinalIgnoreCase);
        int Col(params string[] names){foreach(var n in names)if(headers.TryGetValue(Header(n),out var i))return i;return -1;}
        var gst=Col("GSTIN of supplier","Supplier GSTIN","GSTIN/UIN of Supplier","GSTIN");
        var inv=Col("Invoice number","Invoice No","Document Number","Note Number");
        var date=Col("Invoice Date","Document Date","Note Date"); var value=Col("Invoice Value","Document Value","Note Value");
        var taxable=Col("Taxable Value","Taxable Amount");var igst=Col("Integrated Tax","IGST");
        var cgst=Col("Central Tax","CGST");var sgst=Col("State/UT Tax","State Tax","SGST");var cess=Col("Cess");
        if(gst<0||inv<0||value<0)throw new InvalidDataException("Required columns missing: supplier GSTIN, invoice number and invoice value.");
        string Cell(List<string> r,int i)=>i>=0&&i<r.Count?r[i].Trim():"";
        var result=new List<Gstr2BInvoice>();
        for(var n=1;n<rows.Count;n++){
            var r=rows[n];if(r.All(string.IsNullOrWhiteSpace))continue;
            var g=NormalizeGstin(Cell(r,gst));var number=Cell(r,inv);
            if(g.Length==0&&number.Length==0)continue;
            if(g.Length!=15)throw new InvalidDataException($"Invalid supplier GSTIN on CSV row {n+1}.");
            if(number.Length==0)throw new InvalidDataException($"Missing invoice number on CSV row {n+1}.");
            result.Add(new(g,number,ParseDate(Cell(r,date)),Amount(Cell(r,value),n+1,"invoice value"),
                Amount(Cell(r,taxable),n+1,"taxable value"),Amount(Cell(r,igst),n+1,"IGST"),
                Amount(Cell(r,cgst),n+1,"CGST"),Amount(Cell(r,sgst),n+1,"SGST"),Amount(Cell(r,cess),n+1,"cess")));
        }
        if(result.Count==0)throw new InvalidDataException("No valid B2B invoice rows found.");
        return result;
    }

    private static List<List<string>> ReadRows(string csv){
        var rows=new List<List<string>>();var row=new List<string>();var field=new StringBuilder();bool quote=false;csv=csv.TrimStart('\uFEFF');
        for(int i=0;i<csv.Length;i++){char c=csv[i];if(c=='"'){if(quote&&i+1<csv.Length&&csv[i+1]=='"'){field.Append('"');i++;}else quote=!quote;}
            else if(c==','&&!quote){row.Add(field.ToString());field.Clear();}
            else if((c=='\r'||c=='\n')&&!quote){if(c=='\r'&&i+1<csv.Length&&csv[i+1]=='\n')i++;row.Add(field.ToString());field.Clear();if(row.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(row);row=new();}
            else field.Append(c);}
        if(quote)throw new InvalidDataException("CSV has an unterminated quoted field.");
        row.Add(field.ToString());if(row.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(row);return rows;
    }
    private static string Header(string s)=>new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string NormalizeGstin(string? s)=>(s??"").Trim().Replace(" ","").ToUpperInvariant();
    private static string NormalizeInvoice(string? s)=>new((s??"").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static DateTime? ParseDate(string? s){if(string.IsNullOrWhiteSpace(s))return null;
        string[] f={"dd-MM-yyyy","dd/MM/yyyy","yyyy-MM-dd","d-M-yyyy","d/M/yyyy"};
        if(DateTime.TryParseExact(s.Trim(),f,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d))return d.Date;
        return DateTime.TryParse(s,CultureInfo.GetCultureInfo("en-IN"),DateTimeStyles.None,out d)?d.Date:null;}
    private static decimal Amount(string s,int row,string field){if(string.IsNullOrWhiteSpace(s)||s=="-")return 0m;
        var v=s.Replace(",","").Replace("₹","").Trim();
        if(decimal.TryParse(v,NumberStyles.Number|NumberStyles.AllowParentheses,CultureInfo.InvariantCulture,out var d))return d;
        throw new InvalidDataException($"Invalid {field} on CSV row {row}: '{s}'.");}
    private sealed record Book(string Invoice,string Gstin,string? Voucher,DateTime? Date,decimal Value);
}
