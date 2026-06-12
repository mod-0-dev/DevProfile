using DevProfile.Core;

namespace DevProfile.Cli;

/// <summary>Console rendering + interactive input helpers shared by the commands.</summary>
internal static class ConsoleUi
{
    public static void Line(string text = "") => Console.WriteLine(text);

    public static void Line(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    public static void Error(string text) => Line(ConsoleColor.Red, $"error: {text}");

    /// <summary>Provider/orchestrator log lines: failures ("!") stand out, the rest stay dim.</summary>
    public static void LogLine(string line) =>
        Line(line.Contains('!') ? ConsoleColor.DarkYellow : ConsoleColor.Gray, line);

    public static ConsoleColor ActionColor(PlanAction action) => action switch
    {
        PlanAction.Install => ConsoleColor.Green,
        PlanAction.Overwrite => ConsoleColor.Yellow,
        PlanAction.Merge => ConsoleColor.Cyan,
        PlanAction.Manual => ConsoleColor.Magenta,
        _ => ConsoleColor.DarkGray,
    };

    public static void RenderPlan(IReadOnlyList<PlanItem> items)
    {
        if (items.Count == 0)
        {
            Line("Nothing to do — the plan is empty.");
            return;
        }

        int wAction = items.Max(i => i.Action.ToString().Length);
        int wLabel = Math.Min(48, items.Max(i => i.Label.Length));
        int wStatus = items.Max(i => i.Status.Length);
        foreach (var i in items)
        {
            Console.ForegroundColor = ActionColor(i.Action);
            Console.Write($"  {i.Action.ToString().PadRight(wAction)}  ");
            Console.ResetColor();
            Console.Write($"{Truncate(i.Label, wLabel).PadRight(wLabel)}  {i.Status.PadRight(wStatus)}");
            if (!string.IsNullOrEmpty(i.Detail))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {i.Detail}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    public static string Summarize(IReadOnlyList<PlanItem> items)
    {
        int install = items.Count(i => i.Action == PlanAction.Install);
        int update = items.Count(i => i.Action is PlanAction.Overwrite or PlanAction.Merge);
        int skip = items.Count(i => i.Action == PlanAction.Skip);
        int manual = items.Count(i => i.Action == PlanAction.Manual);
        return $"{install} to install · {update} to update · {skip} already current"
               + (manual > 0 ? $" · {manual} manual" : "");
    }

    public static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N] ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().StartsWith('y');
    }

    /// <summary>Prompt for a passphrase without echoing it. Requires an interactive console.</summary>
    public static string PromptPassphrase(string prompt)
    {
        Console.Write($"{prompt}: ");
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; continue; }
            if (key.KeyChar != '\0') buffer.Append(key.KeyChar);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
