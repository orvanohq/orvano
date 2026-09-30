using System.Text;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Ast;

namespace Orvano.Messaging.Domain;

/// <summary>The three parts of an email template. Each is its own Liquid template.</summary>
/// <param name="Subject">The subject line.</param>
/// <param name="Html">The HTML part.</param>
/// <param name="Text">The text part, or null to derive it from the rendered HTML.</param>
internal sealed record TemplateSource(string Subject, string Html, string? Text);

/// <summary>What is wrong with a template (spec 0009, AC-10): the part, the line when it is known, and the problem.</summary>
/// <param name="Part"><c>subject</c>, <c>html</c>, or <c>text</c>.</param>
/// <param name="Line">The line in that part, from 1.</param>
/// <param name="Message">The problem, in words safe to show.</param>
internal sealed record TemplateProblem(string Part, int? Line, string Message)
{
    /// <summary>The problem <c>detail</c>, for example <c>html: line 4: unknown variable action_ur</c>.</summary>
    public string Detail => Line is { } line ? $"{Part}: line {line}: {Message}" : $"{Part}: {Message}";
}

/// <summary>A template that parsed and passed <see cref="EmailTemplates.TryCompile"/>, ready to render.</summary>
internal sealed record CompiledTemplate(IFluidTemplate Subject, IFluidTemplate Html, IFluidTemplate? Text);

/// <summary>A render that failed, and in which part.</summary>
internal sealed class EmailRenderException(string part, TemplateRenderException inner) : Exception(inner.Message, inner)
{
    public TemplateProblem Problem { get; } = new(part, null, inner.Message);
}

/// <summary>
/// The rules of an email template (spec 0009, AC-10, AC-11): its size limits, what Liquid it may use, and how it
/// becomes an email.
/// </summary>
internal static partial class EmailTemplates
{
    public const int MaxSubjectLength = 255;

    /// <summary>100 KB, in UTF-8 bytes, as the table checks it.</summary>
    public const int MaxPartBytes = 102_400;

    public const string SubjectPart = "subject";
    public const string HtmlPart = "html";
    public const string TextPart = "text";

    /// <summary>
    /// The sizes (AC-10's 400s). The subject is trimmed, and a blank text part means none. Returns the content as
    /// it is stored, or the error, which starts with the input's name and a colon.
    /// </summary>
    public static bool TryNormalize(string? subject, string? html, string? text, out TemplateSource source, out string error)
    {
        source = new TemplateSource(subject?.Trim() ?? "", html ?? "", string.IsNullOrWhiteSpace(text) ? null : text);
        error = source switch
        {
            { Subject.Length: 0 } => "subject: Enter a subject.",
            { Subject.Length: > MaxSubjectLength } => $"subject: Use at most {MaxSubjectLength} characters.",
            _ when Encoding.UTF8.GetByteCount(source.Html) > MaxPartBytes => "html: Use at most 100 KB.",
            { Text: { } given } when Encoding.UTF8.GetByteCount(given) > MaxPartBytes => "text: Use at most 100 KB.",
            _ => "",
        };
        return error.Length == 0;
    }

    /// <summary>
    /// Parses each part and checks what it uses against <paramref name="info"/>: only that template's variables,
    /// names the template itself creates, the allowed filters, and no <c>include</c> or <c>render</c>.
    /// </summary>
    public static bool TryCompile(EmailTemplateInfo info, TemplateSource source, out CompiledTemplate compiled, out TemplateProblem problem)
    {
        compiled = null!;
        if (!TryCompilePart(info, SubjectPart, source.Subject, out var subject, out problem)
            || !TryCompilePart(info, HtmlPart, source.Html, out var html, out problem))
        {
            return false;
        }

        IFluidTemplate? text = null;
        if (source.Text is not null && !TryCompilePart(info, TextPart, source.Text, out text, out problem)) return false;

        compiled = new CompiledTemplate(subject, html, text);
        return true;
    }

    /// <summary>
    /// Renders the email. The HTML part is encoded, the subject and text are not. Control characters in the subject
    /// become spaces and it is cut to <see cref="MaxSubjectLength"/> (AC-11); a missing text part is derived from
    /// the rendered HTML.
    /// </summary>
    /// <exception cref="EmailRenderException">A part reached a limit or failed to render.</exception>
    public static async ValueTask<EmailContent> RenderAsync(CompiledTemplate template, TemplateValues values)
    {
        var subject = InvitationEmailContent.Subject(await RenderPartAsync(SubjectPart, template.Subject, values, html: false));
        var html = await RenderPartAsync(HtmlPart, template.Html, values, html: true);
        var text = template.Text is null ? HtmlToText.Convert(html) : await RenderPartAsync(TextPart, template.Text, values, html: false);
        return new EmailContent(subject, html, text);
    }

    /// <summary>
    /// Everything <c>update</c>, <c>preview</c>, and <c>test</c> check after the sizes: the template compiles and
    /// renders with <paramref name="sample"/> within the limits. Returns the rendered email, or the problem.
    /// </summary>
    public static async ValueTask<(EmailContent? Rendered, TemplateProblem? Problem)> CheckAsync(
        EmailTemplateInfo info, TemplateSource source, TemplateValues sample)
    {
        if (!TryCompile(info, source, out var compiled, out var problem)) return (null, problem);
        try
        {
            return (await RenderAsync(compiled, sample), null);
        }
        catch (EmailRenderException e)
        {
            return (null, e.Problem);
        }
    }

    private static async ValueTask<string> RenderPartAsync(string part, IFluidTemplate template, TemplateValues values, bool html)
    {
        try
        {
            return await LiquidEngine.RenderAsync(template, values, html);
        }
        catch (TemplateRenderException e)
        {
            throw new EmailRenderException(part, e);
        }
    }

    private static bool TryCompilePart(EmailTemplateInfo info, string part, string source, out IFluidTemplate template, out TemplateProblem problem)
    {
        problem = null!;
        if (!LiquidEngine.TryParse(source, out template, out var line, out var message))
        {
            problem = new TemplateProblem(part, line, message);
            return false;
        }

        var usage = new UsageCheck(info);
        usage.VisitTemplate(template);
        if (usage.Problem is { } found)
        {
            problem = new TemplateProblem(part, LineOf(source, found.Pattern, found.Occurrence), found.Message);
            return false;
        }

        template = LiquidEngine.Harden(template);
        return true;
    }

    [GeneratedRegex(@"\{\{.*?\}\}|\{%.*?%\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex(@"'[^']*'|""[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex Quoted();

    /// <summary>
    /// The line of match number <paramref name="occurrence"/> (from 0) of <paramref name="pattern"/> inside the
    /// Liquid tags of <paramref name="source"/>, quoted text aside; the first match's line when there are fewer.
    /// </summary>
    private static int? LineOf(string source, string pattern, int occurrence)
    {
        int? first = null;
        foreach (Match tag in Tag().Matches(source))
        {
            var code = Quoted().Replace(tag.Value, quoted => new string(' ', quoted.Length));
            foreach (Match match in Regex.Matches(code, pattern, RegexOptions.CultureInvariant))
            {
                var line = source.AsSpan(0, tag.Index + match.Index).Count('\n') + 1;
                first ??= line;
                if (occurrence-- == 0) return line;
            }
        }

        return first;
    }

    /// <summary>
    /// Walks a parsed template and keeps the first thing it may not use. Names made by <c>assign</c> and
    /// <c>capture</c> count from where they are made; a loop's variable and <c>forloop</c> or <c>tablerowloop</c>
    /// count inside the loop.
    /// </summary>
    private sealed class UsageCheck(EmailTemplateInfo info) : AstVisitor
    {
        private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
        private readonly List<string> _loops = [];

        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

        /// <summary>What is wrong, a pattern that finds such a use in the source, and which match of it this is.</summary>
        public (string Message, string Pattern, int Occurrence)? Problem { get; private set; }

        protected override Expression VisitMemberExpression(MemberExpression memberExpression)
        {
            var segments = memberExpression.Segments;
            if (segments[0] is IdentifierSegment root)
            {
                var named = segments.TakeWhile(s => s is IdentifierSegment).Cast<IdentifierSegment>().Select(s => s.Identifier).ToList();
                var path = string.Join('.', named);
                var whole = named.Count == segments.Count;
                // Where a name is declared (for x, assign x) is not a use of it.
                var pattern = @"(?<![\w.])(?<!\b(?:for|tablerow|assign|capture|increment|decrement)\s+)" + Regex.Escape(path) + (whole ? @"(?![\w.\[])" : @"\[");
                var occurrence = Count(pattern);
                if (!_assigned.Contains(root.Identifier) && !_loops.Contains(root.Identifier) && !(whole && info.Allows(path)))
                    Problem ??= ($"unknown variable {(whole ? path : path + "[...]")}", pattern, occurrence);
            }

            return base.VisitMemberExpression(memberExpression);
        }

        protected override Expression VisitFilterExpression(FilterExpression filterExpression)
        {
            var pattern = @"\|\s*" + Regex.Escape(filterExpression.Name) + @"(?![\w])";
            var occurrence = Count(pattern);
            if (!LiquidEngine.AllowedFilters.Contains(filterExpression.Name))
                Problem ??= ($"unknown filter {filterExpression.Name}", pattern, occurrence);
            return base.VisitFilterExpression(filterExpression);
        }

        private int Count(string pattern)
        {
            var count = _seen.GetValueOrDefault(pattern);
            _seen[pattern] = count + 1;
            return count;
        }

        protected override Statement VisitIncludeStatement(IncludeStatement includeStatement)
        {
            Problem ??= ("include is not allowed", @"\{%-?\s*include(?![\w])", 0);
            return includeStatement;
        }

        protected override Statement VisitRenderStatement(RenderStatement renderStatement)
        {
            Problem ??= ("render is not allowed", @"\{%-?\s*render(?![\w])", 0);
            return renderStatement;
        }

        protected override Statement VisitAssignStatement(AssignStatement assignStatement)
        {
            var visited = base.VisitAssignStatement(assignStatement);
            _assigned.Add(assignStatement.Identifier);
            return visited;
        }

        protected override Statement VisitCaptureStatement(CaptureStatement captureStatement)
        {
            var visited = base.VisitCaptureStatement(captureStatement);
            _assigned.Add(captureStatement.Identifier);
            return visited;
        }

        protected override Statement VisitForStatement(ForStatement forStatement)
        {
            _loops.Add(forStatement.Identifier);
            _loops.Add("forloop");
            var visited = base.VisitForStatement(forStatement);
            _loops.RemoveRange(_loops.Count - 2, 2);
            return visited;
        }

        protected override Statement VisitTableRowStatement(TableRowStatement tableRowStatement)
        {
            _loops.Add(tableRowStatement.Identifier);
            _loops.Add("tablerowloop");
            var visited = base.VisitTableRowStatement(tableRowStatement);
            _loops.RemoveRange(_loops.Count - 2, 2);
            return visited;
        }
    }
}
