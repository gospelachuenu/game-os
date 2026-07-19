using System.Text;

namespace ConsoleLibrary.Steam;

/// <summary>
/// Minimal recursive-descent parser for Valve's text KeyValue (VDF) format,
/// sufficient for reading Steam's appmanifest_*.acf files.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var position = 0;
        var root = new VdfNode();
        ParseObjectBody(text, ref position, root);
        return root;
    }

    private static void ParseObjectBody(string text, ref int position, VdfNode target)
    {
        while (true)
        {
            SkipWhitespaceAndComments(text, ref position);

            if (position >= text.Length)
            {
                return;
            }

            if (text[position] == '}')
            {
                position++;
                return;
            }

            var key = ReadQuotedString(text, ref position);
            SkipWhitespaceAndComments(text, ref position);

            if (position >= text.Length)
            {
                target.Children[key] = new VdfNode { Value = string.Empty };
                return;
            }

            if (text[position] == '{')
            {
                position++;
                var child = new VdfNode();
                ParseObjectBody(text, ref position, child);
                target.Children[key] = child;
            }
            else
            {
                var value = ReadQuotedString(text, ref position);
                target.Children[key] = new VdfNode { Value = value };
            }
        }
    }

    private static string ReadQuotedString(string text, ref int position)
    {
        SkipWhitespaceAndComments(text, ref position);

        if (position >= text.Length || text[position] != '"')
        {
            throw new FormatException($"Expected '\"' at position {position}.");
        }

        position++; // opening quote
        var sb = new StringBuilder();

        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
                sb.Append(text[position] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    var other => other,
                });
            }
            else
            {
                sb.Append(text[position]);
            }

            position++;
        }

        position++; // closing quote
        return sb.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else
            {
                return;
            }
        }
    }
}

public sealed class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.TryGetValue(key, out var child) ? child : null;

    public string? GetString(string key) => this[key]?.Value;
}
