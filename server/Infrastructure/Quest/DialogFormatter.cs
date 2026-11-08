namespace SGO.Server.Infrastructure.Quest;

public static class DialogFormatter
{
    public static string CleanText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var text = value;

        // Legacy dialog strings use short presentation prefixes such as:
        // n, n1, n7, p7, s ... They were interpreted by the old client/server
        // stack and must not be shown as literal characters in the recovered client.
        if (text.Length >= 1)
        {
            if (text[0] is 'n' or 'p' or 's' or 'c')
            {
                var index = 1;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;
                text = text[index..];
            }
        }

        return text;
    }
}
