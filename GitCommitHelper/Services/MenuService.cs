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

    public static string BrowseForDirectory(string title, string? startPath = null)
    {
        var currentPath = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        while (true)
        {
            try
            {
                var directories = Directory.GetDirectories(currentPath)
                    .Select(d => new DirectoryInfo(d).Name)
                    .OrderBy(d => d)
                    .ToList();

                var choices = new List<string> { "[Use this directory]" };
                
                // Add drive switching option on Windows
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    choices.Add("🔄 Switch Drive");
                }
                
                if (Directory.GetParent(currentPath) != null)
                {
                    choices.Add(".. (Go up one level)");
                }

                choices.AddRange(directories);

                var displayPath = currentPath.Length > 60 
                    ? "..." + currentPath.Substring(currentPath.Length - 57)
                    : currentPath;

                var selection = Prompt($"{title}\n[grey]Current: {displayPath.EscapeMarkup()}[/]", choices, c => c.EscapeMarkup());

                if (selection == "[Use this directory]")
                {
                    return currentPath;
                }
                else if (selection == "🔄 Switch Drive")
                {
                    var selectedDrive = SelectDrive();
                    if (selectedDrive != null)
                    {
                        currentPath = selectedDrive;
                    }
                }
                else if (selection == ".. (Go up one level)")
                {
                    var parent = Directory.GetParent(currentPath);
                    if (parent != null)
                    {
                        currentPath = parent.FullName;
                    }
                }
                else
                {
                    // Find the original directory name (unescaped) to use for path construction
                    var originalDirName = directories.FirstOrDefault(d => d.EscapeMarkup() == selection) ?? selection;
                    currentPath = Path.Combine(currentPath, originalDirName);
                }
            }
            catch (UnauthorizedAccessException)
            {
                RenderWarning("Access denied to this directory. Please choose another location.");
                var parent = Directory.GetParent(currentPath);
                if (parent != null)
                {
                    currentPath = parent.FullName;
                }
                else
                {
                    currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
            }
            catch (Exception ex)
            {
                RenderWarning($"Error accessing directory: {ex.Message}");
                currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
    }

    private static string? SelectDrive()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new { 
                    Root = d.RootDirectory.FullName,
                    Display = $"{d.RootDirectory.FullName.TrimEnd('\\')} ({d.DriveType})" + 
                             (d.DriveType == DriveType.Fixed ? $" - {d.AvailableFreeSpace / (1024 * 1024 * 1024):F1} GB free" : "")
                })
                .ToList();

            if (!drives.Any())
            {
                RenderWarning("No accessible drives found.");
                return null;
            }

            var driveChoices = drives.Select(d => d.Display).ToList();
            driveChoices.Insert(0, "❌ Cancel");

            var selection = Prompt("Select a drive", driveChoices, c => c);

            if (selection == "❌ Cancel")
            {
                return null;
            }

            var selectedDrive = drives.FirstOrDefault(d => d.Display == selection);
            return selectedDrive?.Root;
        }
        catch (Exception ex)
        {
            RenderWarning($"Error accessing drives: {ex.Message}");
            return null;
        }
    }
}