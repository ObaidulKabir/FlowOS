using System;
using System.Globalization;
using System.Text;

namespace FlowOS.Api.Http;

/// <summary>
/// CR-10c: opaque change-feed cursor. Clients store it and send it back unchanged; the format may
/// change in a later schema version.
/// </summary>
public static class ChangeFeedCursor
{
    private const string Prefix = "v1:";

    public static string Encode(long sequence)
    {
        var bytes = Encoding.ASCII.GetBytes(Prefix + sequence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>An empty cursor means "from the start" (sequence 0).</summary>
    public static bool TryDecode(string? cursor, out long sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        if (cursor.Length > 64) return false;

        var base64 = cursor.Trim().Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        string text;
        try
        {
            text = Encoding.ASCII.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return false;
        }

        return text.StartsWith(Prefix, StringComparison.Ordinal)
            && long.TryParse(text.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            && sequence >= 0;
    }
}
