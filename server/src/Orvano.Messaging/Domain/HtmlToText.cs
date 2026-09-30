using System.Text;
using MimeKit.Text;

namespace Orvano.Messaging.Domain;

/// <summary>
/// Derives an email's text part from its HTML (spec 0009, stack additions): block elements and <c>&lt;br&gt;</c>
/// become new lines, a link becomes <c>text (url)</c>, a list item <c>- item</c>, a heading gets a blank line after,
/// <c>&lt;script&gt;</c>, <c>&lt;style&gt;</c>, and <c>&lt;head&gt;</c> are dropped, entities are decoded, and runs
/// of blank lines collapse to one.
/// </summary>
internal static class HtmlToText
{
    /// <summary>The text form of <paramref name="html"/>.</summary>
    public static string Convert(string html)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        var pendingSpace = false;
        HtmlTagId? skipping = null;
        string? href = null;
        var linkStart = 0;

        void Flush(bool keepEmpty)
        {
            if (line.Length > 0 || keepEmpty) lines.Add(line.ToString());
            line.Clear();
            pendingSpace = false;
        }

        void Append(string text)
        {
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || c == ' ')
                {
                    pendingSpace = line.Length > 0;
                    continue;
                }

                if (pendingSpace) line.Append(' ');
                pendingSpace = false;
                line.Append(c);
            }
        }

        var tokenizer = new HtmlTokenizer(new StringReader(html)) { DecodeCharacterReferences = true };
        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken tag)
            {
                if (skipping is not null)
                {
                    if (tag.IsEndTag && tag.Id == skipping) skipping = null;
                    continue;
                }

                if (tag.Id is HtmlTagId.Script or HtmlTagId.Style or HtmlTagId.Head)
                {
                    if (!tag.IsEndTag && !tag.IsEmptyElement) skipping = tag.Id;
                }
                else if (tag.Id == HtmlTagId.Br)
                {
                    Flush(keepEmpty: true);
                }
                else if (tag.Id == HtmlTagId.A)
                {
                    if (!tag.IsEndTag)
                    {
                        href = tag.Attributes.FirstOrDefault(a => a.Id == HtmlAttributeId.Href)?.Value?.Trim();
                        linkStart = line.Length;
                    }
                    else if (href is not null)
                    {
                        var text = linkStart <= line.Length ? line.ToString(linkStart, line.Length - linkStart).Trim() : "";
                        if (href.Length > 0 && !href.StartsWith('#') && !string.Equals(text, href, StringComparison.Ordinal))
                        {
                            pendingSpace = false;
                            line.Append(text.Length > 0 ? $" ({href})" : href);
                        }

                        href = null;
                    }
                }
                else if (IsHeading(tag.Id))
                {
                    Flush(keepEmpty: false);
                    if (tag.IsEndTag) lines.Add("");
                }
                else if (tag.Id == HtmlTagId.LI)
                {
                    Flush(keepEmpty: false);
                    if (!tag.IsEndTag) line.Append("- ");
                }
                else if (IsBlock(tag.Id))
                {
                    Flush(keepEmpty: false);
                    // A link cut by a block keeps its text; the address is dropped with the link.
                    linkStart = 0;
                }
            }
            else if (skipping is null && token is HtmlDataToken data and not HtmlScriptDataToken and not HtmlCDataToken)
            {
                Append(data.Data);
            }
        }

        Flush(keepEmpty: false);

        var text2 = new StringBuilder();
        var blank = true; // drops leading blank lines
        foreach (var l in lines.Select(l => l.TrimEnd()))
        {
            if (l.Length == 0)
            {
                if (!blank) text2.Append('\n');
                blank = true;
                continue;
            }

            text2.Append(l).Append('\n');
            blank = false;
        }

        return text2.ToString().TrimEnd('\n');
    }

    private static bool IsHeading(HtmlTagId id) =>
        id is HtmlTagId.H1 or HtmlTagId.H2 or HtmlTagId.H3 or HtmlTagId.H4 or HtmlTagId.H5 or HtmlTagId.H6;

    private static bool IsBlock(HtmlTagId id) => id is
        HtmlTagId.P or HtmlTagId.Div or HtmlTagId.Table or HtmlTagId.TR or HtmlTagId.TD or HtmlTagId.TH or HtmlTagId.UL or HtmlTagId.OL
        or HtmlTagId.BlockQuote or HtmlTagId.Pre or HtmlTagId.HR or HtmlTagId.Section or HtmlTagId.Article or HtmlTagId.Header
        or HtmlTagId.Footer or HtmlTagId.Body or HtmlTagId.Html or HtmlTagId.Title or HtmlTagId.Address or HtmlTagId.DL or HtmlTagId.DT
        or HtmlTagId.DD or HtmlTagId.Figure or HtmlTagId.FigCaption or HtmlTagId.Main or HtmlTagId.Nav or HtmlTagId.Aside;
}
