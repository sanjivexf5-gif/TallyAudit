using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyResponseParser : ITallyResponseParser
{
    private readonly ILogger<TallyResponseParser> _logger;

    public TallyResponseParser(ILogger<TallyResponseParser> logger)
    {
        _logger = logger;
    }

    public (bool HasError, string? ErrorMessage) CheckForTallyErrors(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return (true, "Empty response received from TallyPrime.");
        }

        if (format == TallyRequestFormat.Json)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(responseContent);
                var root = jsonDoc.RootElement;
                if (root.TryGetProperty("HEADER", out var header) &&
                    header.TryGetProperty("STATUS", out var status) &&
                    status.GetString() == "0")
                {
                    var msg = header.TryGetProperty("ERROR", out var err) ? err.GetString() : "Tally reported command failure.";
                    return (true, msg);
                }
                return (false, null);
            }
            catch (JsonException ex)
            {
                return (true, $"Malformed JSON response: {ex.Message}");
            }
        }

        // XML error checks
        try
        {
            if (responseContent.Contains("<LINEERROR>", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(responseContent, @"<LINEERROR>(.*?)</LINEERROR>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return (true, match.Success ? match.Groups[1].Value.Trim() : "Tally script execution line error.");
            }

            if (responseContent.Contains("<PARSERROR>", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(responseContent, @"<PARSERROR>(.*?)</PARSERROR>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return (true, match.Success ? match.Groups[1].Value.Trim() : "Tally XML parse syntax error.");
            }

            if (responseContent.Contains("<STATUS>0</STATUS>", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "Tally returned Status 0 (Command failed or unsupported request).");
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while inspecting Tally response for errors.");
            return (false, null);
        }
    }

    public IReadOnlyList<string> ParseCompanyList(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var (hasError, error) = CheckForTallyErrors(responseContent, format);
        if (hasError)
        {
            _logger.LogWarning("Cannot parse company list due to Tally error: {Error}", error);
            return Array.Empty<string>();
        }

        var results = new List<string>();

        if (format == TallyRequestFormat.Json)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("BODY", out var body) &&
                    body.TryGetProperty("DATA", out var data) &&
                    data.TryGetProperty("COLLECTION", out var collection))
                {
                    if (collection.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in collection.EnumerateArray())
                        {
                            if (el.TryGetProperty("NAME", out var nameProp))
                            {
                                var n = nameProp.GetString()?.Trim();
                                if (!string.IsNullOrEmpty(n) && !results.Contains(n)) results.Add(n);
                            }
                        }
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON company list");
            }
        }

        // XML Parsing
        try
        {
            var sanitizedXml = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitizedXml);

            // Find all elements representing COMPANY (or any node with local name 'COMPANY')
            var companyNodes = doc.GetElementsByTagName("COMPANY");
            if (companyNodes.Count == 0)
            {
                // Fallback to searching all nodes that might be company blocks or contain company lists
                companyNodes = doc.SelectNodes("//COMPANY") ?? doc.SelectNodes("//*[local-name()='COMPANY']");
            }

            if (companyNodes != null)
            {
                foreach (XmlNode companyNode in companyNodes)
                {
                    string? name = null;

                    // 1. First inspect NAME attribute
                    if (companyNode.Attributes != null && companyNode.Attributes["NAME"] != null)
                    {
                        name = companyNode.Attributes["NAME"]?.Value;
                    }

                    // 2. Then inspect direct NAME element
                    if (string.IsNullOrEmpty(name))
                    {
                        var directNameNode = companyNode.SelectSingleNode("NAME");
                        if (directNameNode != null && !directNameNode.HasChildNodes)
                        {
                            name = directNameNode.InnerText;
                        }
                        else if (directNameNode != null)
                        {
                            name = directNameNode.Value ?? directNameNode.InnerText;
                        }
                    }

                    // 3. Then inspect NAME.LIST/NAME
                    if (string.IsNullOrEmpty(name))
                    {
                        var nameListNode = companyNode.SelectSingleNode("NAME.LIST/NAME");
                        if (nameListNode != null)
                        {
                            name = nameListNode.InnerText;
                        }
                    }

                    // 4. Clean and validate the extracted name
                    if (!string.IsNullOrEmpty(name))
                    {
                        name = name.Trim();
                        // Filter out structural/unrelated XML tag text like COLLECTION, COMPANY.LIST, etc.
                        if (!string.IsNullOrEmpty(name) && 
                            !name.Equals("COLLECTION", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("COMPANY.LIST", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("FORMALNAME", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("STATE", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("GSTIN", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("<") && !name.Contains(">"))
                        {
                            // Deduplicate case-insensitively while preserving original company name
                            if (!results.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            {
                                results.Add(name);
                            }
                        }
                    }
                }
            }

            // If we found zero companies with structured XML parsing, use a robust regex fallback specifically tuned for company structures
            if (results.Count == 0)
            {
                var nameMatches = Regex.Matches(responseContent, @"<COMPANY[^>]*NAME=""([^""]+)""", RegexOptions.IgnoreCase);
                foreach (Match m in nameMatches)
                {
                    var val = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(val) && !results.Any(r => r.Equals(val, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(val);
                    }
                }

                var listMatches = Regex.Matches(responseContent, @"<COMPANY[^>]*>[\s\S]*?<NAME[^>]*>([^<]+)</NAME>", RegexOptions.IgnoreCase);
                foreach (Match m in listMatches)
                {
                    var val = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(val) && 
                        !val.Equals("COLLECTION", StringComparison.OrdinalIgnoreCase) &&
                        !val.Equals("COMPANY.LIST", StringComparison.OrdinalIgnoreCase) &&
                        !results.Any(r => r.Equals(val, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(val);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Standard XML company parsing failed. Using general regex fallback.");
            var matches = Regex.Matches(responseContent, @"<NAME[^>]*>([^<]+)</NAME>", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                var val = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(val) && 
                    !val.Equals("COLLECTION", StringComparison.OrdinalIgnoreCase) &&
                    !val.Equals("COMPANY.LIST", StringComparison.OrdinalIgnoreCase) &&
                    !results.Any(r => r.Equals(val, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(val);
                }
            }
        }

        return results;
    }

    public TallyCompanyProfile? ParseCompanyProfile(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var (hasError, _) = CheckForTallyErrors(responseContent, format);
        if (hasError) return null;

        try
        {
            var sanitizedXml = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitizedXml);

            var companyNode = doc.SelectSingleNode("//COMPANY");
            if (companyNode == null) return null;

            var profile = new TallyCompanyProfile
            {
                Name = companyNode.SelectSingleNode("NAME")?.InnerText?.Trim() ?? string.Empty,
                FormalName = companyNode.SelectSingleNode("FORMALNAME")?.InnerText?.Trim(),
                GSTIN = companyNode.SelectSingleNode("GSTIN")?.InnerText?.Trim(),
                PAN = companyNode.SelectSingleNode("PAN")?.InnerText?.Trim(),
                StateName = companyNode.SelectSingleNode("STATENAME")?.InnerText?.Trim(),
                StateCode = companyNode.SelectSingleNode("STATECODE")?.InnerText?.Trim(),
                BaseCurrencySymbol = companyNode.SelectSingleNode("BASICCURRENCYSYMBOL")?.InnerText?.Trim() ?? "₹"
            };

            var dateStr = companyNode.SelectSingleNode("BOOKSBEGINNINGFROM")?.InnerText?.Trim();
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var booksFrom))
            {
                profile.BooksBeginningFrom = booksFrom;
            }
            else
            {
                profile.BooksBeginningFrom = new DateTime(DateTime.Now.Year, 4, 1);
            }

            if (long.TryParse(companyNode.SelectSingleNode("ALTERID")?.InnerText?.Trim(), out var alterId))
            {
                profile.AlterId = alterId;
            }

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Tally company profile XML");
            return null;
        }
    }

    public IReadOnlyList<TallyLedgerDto> ParseLedgers(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var results = new List<TallyLedgerDto>();
        var (hasError, _) = CheckForTallyErrors(responseContent, format);
        if (hasError) return results;

        try
        {
            var sanitized = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitized);

            var ledgerNodes = doc.SelectNodes("//LEDGER");
            if (ledgerNodes == null) return results;

            foreach (XmlNode node in ledgerNodes)
            {
                var name = node.SelectSingleNode("NAME")?.InnerText?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var ledger = new TallyLedgerDto
                {
                    Name = name,
                    ParentGroup = node.SelectSingleNode("PARENT")?.InnerText?.Trim() ?? "Sundry Debtors",
                    GSTIN = node.SelectSingleNode("GSTIN")?.InnerText?.Trim(),
                    PAN = node.SelectSingleNode("INCOMETAXNUMBER")?.InnerText?.Trim(),
                    StateName = node.SelectSingleNode("STATENAME")?.InnerText?.Trim(),
                    TaxType = node.SelectSingleNode("TAXTYPE")?.InnerText?.Trim(),
                    HsnCode = node.SelectSingleNode("HSNCODE")?.InnerText?.Trim()
                };

                if (decimal.TryParse(node.SelectSingleNode("OPENINGBALANCE")?.InnerText?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var opBal))
                {
                    ledger.OpeningBalance = opBal;
                }

                if (decimal.TryParse(node.SelectSingleNode("CLOSINGBALANCE")?.InnerText?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var clBal))
                {
                    ledger.ClosingBalance = clBal;
                }

                if (decimal.TryParse(node.SelectSingleNode("GSTRATE")?.InnerText?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                {
                    ledger.GstRate = rate;
                }

                if (long.TryParse(node.SelectSingleNode("ALTERID")?.InnerText?.Trim(), out var alterId))
                {
                    ledger.AlterId = alterId;
                }

                results.Add(ledger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Tally ledgers collection XML");
        }

        return results;
    }

    public IReadOnlyList<string> ParseGroups(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var results = new List<string>();
        var (hasError, _) = CheckForTallyErrors(responseContent, format);
        if (hasError) return results;

        try
        {
            var sanitized = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitized);

            var groupNodes = doc.SelectNodes("//GROUP/NAME | //GROUP");
            if (groupNodes == null) return results;

            foreach (XmlNode node in groupNodes)
            {
                var name = node.InnerText?.Trim();
                if (!string.IsNullOrEmpty(name) && !results.Contains(name))
                {
                    results.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Tally groups XML");
        }

        return results;
    }

    public IReadOnlyList<TallyVoucherDto> ParseVouchers(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var results = new List<TallyVoucherDto>();
        var (hasError, _) = CheckForTallyErrors(responseContent, format);
        if (hasError) return results;

        try
        {
            var sanitized = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitized);

            var voucherNodes = doc.SelectNodes("//VOUCHER");
            if (voucherNodes == null) return results;

            foreach (XmlNode node in voucherNodes)
            {
                var voucher = new TallyVoucherDto
                {
                    Guid = node.SelectSingleNode("GUID")?.InnerText?.Trim() ?? Guid.NewGuid().ToString(),
                    VoucherNumber = node.SelectSingleNode("VOUCHERNUMBER")?.InnerText?.Trim() ?? string.Empty,
                    ReferenceNumber = node.SelectSingleNode("REFERENCE")?.InnerText?.Trim(),
                    VoucherType = node.SelectSingleNode("VOUCHERTYPENAME")?.InnerText?.Trim() ?? "Journal",
                    Narration = node.SelectSingleNode("NARRATION")?.InnerText?.Trim(),
                    PartyLedgerName = node.SelectSingleNode("PARTYLEDGERNAME")?.InnerText?.Trim(),
                    IsCancelled = node.SelectSingleNode("ISCANCELLED")?.InnerText?.Trim() == "Yes",
                    IsOptional = node.SelectSingleNode("ISOPTIONAL")?.InnerText?.Trim() == "Yes"
                };

                var dateStr = node.SelectSingleNode("DATE")?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var vDate))
                {
                    voucher.VoucherDate = vDate;
                }
                else
                {
                    voucher.VoucherDate = DateTime.Today;
                }

                if (decimal.TryParse(node.SelectSingleNode("AMOUNT")?.InnerText?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                {
                    voucher.TotalAmount = Math.Abs(amt);
                }

                if (long.TryParse(node.SelectSingleNode("ALTERID")?.InnerText?.Trim(), out var alterId))
                {
                    voucher.AlterId = alterId;
                }

                // Extract ledger entries
                var entryNodes = node.SelectNodes(".//ALLLEDGERENTRIES.LIST");
                if (entryNodes != null)
                {
                    foreach (XmlNode entryNode in entryNodes)
                    {
                        var entry = new TallyVoucherEntryDto
                        {
                            LedgerName = entryNode.SelectSingleNode("LEDGERNAME")?.InnerText?.Trim() ?? string.Empty
                        };

                        if (decimal.TryParse(entryNode.SelectSingleNode("AMOUNT")?.InnerText?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var entryAmt))
                        {
                            // In Tally, negative amount indicates Credit, positive indicates Debit
                            entry.Amount = entryAmt;
                            entry.IsDebit = entryAmt > 0;
                        }

                        entry.BillRefType = entryNode.SelectSingleNode("BILLTYPE")?.InnerText?.Trim();
                        entry.BillName = entryNode.SelectSingleNode("BILLNAME")?.InnerText?.Trim();

                        if (!string.IsNullOrEmpty(entry.LedgerName))
                        {
                            voucher.Entries.Add(entry);
                        }
                    }
                }

                results.Add(voucher);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Tally vouchers XML");
        }

        return results;
    }

    private static string SanitizeXmlContent(string rawXml)
    {
        if (string.IsNullOrEmpty(rawXml)) return string.Empty;

        // Escape raw unescaped ampersands common in Indian ledger names (e.g. "M/S RAM & SHYAM CO")
        var sanitized = Regex.Replace(rawXml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);)", "&amp;");
        return sanitized;
    }
}
