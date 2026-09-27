namespace Orvano.Server.Install;

/// <summary>A terminal to ask on. There is none when the installer runs without <c>/dev/tty</c>.</summary>
internal interface IInstallTerminal
{
    /// <summary>Writes <paramref name="prompt"/> and reads one line; null when input ends.</summary>
    string? Ask(string prompt);
}

/// <summary>The console's own terminal, used when <c>install.sh</c> passes <c>-it</c>.</summary>
internal sealed class ConsoleTerminal : IInstallTerminal
{
    public string? Ask(string prompt)
    {
        Console.Out.Write(prompt);
        Console.Out.Flush();
        return Console.In.ReadLine();
    }
}

/// <summary>
/// The questions a run asks (spec 0006, AC-5, AC-7, AC-28). Without a terminal it never waits:
/// a value falls back to its flag or current value, and a yes or no question takes its default
/// unless <c>--yes</c> is given.
/// </summary>
internal sealed class InstallQuestions(IInstallTerminal? terminal, TextWriter output, bool yes)
{
    public bool HasTerminal => terminal is not null;

    /// <summary>
    /// Asks for a value until <paramref name="isValid"/> accepts it, offering <paramref name="current"/>
    /// as the default. With <c>--yes</c> a current value is taken without asking. Returns null when
    /// there is no terminal and no current value.
    /// </summary>
    public string? AskValue(string label, string? current, Func<string, bool> isValid, Func<string, string> invalid, bool allowEmpty = false)
    {
        if (terminal is null || (yes && current is not null)) return current;

        while (true)
        {
            var hint = string.IsNullOrEmpty(current) ? allowEmpty ? " (leave empty to skip)" : "" : $" [{current}]";
            var answer = terminal.Ask($"{label}{hint}: ");
            if (answer is null) return current;

            answer = answer.Trim();
            if (answer.Length == 0 && current is not null) return current;
            if (answer.Length == 0 && !allowEmpty) continue;
            if (isValid(answer)) return answer;
            output.WriteLine(invalid(answer));
        }
    }

    /// <summary>A yes or no question whose default is no.</summary>
    public bool Confirm(string question)
    {
        if (yes)
        {
            output.WriteLine($"{question} yes (--yes)");
            return true;
        }

        if (terminal is null)
        {
            output.WriteLine($"{question} no (no terminal; pass --yes to continue)");
            return false;
        }

        var answer = terminal.Ask($"{question} [y/N] ")?.Trim().ToLowerInvariant();
        return answer is "y" or "yes";
    }
}
