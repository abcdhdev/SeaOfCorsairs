using System.Text;

public static class UiTextSanitizer
{
    /// <summary>
    /// Sanitizes user-provided text before assigning to UI Toolkit labels.
    /// - Prevents rich-text tag parsing by replacing angle brackets.
    /// - Removes problematic control characters.
    /// - Repairs malformed UTF-16 surrogate pairs.
    /// </summary>
    public static string SanitizeForLabel(string value, bool collapseWhitespace)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        bool previousWasSpace = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (char.IsSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsSurrogatePair(c, value[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(value[i + 1]);
                    i++;
                    previousWasSpace = false;
                }
                else
                {
                    sb.Append('\uFFFD');
                    previousWasSpace = false;
                }

                continue;
            }

            if (c == '<')
            {
                sb.Append('＜');
                previousWasSpace = false;
                continue;
            }

            if (c == '>')
            {
                sb.Append('＞');
                previousWasSpace = false;
                continue;
            }

            if (char.IsControl(c))
            {
                if (c == '\n' || c == '\r' || c == '\t')
                {
                    if (collapseWhitespace && !previousWasSpace)
                    {
                        sb.Append(' ');
                        previousWasSpace = true;
                    }
                }

                continue;
            }

            if (collapseWhitespace && char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
                continue;
            }

            sb.Append(c);
            previousWasSpace = false;
        }

        return sb.ToString().Trim();
    }
}
