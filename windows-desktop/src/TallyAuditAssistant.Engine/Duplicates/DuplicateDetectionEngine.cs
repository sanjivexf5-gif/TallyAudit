using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Duplicates;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Duplicates;

public class DuplicateDetectionEngine : IDuplicateDetectionEngine
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IDuplicateRepository _repository;
    private readonly ILogger<DuplicateDetectionEngine> _logger;

    public DuplicateStrategyConfiguration Configuration { get; set; } = new();
    public event EventHandler<DuplicateAuditProgress>? ProgressChanged;

    public DuplicateDetectionEngine(
        SqliteConnectionFactory connectionFactory,
        IDuplicateRepository repository,
        ILogger<DuplicateDetectionEngine> logger)
    {
        _connectionFactory = connectionFactory;
        _repository = repository;
        _logger = logger;
    }

    public async Task<DuplicateAuditSummary> DetectDuplicatesAsync(
        string companyId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        DuplicateStrategyConfiguration? overrideConfig = null,
        CancellationToken cancellationToken = default)
    {
        var config = overrideConfig ?? Configuration;
        _logger.LogInformation("Starting high-performance duplicate detection for company {CompanyId}", companyId);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Fetch eligible vouchers into lightweight snapshots (indexed query)
        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.ReferenceNumber, v.VoucherDate,
                   v.VoucherTypeName, v.PartyLedgerName, l.GSTIN as PartyGstin, l.PAN as PartyPan,
                   v.TotalAmount, v.Narration,
                   (SELECT COUNT(*) FROM VoucherEntries ve WHERE ve.VoucherId = v.Id) as LineItemCount
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId
              AND (@FromDate IS NULL OR v.VoucherDate >= @FromDate)
              AND (@ToDate IS NULL OR v.VoucherDate <= @ToDate)
            ORDER BY v.PartyLedgerName, v.VoucherDate ASC;
        ";

        var vouchers = (await connection.QueryAsync<TransactionSnapshot>(new CommandDefinition(sql, new
        {
            CompanyId = companyId,
            FromDate = fromDate?.ToString("yyyy-MM-dd"),
            ToDate = toDate?.ToString("yyyy-MM-dd")
        }, cancellationToken: cancellationToken))).ToList();

        _logger.LogInformation("Loaded {Count} vouchers for candidate blocking analysis", vouchers.Count);

        var matchPairs = new List<DuplicateMatchPair>();
        var matchedPairKeys = new HashSet<string>(); // avoid re-flagging same (Id1, Id2) pair

        int totalBatches = 3;
        int currentBatch = 0;

        // -------------------------------------------------------------
        // PASS 1: EXACT DUPLICATE STRATEGY (100% Confidence)
        // Group by Party + Date + Amount + Voucher/Ref Number
        // -------------------------------------------------------------
        if (config.EnableExactMatch)
        {
            currentBatch++;
            ProgressChanged?.Invoke(this, new DuplicateAuditProgress("Executing Pass 1: Exact Duplicate Match", currentBatch, totalBatches, matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.ExactDuplicate), 0, 0, 33.3));

            // Bucket by (Party, Date, Amount)
            var exactBuckets = vouchers
                .Where(v => !string.IsNullOrWhiteSpace(v.PartyLedgerName))
                .GroupBy(v => new { Party = v.PartyLedgerName.Trim().ToLowerInvariant(), v.VoucherDate, Amount = Math.Abs(v.TotalAmount) })
                .Where(g => g.Count() > 1);

            foreach (var group in exactBuckets)
            {
                var list = group.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var v1 = list[i];
                        var v2 = list[j];
                        string pairKey = GetPairKey(v1.VoucherId, v2.VoucherId);
                        if (matchedPairKeys.Contains(pairKey)) continue;

                        bool sameNumber = !string.IsNullOrWhiteSpace(v1.VoucherNumber) &&
                                          string.Equals(v1.VoucherNumber.Trim(), v2.VoucherNumber?.Trim(), StringComparison.OrdinalIgnoreCase);
                        bool sameRef = !string.IsNullOrWhiteSpace(v1.ReferenceNumber) &&
                                       string.Equals(v1.ReferenceNumber.Trim(), v2.ReferenceNumber?.Trim(), StringComparison.OrdinalIgnoreCase);

                        if (sameNumber || sameRef || string.Equals(v1.VoucherTypeName, v2.VoucherTypeName, StringComparison.OrdinalIgnoreCase))
                        {
                            var pair = BuildMatchPair(
                                companyId,
                                DuplicateConfidenceTier.ExactDuplicate,
                                100.0,
                                "Exact Match Strategy: Date + Party + Amount + Identifier/Type",
                                v1, v2);

                            matchPairs.Add(pair);
                            matchedPairKeys.Add(pairKey);
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------
        // PASS 2: STRONG MATCH STRATEGY (Likely Duplicate: 80 - 99%)
        // Block by Party + Exact Amount, with Time Window Proximity
        // -------------------------------------------------------------
        if (config.EnableStrongMatch)
        {
            currentBatch++;
            ProgressChanged?.Invoke(this, new DuplicateAuditProgress("Executing Pass 2: Likely Duplicate Proximity Match", currentBatch, totalBatches, matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.ExactDuplicate), matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.LikelyDuplicate), 0, 66.6));

            var partyAmountBuckets = vouchers
                .Where(v => !string.IsNullOrWhiteSpace(v.PartyLedgerName) && v.TotalAmount != 0)
                .GroupBy(v => new { Party = v.PartyLedgerName.Trim().ToLowerInvariant(), Amount = Math.Abs(v.TotalAmount) })
                .Where(g => g.Count() > 1);

            foreach (var group in partyAmountBuckets)
            {
                var sorted = group.OrderBy(v => v.VoucherDate).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    for (int j = i + 1; j < sorted.Count; j++)
                    {
                        var v1 = sorted[i];
                        var v2 = sorted[j];
                        string pairKey = GetPairKey(v1.VoucherId, v2.VoucherId);
                        if (matchedPairKeys.Contains(pairKey)) continue;

                        int daysDiff = Math.Abs((v1.VoucherDate - v2.VoucherDate).Days);
                        if (daysDiff <= config.StrongMatchDateWindowDays)
                        {
                            // Calculate score
                            double score = 95.0 - (daysDiff * 3.0);
                            if (string.Equals(v1.VoucherTypeName, v2.VoucherTypeName, StringComparison.OrdinalIgnoreCase))
                                score += 3.0;

                            var pair = BuildMatchPair(
                                companyId,
                                DuplicateConfidenceTier.LikelyDuplicate,
                                Math.Min(score, 98.0),
                                "Strong Match Strategy: Same Party + Exact Amount + Date Proximity (±" + daysDiff + " days)",
                                v1, v2);

                            matchPairs.Add(pair);
                            matchedPairKeys.Add(pairKey);
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------
        // PASS 3: POSSIBLE MATCH STRATEGY (Possible Duplicate: 50 - 79%)
        // Block by Party only, with Bounded Amount & Narration Similarity
        // -------------------------------------------------------------
        if (config.EnablePossibleMatch)
        {
            currentBatch++;
            ProgressChanged?.Invoke(this, new DuplicateAuditProgress("Executing Pass 3: Possible Duplicate Fuzzy Match", currentBatch, totalBatches, matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.ExactDuplicate), matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.LikelyDuplicate), matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.PossibleDuplicate), 100.0));

            var partyBuckets = vouchers
                .Where(v => !string.IsNullOrWhiteSpace(v.PartyLedgerName) && v.TotalAmount != 0)
                .GroupBy(v => v.PartyLedgerName.Trim().ToLowerInvariant())
                .Where(g => g.Count() > 1);

            foreach (var group in partyBuckets)
            {
                var partyList = group.OrderBy(v => v.VoucherDate).ToList();
                for (int i = 0; i < partyList.Count; i++)
                {
                    for (int j = i + 1; j < partyList.Count; j++)
                    {
                        var v1 = partyList[i];
                        var v2 = partyList[j];
                        string pairKey = GetPairKey(v1.VoucherId, v2.VoucherId);
                        if (matchedPairKeys.Contains(pairKey)) continue;

                        int daysDiff = Math.Abs((v1.VoucherDate - v2.VoucherDate).Days);
                        if (daysDiff > config.PossibleMatchDateWindowDays) continue;

                        decimal a1 = Math.Abs(v1.TotalAmount);
                        decimal a2 = Math.Abs(v2.TotalAmount);
                        decimal diffAmount = Math.Abs(a1 - a2);
                        decimal diffPercent = a1 > 0 ? (diffAmount / a1) * 100m : 100m;

                        bool amountMatches = diffAmount <= config.PossibleMatchAmountToleranceRupees ||
                                             diffPercent <= config.PossibleMatchAmountTolerancePercent;

                        double narrationSimilarity = CalculateTextSimilarity(v1.Narration, v2.Narration);

                        if (amountMatches || narrationSimilarity >= config.NarrationSimilarityThreshold)
                        {
                            double confidence = 60.0;
                            if (amountMatches) confidence += 10.0;
                            if (narrationSimilarity >= 0.5) confidence += (narrationSimilarity * 15.0);
                            if (daysDiff <= 5) confidence += 5.0;

                            var pair = BuildMatchPair(
                                companyId,
                                DuplicateConfidenceTier.PossibleDuplicate,
                                Math.Round(Math.Min(confidence, 79.0), 1),
                                "Possible Match Strategy: Same Party + Similar Amount (diff " + diffAmount.ToString("C2") + ") + Narration Similarity (" + (narrationSimilarity * 100).ToString("F0") + "%)",
                                v1, v2);

                            matchPairs.Add(pair);
                            matchedPairKeys.Add(pairKey);
                        }
                    }
                }
            }
        }

        // Save findings to repository
        await _repository.SaveMatchPairsBatchAsync(matchPairs, cancellationToken);

        var summary = new DuplicateAuditSummary
        {
            TotalVouchersScanned = vouchers.Count,
            TotalDuplicatePairsFound = matchPairs.Count,
            ExactDuplicatesCount = matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.ExactDuplicate),
            LikelyDuplicatesCount = matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.LikelyDuplicate),
            PossibleDuplicatesCount = matchPairs.Count(m => m.Tier == DuplicateConfidenceTier.PossibleDuplicate),
            SalesDuplicatesCount = matchPairs.Count(m => m.VoucherCategory.Contains("Sales", StringComparison.OrdinalIgnoreCase)),
            PurchaseDuplicatesCount = matchPairs.Count(m => m.VoucherCategory.Contains("Purchase", StringComparison.OrdinalIgnoreCase)),
            ReceiptDuplicatesCount = matchPairs.Count(m => m.VoucherCategory.Contains("Receipt", StringComparison.OrdinalIgnoreCase)),
            PaymentDuplicatesCount = matchPairs.Count(m => m.VoucherCategory.Contains("Payment", StringComparison.OrdinalIgnoreCase)),
            JournalDuplicatesCount = matchPairs.Count(m => m.VoucherCategory.Contains("Journal", StringComparison.OrdinalIgnoreCase)),
            CreditDebitNoteDuplicatesCount = matchPairs.Count(m => m.VoucherCategory.Contains("Credit", StringComparison.OrdinalIgnoreCase) || m.VoucherCategory.Contains("Debit", StringComparison.OrdinalIgnoreCase)),
            TotalPotentialExposureRupees = matchPairs.Sum(m => Math.Min(Math.Abs(m.OriginalTransaction.TotalAmount), Math.Abs(m.PotentialDuplicate.TotalAmount))),
            EvaluatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Duplicate detection complete: {Exact} exact, {Likely} likely, {Possible} possible duplicates found",
            summary.ExactDuplicatesCount, summary.LikelyDuplicatesCount, summary.PossibleDuplicatesCount);

        return summary;
    }

    private static string GetPairKey(string id1, string id2)
    {
        return string.Compare(id1, id2, StringComparison.Ordinal) < 0
            ? $"{id1}:{id2}"
            : $"{id2}:{id1}";
    }

    private static DuplicateMatchPair BuildMatchPair(
        string companyId,
        DuplicateConfidenceTier tier,
        double confidence,
        string strategyUsed,
        TransactionSnapshot v1,
        TransactionSnapshot v2)
    {
        var matching = new List<string>();
        var differences = new List<string>();
        var fieldDetails = new List<FieldComparisonResult>();

        // 1. Party Ledger
        bool partyMatch = string.Equals(v1.PartyLedgerName, v2.PartyLedgerName, StringComparison.OrdinalIgnoreCase);
        if (partyMatch) matching.Add("Party Ledger");
        else differences.Add($"Party Ledger ('{v1.PartyLedgerName}' vs '{v2.PartyLedgerName}')");
        fieldDetails.Add(new FieldComparisonResult { FieldName = "Party Ledger", OriginalValue = v1.PartyLedgerName, DuplicateValue = v2.PartyLedgerName, IsMatched = partyMatch });

        // 2. Amount
        bool amountMatch = Math.Abs(v1.TotalAmount) == Math.Abs(v2.TotalAmount);
        if (amountMatch) matching.Add("Total Amount");
        else differences.Add($"Amount (₹{v1.TotalAmount:N2} vs ₹{v2.TotalAmount:N2})");
        fieldDetails.Add(new FieldComparisonResult { FieldName = "Total Amount", OriginalValue = $"₹{v1.TotalAmount:N2}", DuplicateValue = $"₹{v2.TotalAmount:N2}", IsMatched = amountMatch });

        // 3. Voucher Date
        int daysDiff = Math.Abs((v1.VoucherDate - v2.VoucherDate).Days);
        bool dateMatch = daysDiff == 0;
        if (dateMatch) matching.Add("Transaction Date");
        else differences.Add($"Date ({v1.VoucherDate:dd-MMM-yyyy} vs {v2.VoucherDate:dd-MMM-yyyy}, {daysDiff} days delta)");
        fieldDetails.Add(new FieldComparisonResult { FieldName = "Date", OriginalValue = v1.VoucherDate.ToString("dd-MMM-yyyy"), DuplicateValue = v2.VoucherDate.ToString("dd-MMM-yyyy"), IsMatched = dateMatch, Description = dateMatch ? "Identical Date" : $"{daysDiff} days difference" });

        // 4. Voucher Type
        bool typeMatch = string.Equals(v1.VoucherTypeName, v2.VoucherTypeName, StringComparison.OrdinalIgnoreCase);
        if (typeMatch) matching.Add("Voucher Type");
        else differences.Add($"Voucher Type ('{v1.VoucherTypeName}' vs '{v2.VoucherTypeName}')");
        fieldDetails.Add(new FieldComparisonResult { FieldName = "Voucher Type", OriginalValue = v1.VoucherTypeName, DuplicateValue = v2.VoucherTypeName, IsMatched = typeMatch });

        // 5. Voucher / Ref Number
        bool numMatch = !string.IsNullOrWhiteSpace(v1.VoucherNumber) && string.Equals(v1.VoucherNumber, v2.VoucherNumber, StringComparison.OrdinalIgnoreCase);
        if (numMatch) matching.Add("Voucher Number");
        else differences.Add($"Voucher Number ('{v1.VoucherNumber}' vs '{v2.VoucherNumber}')");
        fieldDetails.Add(new FieldComparisonResult { FieldName = "Voucher Number", OriginalValue = v1.VoucherNumber, DuplicateValue = v2.VoucherNumber, IsMatched = numMatch });

        // 6. Narration
        double narrSim = CalculateTextSimilarity(v1.Narration, v2.Narration);
        bool narrMatch = narrSim >= 0.8;
        if (narrMatch) matching.Add("Narration");
        else if (narrSim >= 0.4) matching.Add($"Narration (~{(narrSim * 100):F0}% similar)");
        else differences.Add("Narration Text");
        fieldDetails.Add(new FieldComparisonResult { FieldName = "Narration", OriginalValue = v1.Narration ?? "(None)", DuplicateValue = v2.Narration ?? "(None)", IsMatched = narrMatch, Description = $"{(narrSim * 100):F0}% text similarity" });

        // Neutral, non-prejudicial explanation
        string explanation = tier switch
        {
            DuplicateConfidenceTier.ExactDuplicate =>
                $"Potential exact duplicate match detected between voucher {v1.VoucherNumber} and {v2.VoucherNumber} sharing identical party '{v1.PartyLedgerName}', amount ₹{Math.Abs(v1.TotalAmount):N2}, and transaction date {v1.VoucherDate:dd-MMM-yyyy}.",
            DuplicateConfidenceTier.LikelyDuplicate =>
                $"Potential likely duplicate match detected: Voucher {v1.VoucherNumber} and {v2.VoucherNumber} share identical party '{v1.PartyLedgerName}' and amount ₹{Math.Abs(v1.TotalAmount):N2} posted within {daysDiff} days under {v1.VoucherTypeName} vouchers.",
            _ =>
                $"Potential duplicate match identified for auditor evaluation between {v1.VoucherNumber} and {v2.VoucherNumber} based on matching party '{v1.PartyLedgerName}' with proximate dates and similar commercial attributes."
        };

        var evidence = new
        {
            MatchTier = tier.ToString(),
            Confidence = confidence,
            MatchingFields = matching,
            Differences = differences,
            DateDifferenceDays = daysDiff,
            OriginalId = v1.VoucherId,
            DuplicateId = v2.VoucherId
        };

        return new DuplicateMatchPair
        {
            CompanyId = companyId,
            Tier = tier,
            ConfidenceScore = confidence,
            StrategyUsed = strategyUsed,
            VoucherCategory = v1.VoucherTypeName,
            OriginalTransaction = v1,
            PotentialDuplicate = v2,
            MatchingFields = matching,
            DifferenceFields = differences,
            DetailedFieldComparisons = fieldDetails,
            Explanation = explanation,
            EvidenceJson = JsonSerializer.Serialize(evidence),
            ReviewStatus = ReviewStatus.Pending
        };
    }

    private static double CalculateTextSimilarity(string? s1, string? s2)
    {
        if (string.IsNullOrWhiteSpace(s1) && string.IsNullOrWhiteSpace(s2)) return 1.0;
        if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return 0.0;

        var tokens1 = s1.ToLowerInvariant().Split(new[] { ' ', ',', '.', ';', '-', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tokens2 = s2.ToLowerInvariant().Split(new[] { ' ', ',', '.', ';', '-', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        int intersection = tokens1.Intersect(tokens2).Count();
        int union = tokens1.Union(tokens2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }
}
