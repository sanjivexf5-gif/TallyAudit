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
            var trimmed = responseContent.TrimStart();
            if (trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) || 
                trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "Endpoint returned HTML instead of Tally XML. Verify the Tally port.");
            }

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

            if (responseContent.Contains("<SYNTAXERROR>", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(responseContent, @"<SYNTAXERROR>(.*?)</SYNTAXERROR>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return (true, match.Success ? match.Groups[1].Value.Trim() : "Tally XML syntax error.");
            }

            if (Regex.IsMatch(responseContent, @"<STATUS>\s*0\s*</STATUS>", RegexOptions.IgnoreCase))
            {
                var errorMatch = Regex.Match(responseContent, @"<(?:ERROR|ERRORMESSAGE|LINEERROR|DESCRIPTION)>(.*?)</(?:ERROR|ERRORMESSAGE|LINEERROR|DESCRIPTION)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var errDetail = errorMatch.Success ? errorMatch.Groups[1].Value.Trim() : "Command failed or unsupported request.";
                return (true, $"Tally returned Status 0: {errDetail}");
            }

            var errMatch = Regex.Match(responseContent, @"<(?:ERROR|ERRORMESSAGE|EXCEPTION|FATALERROR)>(.*?)</(?:ERROR|ERRORMESSAGE|EXCEPTION|FATALERROR)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (errMatch.Success && !string.IsNullOrWhiteSpace(errMatch.Groups[1].Value))
            {
                return (true, $"Tally error: {errMatch.Groups[1].Value.Trim()}");
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while inspecting Tally response for errors.");
            return (true, $"Failed to inspect Tally response: {ex.Message}");
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
        var (hasError, errMsg) = CheckForTallyErrors(responseContent, format);
        if (hasError)
        {
            throw new InvalidOperationException($"Tally error in company profile response: {errMsg}");
        }

        try
        {
            var sanitizedXml = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitizedXml);

            var companyNodes = doc.SelectNodes("//COMPANY");
            if (companyNodes == null || companyNodes.Count == 0) return null;

            XmlNode companyNode = companyNodes[0]!;

            string name = companyNode.Attributes?["NAME"]?.Value?.Trim()
                          ?? companyNode.SelectSingleNode("NAME")?.InnerText?.Trim()
                          ?? companyNode.SelectSingleNode("NAME.LIST/NAME")?.InnerText?.Trim()
                          ?? string.Empty;

            var profile = new TallyCompanyProfile
            {
                Name = name,
                FormalName = companyNode.SelectSingleNode("FORMALNAME")?.InnerText?.Trim()
                             ?? companyNode.SelectSingleNode("BASICCOMPANYFORMALNAME")?.InnerText?.Trim()
                             ?? name,
                GSTIN = companyNode.SelectSingleNode("GSTIN")?.InnerText?.Trim()
                        ?? companyNode.SelectSingleNode("PARTYGSTIN")?.InnerText?.Trim(),
                PAN = companyNode.SelectSingleNode("PAN")?.InnerText?.Trim()
                      ?? companyNode.SelectSingleNode("INCOMETAXNUMBER")?.InnerText?.Trim(),
                StateName = companyNode.SelectSingleNode("STATENAME")?.InnerText?.Trim(),
                StateCode = companyNode.SelectSingleNode("STATECODE")?.InnerText?.Trim(),
                BaseCurrencySymbol = companyNode.SelectSingleNode("BASICCURRENCYSYMBOL")?.InnerText?.Trim() ?? "₹"
            };

            var dateStr = companyNode.SelectSingleNode("BOOKSBEGINNINGFROM")?.InnerText?.Trim()
                          ?? companyNode.SelectSingleNode("STARTINGFROM")?.InnerText?.Trim();
            if (!string.IsNullOrEmpty(dateStr))
            {
                string[] dateFormats = new[] { "yyyyMMdd", "yyyy-MM-dd", "dd-MMM-yyyy", "dd-MM-yyyy", "d-MMM-yyyy", "yyyy/MM/dd" };
                if (DateTime.TryParseExact(dateStr, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var booksFrom) ||
                    DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out booksFrom))
                {
                    profile.BooksBeginningFrom = booksFrom;
                }
                else
                {
                    profile.BooksBeginningFrom = new DateTime(DateTime.Now.Year, 4, 1);
                }
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
        catch (XmlException ex)
        {
            _logger.LogError(ex, "Failed to parse Tally company profile XML");
            throw new InvalidOperationException($"Malformed XML received for company profile: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse Tally company profile XML");
            throw new InvalidOperationException($"Failed to parse company profile XML: {ex.Message}", ex);
        }
    }

    public IReadOnlyList<TallyLedgerDto> ParseLedgers(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var (hasError, errMsg) = CheckForTallyErrors(responseContent, format);
        if (hasError)
        {
            throw new InvalidOperationException($"Tally error in ledger collection response: {errMsg}");
        }

        var results = new List<TallyLedgerDto>();
        try
        {
            var sanitized = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitized);

            var ledgerNodes = doc.SelectNodes("//LEDGER");
            if (ledgerNodes == null) return results;

            foreach (XmlNode node in ledgerNodes)
            {
                string? name = node.Attributes?["NAME"]?.Value?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = node.SelectSingleNode("NAME")?.InnerText?.Trim();
                }
                if (string.IsNullOrEmpty(name))
                {
                    name = node.SelectSingleNode("NAME.LIST/NAME")?.InnerText?.Trim();
                }
                if (string.IsNullOrEmpty(name)) continue;

                string parent = node.SelectSingleNode("PARENT")?.InnerText?.Trim()
                                ?? node.Attributes?["PARENT"]?.Value?.Trim()
                                ?? "Sundry Debtors";

                var ledger = new TallyLedgerDto
                {
                    Name = name,
                    ParentGroup = parent,
                    GSTIN = node.SelectSingleNode("GSTIN")?.InnerText?.Trim() ?? node.SelectSingleNode("PARTYGSTIN")?.InnerText?.Trim(),
                    PAN = node.SelectSingleNode("INCOMETAXNUMBER")?.InnerText?.Trim() ?? node.SelectSingleNode("PAN")?.InnerText?.Trim(),
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
        catch (XmlException ex)
        {
            _logger.LogError(ex, "Failed to parse Tally ledgers collection XML");
            throw new InvalidOperationException($"Malformed XML received for ledgers: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse Tally ledgers collection XML");
            throw new InvalidOperationException($"Failed to parse ledgers XML: {ex.Message}", ex);
        }

        return results;
    }

    public IReadOnlyList<string> ParseGroups(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var (hasError, errMsg) = CheckForTallyErrors(responseContent, format);
        if (hasError)
        {
            throw new InvalidOperationException($"Tally error in group collection response: {errMsg}");
        }

        var results = new List<string>();
        try
        {
            var sanitized = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitized);

            var groupNodes = doc.SelectNodes("//GROUP");
            if (groupNodes == null) return results;

            foreach (XmlNode node in groupNodes)
            {
                string? name = node.Attributes?["NAME"]?.Value?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = node.SelectSingleNode("NAME")?.InnerText?.Trim();
                }
                if (string.IsNullOrEmpty(name))
                {
                    name = node.SelectSingleNode("NAME.LIST/NAME")?.InnerText?.Trim();
                }
                if (string.IsNullOrEmpty(name) && !node.HasChildNodes)
                {
                    name = node.InnerText?.Trim();
                }

                if (!string.IsNullOrEmpty(name) && !results.Contains(name))
                {
                    results.Add(name);
                }
            }
        }
        catch (XmlException ex)
        {
            _logger.LogError(ex, "Failed to parse Tally groups XML");
            throw new InvalidOperationException($"Malformed XML received for groups: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse Tally groups XML");
            throw new InvalidOperationException($"Failed to parse groups XML: {ex.Message}", ex);
        }

        return results;
    }

    public IReadOnlyList<TallyVoucherDto> ParseVouchers(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var (hasError, errMsg) = CheckForTallyErrors(responseContent, format);
        if (hasError)
        {
            throw new InvalidOperationException($"Tally error in voucher collection response: {errMsg}");
        }

        var results = new List<TallyVoucherDto>();
        try
        {
            var sanitized = SanitizeXmlContent(responseContent);
            var doc = new XmlDocument();
            doc.LoadXml(sanitized);

            var voucherNodes = doc.SelectNodes("//VOUCHER");
            if (voucherNodes == null) return results;

            string[] dateFormats = new[] { "yyyyMMdd", "yyyy-MM-dd", "dd-MMM-yyyy", "dd-MM-yyyy", "d-MMM-yyyy", "yyyy/MM/dd" };

            foreach (XmlNode node in voucherNodes)
            {
                var guid = node.SelectSingleNode("GUID")?.InnerText?.Trim()
                           ?? node.Attributes?["GUID"]?.Value?.Trim()
                           ?? Guid.NewGuid().ToString();

                var vNumber = node.SelectSingleNode("VOUCHERNUMBER")?.InnerText?.Trim()
                              ?? node.Attributes?["VOUCHERNUMBER"]?.Value?.Trim()
                              ?? string.Empty;

                var voucher = new TallyVoucherDto
                {
                    Guid = guid,
                    VoucherNumber = vNumber,
                    ReferenceNumber = node.SelectSingleNode("REFERENCE")?.InnerText?.Trim(),
                    VoucherType = node.SelectSingleNode("VOUCHERTYPENAME")?.InnerText?.Trim() ?? "Journal",
                    Narration = node.SelectSingleNode("NARRATION")?.InnerText?.Trim(),
                    PartyLedgerName = node.SelectSingleNode("PARTYLEDGERNAME")?.InnerText?.Trim(),
                    IsCancelled = node.SelectSingleNode("ISCANCELLED")?.InnerText?.Trim()?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true,
                    IsOptional = node.SelectSingleNode("ISOPTIONAL")?.InnerText?.Trim()?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true
                };

                var dateStr = node.SelectSingleNode("DATE")?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(dateStr) && (DateTime.TryParseExact(dateStr, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var vDate) ||
                                                       DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out vDate)))
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

                // Extract ledger entries - prefer .LIST nodes to avoid duplicating parent/child matches
                var entryNodes = node.SelectNodes(".//ALLLEDGERENTRIES.LIST | .//LEDGERENTRIES.LIST");
                if (entryNodes == null || entryNodes.Count == 0)
                {
                    entryNodes = node.SelectNodes(".//ALLLEDGERENTRIES | .//LEDGERENTRIES");
                }

                if (entryNodes != null)
                {
                    foreach (XmlNode entryNode in entryNodes)
                    {
                        var ledgerName = entryNode.SelectSingleNode("LEDGERNAME")?.InnerText?.Trim()
                                         ?? entryNode.Attributes?["LEDGERNAME"]?.Value?.Trim()
                                         ?? entryNode.SelectSingleNode("NAME")?.InnerText?.Trim()
                                         ?? string.Empty;

                        var entry = new TallyVoucherEntryDto
                        {
                            LedgerName = ledgerName
                        };

                        if (decimal.TryParse(entryNode.SelectSingleNode("AMOUNT")?.InnerText?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var entryAmt))
                        {
                            entry.Amount = entryAmt;

                            var deemedPositive = entryNode.SelectSingleNode("ISDEEMEDPOSITIVE")?.InnerText?.Trim();
                            if (!string.IsNullOrEmpty(deemedPositive))
                            {
                                entry.IsDebit = deemedPositive.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                            }
                            else
                            {
                                // In standard Tally, positive amount indicates Debit, negative indicates Credit
                                entry.IsDebit = entryAmt > 0;
                            }
                        }

                        entry.BillRefType = entryNode.SelectSingleNode("BILLTYPE")?.InnerText?.Trim();
                        entry.BillName = entryNode.SelectSingleNode("BILLNAME")?.InnerText?.Trim();

                        if (!string.IsNullOrEmpty(entry.LedgerName))
                        {
                            voucher.Entries.Add(entry);
                        }
                    }
                }

                // If TotalAmount was absent on VOUCHER node, calculate from entries
                if (voucher.TotalAmount == 0 && voucher.Entries.Count > 0)
                {
                    var debitSum = voucher.Entries.Where(e => e.IsDebit).Sum(e => Math.Abs(e.Amount));
                    voucher.TotalAmount = debitSum > 0 ? debitSum : voucher.Entries.Sum(e => Math.Abs(e.Amount)) / 2.0m;
                }

                // If PartyLedgerName was absent on VOUCHER node, infer from entries
                if (string.IsNullOrEmpty(voucher.PartyLedgerName) && voucher.Entries.Count > 0)
                {
                    var partyEntry = voucher.Entries.FirstOrDefault(e => !e.LedgerName.Contains("Sales", StringComparison.OrdinalIgnoreCase) &&
                                                                         !e.LedgerName.Contains("Purchase", StringComparison.OrdinalIgnoreCase) &&
                                                                         !e.LedgerName.Contains("GST", StringComparison.OrdinalIgnoreCase) &&
                                                                         !e.LedgerName.Contains("Tax", StringComparison.OrdinalIgnoreCase));
                    if (partyEntry != null)
                    {
                        voucher.PartyLedgerName = partyEntry.LedgerName;
                    }
                }

                results.Add(voucher);
            }
        }
        catch (XmlException ex)
        {
            _logger.LogError(ex, "Failed to parse Tally vouchers XML");
            throw new InvalidOperationException($"Malformed XML received for vouchers: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse Tally vouchers XML");
            throw new InvalidOperationException($"Failed to parse vouchers XML: {ex.Message}", ex);
        }

        return results;
    }

    private static string SanitizeXmlContent(string rawXml)
    {
        if (string.IsNullOrEmpty(rawXml)) return string.Empty;

        // Strip non-printable invalid XML control characters except tab, LF, CR
        var cleaned = Regex.Replace(rawXml, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", string.Empty);

        // Escape raw unescaped ampersands common in Indian ledger names (e.g. "M/S RAM & SHYAM CO")
        var sanitized = Regex.Replace(cleaned, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);)", "&amp;");
        return sanitized;
    }
}
