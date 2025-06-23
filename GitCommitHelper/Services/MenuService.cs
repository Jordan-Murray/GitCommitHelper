namespace GitCommitHelper.Services;

using Spectre.Console;

public static class MenuService
{
    public static T Prompt<T>(string title, IEnumerable<T> choices, Func<T, string> displaySelector) where T : notnull
    {
        var promptTitle = $"{title} [grey](Press ESC to go back)[/]";

        var prompt = new SelectionPrompt<T>()
                    .Title(promptTitle)
                    .PageSize(30)
                    .UseConverter(displaySelector)
                    .MoreChoicesText("[grey](Move up and down to reveal more choices)[/]")
                    .AddChoices(choices);

        return AnsiConsole.Prompt(prompt);
    }

    public static void RenderTitle(string text)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText(text).Centered().Color(Color.Purple));
        AnsiConsole.WriteLine();
    }

    public static void RenderPanel(string header, string content)
    {
        var panel = new Panel(content.EscapeMarkup())
            .Header(header)
            .Border(BoxBorder.Rounded)
            .Padding(1, 1);

        AnsiConsole.Write(panel);
    }

    public static void RenderException(Exception ex)
    {
        AnsiConsole.MarkupLine("[red]An unexpected error occurred:[/]");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }

    public static void RenderWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]{message.EscapeMarkup()}[/]");
    }

    public static async Task<T> Status<T>(string statusText, Func<Task<T>> action)
    {
        return await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(statusText, async ctx => await action());
    }
}