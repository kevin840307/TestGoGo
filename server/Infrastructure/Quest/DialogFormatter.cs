using System.Text;
using System.Text.RegularExpressions;

namespace SGO.Server.Infrastructure.Quest;

/// <summary>Legacy Lua presentation data, kept separate from the visible dialogue text.</summary>
public sealed record LegacyDialogText(
    string Text,
    string PlainText,
    string Prefix,
    string Speaker,
    string Layout,
    LegacyDialogMedia? Media = null);

/// <summary>Original m#image1#image2#audio# script references; paths are data only.</summary>
public sealed record LegacyDialogMedia(string ImageA, string ImageB, string Audio);

public static class DialogFormatter
{
    // Color(r,g,b) is evaluated by Lua before Say/Ask runs. Use a private
    // delimiter so we can emit balanced BBCode instead of nesting color tags.
    private static readonly Regex ColorDirective = new(
        @"\x1F#([0-9A-Fa-f]{6})\x1F",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ColorToken(int r, int g, int b)
        => $"\u001F#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}\u001F";

    public static LegacyDialogText Parse(string? value, string defaultSpeaker = "NPC")
    {
        var source = value ?? "";
        // Original cinematic strings use m#pictureA.spr#pictureB.spr#music.mp3#.
        // The original sprite/audio files are not yet converted to Godot.
        // Retain their identities without leaking raw file paths into speech.
        if (TryParseMedia(source, out var media))
        {
            const string fallback = "劇情演出";
            return new LegacyDialogText(
                fallback, fallback, "m", defaultSpeaker, "Media", media);
        }

        // Do not eat the first letter of ordinary unmarked dialogue
        // ("hello", "please", "shop", etc.). Only known, single-digit
        // legacy forms may be removed.
        var prefix = ReadLegacyPrefix(source);
        source = source[prefix.Length..];

        var layout = prefix.Length > 0 && prefix[0] is 'h' or 'b'
            ? "Help"
            : prefix is "n7" or "p7"
                ? "Narration"
                : "Default";

        // In the legacy help page, '#' separates paragraphs. Do not touch
        // normal dialogue or media paths that happen to contain '#'.
        if (prefix.StartsWith('b'))
            source = source.Replace("#", "\n\n");

        var (rich, plain) = RenderColors(source);

        // The numbered emotion/portrait codes are preserved for future exact
        // asset mapping; do not invent a facial expression from the number.
        var speaker = prefix.StartsWith('p')
            ? "Player"
            : prefix.StartsWith('n')
                ? "NPC"
                : defaultSpeaker;

        return new LegacyDialogText(rich, plain, prefix, speaker, layout);
    }

    // Preserve old callers that only need to remove the legacy prefix.
    public static string CleanText(string? value) => Parse(value).Text;

    // Choice buttons are plain Godot Buttons, not RichTextLabels. Strip Lua
    // color directives without interpreting a choice's first letter as a prefix.
    public static string PlainInlineText(string? value)
        => RenderColors(value ?? "").Plain;

    private static bool TryParseMedia(string source, out LegacyDialogMedia? media)
    {
        media = null;
        if (!source.StartsWith("m#", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = source.Split('#');
        if (parts.Length != 5 || parts[0].Length != 1 || parts[4].Length != 0)
            return false;

        // The 'na' and empty values are legitimate legacy placeholders.
        if (parts[1].Length == 0 || parts[1].Length > 255 ||
            parts[2].Length > 255 || parts[3].Length > 255)
            return false;

        media = new LegacyDialogMedia(parts[1], parts[2], parts[3]);
        return true;
    }

    private static string ReadLegacyPrefix(string text)
    {
        if (text.Length == 0) return "";
        var code = text[0];
        if (code is not ('n' or 'p' or 's' or 'c' or 'b' or 'h'))
            return "";

        if (text.Length >= 2 && char.IsAsciiDigit(text[1]))
        {
            // No original source evidence for n8/n9, p12 or s3:
            // preserve unrecognized directives instead of silently losing text.
            if (code is not ('n' or 'p') ||
                text[1] < '1' || text[1] > '7' ||
                (text.Length >= 3 && char.IsAsciiDigit(text[2])))
                return "";

            return text[..2];
        }

        // Bare single-letter prefixes are used in the Chinese original quest
        // scripts. ASCII words are not presentation controls.
        if (text.Length >= 2 && char.IsAsciiLetter(text[1]))
            return "";

        return text[..1];
    }



    private static (string Rich, string Plain) RenderColors(string source)
    {
        var rich = new StringBuilder(source.Length + 32);
        var plain = new StringBuilder(source.Length);
        var cursor = 0;
        var colored = false;

        foreach (Match match in ColorDirective.Matches(source))
        {
            var fragment = source[cursor..match.Index];
            rich.Append(fragment);
            plain.Append(fragment);

            if (colored)
            {
                rich.Append("[/color]");
                colored = false;
            }

            var hex = match.Groups[1].Value.ToUpperInvariant();
            // Legacy scripts use Color(0,0,0) as a return to the default
            // black text; close the previous span rather than nesting it.
            if (hex != "000000")
            {
                rich.Append("[color=#").Append(hex).Append(']');
                colored = true;
            }
            cursor = match.Index + match.Length;
        }

        var tail = source[cursor..];
        rich.Append(tail);
        plain.Append(tail);
        if (colored)
            rich.Append("[/color]");

        return (rich.ToString(), plain.ToString());
    }
}
