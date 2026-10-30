using System.Globalization;

namespace Accounts.Services.Ocr.Parsers;

public static class ServiceDateParser
{
    private static readonly string[] Formats =
    [
        "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy", "M/d/yy",
        "MM-dd-yyyy", "M-d-yyyy", "MM-dd-yy", "M-d-yy",
        "MMddyy", "MMddyyyy",
        "yyyy-MM-dd", "yyyy/MM/dd",
        "yyMMdd", "yyyyMMdd"
    ];

    public static DateTime? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();
        // If range like "01/15/2026-01/15/2026" or "01/15/2026 – 01/15/2026", take the first part
        var splitIdx = trimmed.IndexOfAny(['-', '–', '—']);
        // But watch out for "yyyy-MM-dd" where hyphen is a separator!
        // A range typically has a date before the dash, e.g. length > 6 before the dash
        if (splitIdx > 5 && (trimmed.Contains('/') || trimmed.Length > 10))
        {
            var firstPart = trimmed[..splitIdx].Trim();
            if (TryParseInternal(firstPart, out var res)) return res;
        }

        if (TryParseInternal(trimmed, out var fullRes)) return fullRes;

        // Try standard culture parse
        if (DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        return null;
    }

    private static bool TryParseInternal(string s, out DateTime dt)
    {
        return DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
    }
}

