using System.Globalization;
using System.Text.Encodings.Web;
using Fluid;
using Fluid.Ast;
using Fluid.Values;
using Microsoft.Extensions.FileProviders;

namespace Orvano.Messaging.Domain;

/// <summary>Which limit a render ran into (spec 0009, AC-10 and AC-11).</summary>
internal enum TemplateLimit
{
    /// <summary>More than <see cref="LiquidEngine.MaxSteps"/> steps.</summary>
    Steps,

    /// <summary>A value or the output grew past <see cref="LiquidEngine.MaxChars"/>.</summary>
    Size,

    /// <summary>The filters worked through more than <see cref="LiquidEngine.MaxFilterWork"/> characters.</summary>
    Work,
}

/// <summary>A render that was stopped, or failed. Its message is a fixed sentence, safe to show.</summary>
internal sealed class TemplateRenderException : Exception
{
    public TemplateRenderException(TemplateLimit limit)
        : base(limit switch
        {
            TemplateLimit.Steps => $"rendering takes more than {LiquidEngine.MaxSteps:N0} steps",
            TemplateLimit.Size => "a value or the output grows past 1 MB",
            TemplateLimit.Work => "the filters do too much work",
            _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, null),
        })
    {
        Limit = limit;
    }

    public TemplateRenderException(Exception inner)
        : base("it can't be rendered with these values", inner)
    {
    }

    /// <summary>The limit that stopped the render, or null when it failed another way.</summary>
    public TemplateLimit? Limit { get; }
}

/// <summary>
/// Liquid as Orvano runs it (spec 0009, AC-11), through Fluid, the same way everywhere: no file access, only
/// <see cref="AllowedFilters"/>, only the variables given (as plain values, never .NET objects), at most
/// <see cref="MaxSteps"/> steps, and at most <see cref="MaxChars"/> for the output and for every value a filter
/// or a <c>capture</c> produces. With an HTML encoder every <c>{{ }}</c> output is encoded, whatever its type, and
/// the <c>raw</c> filter does not exist, so nothing can skip the encoding.
/// </summary>
internal static class LiquidEngine
{
    public const int MaxSteps = 100_000;

    /// <summary>1 MB, counted in characters.</summary>
    public const int MaxChars = 1024 * 1024;

    /// <summary>
    /// The characters all filter calls of one render may read and write in total. Without it a loop could run a
    /// slow filter over a 1 MB string a hundred thousand times.
    /// </summary>
    public const long MaxFilterWork = 16L * MaxChars;

    private const string BudgetKey = "orvano.budget";

    /// <summary>Liquid's standard filters, minus <c>raw</c>. Fluid's own extras (formatting, hashing, JSON) are left out.</summary>
    public static IReadOnlySet<string> AllowedFilters { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "abs", "append", "at_least", "at_most", "base64_decode", "base64_encode", "base64_url_safe_decode",
        "base64_url_safe_encode", "capitalize", "ceil", "compact", "concat", "date", "default", "divided_by",
        "downcase", "escape", "escape_once", "find", "find_index", "first", "floor", "has", "join", "last", "lstrip",
        "map", "minus", "modulo", "newline_to_br", "plus", "prepend", "reject", "remove", "remove_first",
        "remove_last", "replace", "replace_first", "replace_last", "reverse", "round", "rstrip", "size", "slice",
        "sort", "sort_natural", "split", "strip", "strip_html", "strip_newlines", "sum", "times", "truncate",
        "truncatewords", "uniq", "upcase", "url_decode", "url_encode", "where",
    };

    private static readonly FluidParser Parser = new();

    private static readonly TemplateOptions Options = CreateOptions();

    /// <summary>
    /// Parses <paramref name="source"/>. On failure <paramref name="line"/> is where (when Fluid says) and
    /// <paramref name="message"/> what, without the source text.
    /// </summary>
    public static bool TryParse(string source, out IFluidTemplate template, out int? line, out string message)
    {
        line = null;
        message = "";
        if (Parser.TryParse(source, out template, out var error)) return true;

        // Fluid answers "<what> at (<line>:<column>)", then echoes the source.
        var text = error.AsSpan();
        var end = text.IndexOfAny('\r', '\n');
        if (end >= 0) text = text[..end];
        var at = text.LastIndexOf(" at (");
        if (at >= 0)
        {
            var position = text[(at + 5)..];
            var colon = position.IndexOf(':');
            if (colon > 0 && int.TryParse(position[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                line = parsed;
                text = text[..at];
            }
        }

        message = text.Trim().ToString();
        return false;
    }

    /// <summary>
    /// The template that is actually run: every output is wrapped so a list or any other composite value is encoded
    /// like a string. Validate <paramref name="template"/> first; the wrapping is not part of what the author wrote.
    /// </summary>
    public static IFluidTemplate Harden(IFluidTemplate template) => new OutputGuard().VisitTemplate(template);

    /// <summary>Renders a hardened template. HTML output is encoded; a subject or a text part is not.</summary>
    /// <exception cref="TemplateRenderException">A limit was reached, or the render failed.</exception>
    public static async ValueTask<string> RenderAsync(IFluidTemplate template, TemplateValues values, bool html)
    {
        var context = new TemplateContext(Options);
        context.AmbientValues[BudgetKey] = new Budget();
        context.SetValue("project", Object(("name", values.ProjectName)));
        context.SetValue("user", Object(("email", values.UserEmail), ("name", values.UserName)));
        if (values.ActionUrl is not null) context.SetValue("action_url", new StringValue(values.ActionUrl));
        if (values.Code is not null) context.SetValue("code", new StringValue(values.Code));
        context.SetValue("expires_in_minutes", NumberValue.Create(values.ExpiresInMinutes));

        using var writer = new BoundedWriter();
        try
        {
            await template.RenderAsync(writer, html ? HtmlEncoder.Default : NullEncoder.Default, context);
        }
        catch (TemplateRenderException)
        {
            throw;
        }
        // Fluid reports the step limit with a plain InvalidOperationException.
        catch (InvalidOperationException e) when (e.Message.StartsWith("The maximum", StringComparison.Ordinal))
        {
            throw new TemplateRenderException(TemplateLimit.Steps);
        }
        // A filter given a value it can't work with (a bad base64 string, a number too large) throws its own way.
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new TemplateRenderException(e);
        }

        return writer.ToString();
    }

    private static DictionaryValue Object(params (string Name, string Value)[] members) =>
        new(new FluidValueDictionaryFluidIndexable(members.ToDictionary(m => m.Name, m => (FluidValue)new StringValue(m.Value), StringComparer.Ordinal)));

    private static TemplateOptions CreateOptions()
    {
        var options = new TemplateOptions
        {
            FileProvider = new NullFileProvider(),
            MaxSteps = MaxSteps,
            StrictFilters = true,
            CultureInfo = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc,
        };

        var standard = options.Filters.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        options.Filters.Clear();
        foreach (var name in AllowedFilters)
        {
            options.Filters.AddFilter(name, Guard(name, standard[name]));
        }

        options.Captured = static (_, value, _) =>
        {
            if (Measure(value) > MaxChars) throw new TemplateRenderException(TemplateLimit.Size);
            return new ValueTask<FluidValue>(value);
        };
        return options;
    }

    /// <summary>
    /// Wraps a filter with the size rules. A result that came back marked "already encoded" keeps that mark only
    /// from the two filters that do encode; from any other it could carry an unencoded argument, so it is encoded
    /// again on output.
    /// </summary>
    private static FilterDelegate Guard(string name, FilterDelegate filter) => async (input, arguments, context) =>
    {
        var read = Measure(input);
        // The two filters whose result can be far larger than both inputs are checked before they allocate it.
        if (name == "replace" && arguments.Count >= 2)
        {
            var search = Math.Max(1, arguments.At(0).ToStringValue().Length);
            if (read + ((read / search) + 1) * arguments.At(1).ToStringValue().Length > MaxChars)
                throw new TemplateRenderException(TemplateLimit.Size);
        }
        else if (name == "join" && input is ArrayValue list && arguments.Count >= 1)
        {
            if (read + (long)list.Values.Count * arguments.At(0).ToStringValue().Length > MaxChars)
                throw new TemplateRenderException(TemplateLimit.Size);
        }

        var result = await filter(input, arguments, context);
        var written = Measure(result);
        if (written > MaxChars) throw new TemplateRenderException(TemplateLimit.Size);
        if (context.AmbientValues[BudgetKey] is Budget budget && (budget.FilterWork += read + written) > MaxFilterWork)
            throw new TemplateRenderException(TemplateLimit.Work);

        return result is StringValue { Encode: false } text && name is not ("escape" or "escape_once")
            ? new StringValue(text.ToStringValue(), encode: true)
            : result;
    };

    /// <summary>The size of a value in characters; a list costs a little for every item on top of its text.</summary>
    private static long Measure(FluidValue value) => value switch
    {
        StringValue text => text.ToStringValue().Length,
        ArrayValue list => list.Values.Sum(item => 16 + Measure(item)),
        _ => 0,
    };

    private sealed class Budget
    {
        public long FilterWork;
    }

    /// <summary>
    /// Fluid writes a list by writing its items raw, without the encoder. This makes every output, and every value
    /// of a <c>cycle</c>, a single string first, so it is encoded like any other.
    /// </summary>
    private sealed class OutputGuard : AstRewriter
    {
        protected override Statement VisitOutputStatement(OutputStatement outputStatement) =>
            new OutputStatement(new EncodedOutput(outputStatement.Expression));

        protected override Statement VisitCycleStatement(CycleStatement cycleStatement) =>
            new CycleStatement(cycleStatement.Group, [.. cycleStatement.Values.Select(Expression (value) => new EncodedOutput(value))]);
    }

    private sealed class EncodedOutput(Expression inner) : Expression
    {
        public override async ValueTask<FluidValue> EvaluateAsync(TemplateContext context)
        {
            var value = await inner.EvaluateAsync(context);
            return value.Type is FluidValues.Array or FluidValues.Dictionary or FluidValues.Object
                ? new StringValue(value.ToStringValue(), encode: true)
                : value;
        }
    }

    /// <summary>Collects the output and stops the render once it passes <see cref="MaxChars"/>.</summary>
    private sealed class BoundedWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override void Write(char value)
        {
            Reserve(1);
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Reserve(count);
            base.Write(buffer, index, count);
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            Reserve(buffer.Length);
            base.Write(buffer);
        }

        public override void Write(string? value)
        {
            Reserve(value?.Length ?? 0);
            base.Write(value);
        }

        private void Reserve(int count)
        {
            if (GetStringBuilder().Length + (long)count > MaxChars) throw new TemplateRenderException(TemplateLimit.Size);
        }
    }
}
