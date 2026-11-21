namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Performs local structural and checksum validation of a GSTIN. This does not
/// verify registration status or establish invoice authenticity or ITC eligibility.
/// </summary>
public static class GstinValidator
{
    public static bool IsValid(string? gstin)
    {
        var value = (gstin ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();
        if (value.Length != 15 || !value[..2].All(char.IsDigit) ||
            value[0] == '0' || value[1] == '0' ||
            !System.Text.RegularExpressions.Regex.IsMatch(value[2..12], "^[A-Z]{5}[0-9]{4}[A-Z]$") ||
            !char.IsLetterOrDigit(value[12]) || value[13] != 'Z')
            return false;

        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var digit = alphabet.IndexOf(value[i]);
            if (digit < 0) return false;
            var product = digit * (i % 2 == 0 ? 1 : 2);
            sum += product / 36 + product % 36;
        }

        return value[14] == alphabet[(36 - sum % 36) % 36];
    }
}
