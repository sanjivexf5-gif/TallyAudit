namespace TallyAuditAssistant.Engine.Gst;

public sealed class Gstr1Gstr3BReconciliationService
{
    public Gstr1Gstr3BReconciliation Reconcile(
        Gstr1Workpaper gstr1, Gstr3BWorkpaper gstr3B, decimal tolerance = 1.00m)
    {
        ArgumentNullException.ThrowIfNull(gstr1);
        ArgumentNullException.ThrowIfNull(gstr3B);
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance cannot be negative.");

        var relevant = gstr3B.Liabilities.Where(x =>
            x.Section is Gstr3BLiabilitySection.OutwardTaxable or Gstr3BLiabilitySection.ZeroRated).ToArray();
        var tax = relevant.Select(x => x.Tax).Aggregate(
            new Gstr3BTaxHeadSummary(0, 0, 0, 0), (sum, item) => sum + item);
        var taxable = relevant.Sum(x => x.TaxableValue);
        var values = new[]
        {
            ("Taxable value", gstr1.TaxableValue, taxable),
            ("IGST", gstr1.TotalIgst, tax.Igst),
            ("CGST", gstr1.TotalCgst, tax.Cgst),
            ("SGST", gstr1.TotalSgst, tax.Sgst),
            ("Cess", gstr1.TotalCess, tax.Cess)
        };
        return new Gstr1Gstr3BReconciliation
        {
            Tolerance = tolerance,
            Variances = values.Select(x => new Gstr1Gstr3BVariance(
                x.Item1, x.Item2, x.Item3, decimal.Round(x.Item2 - x.Item3, 2),
                Math.Abs(x.Item2 - x.Item3) > tolerance)).ToArray()
        };
    }
}
