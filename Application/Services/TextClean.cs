namespace SimpleERP.Application.Services;

/// <summary>
/// Removes control characters from typed text before it is stored. The dot-matrix invoice is
/// raw ESC/P: an ESC, form feed or BEL inside a note was stored as typed and reached the
/// printer as a command (reset, bold, page eject), so user text could wreck or blank a
/// printed invoice (security review R9, 2026-10-09). Line breaks and tabs are kept here (a
/// note may have several lines); the printer output strips those too.
/// </summary>
public static class TextClean
{
    public static string? StripControl(string? s)
    {
        if (s == null) return null;
        var clean = true;
        foreach (var c in s)
            if (IsBad(c)) { clean = false; break; }
        if (clean) return s;
        return new string(s.Where(c => !IsBad(c)).ToArray());
    }

    private static bool IsBad(char c) => (c < ' ' && c != '\n' && c != '\r' && c != '\t') || c == '\u007f';
}
