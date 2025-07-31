using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text;

namespace GitCommitHelper.Services;

public abstract record MenuChoice;
public record RepositoryChoice(GitService.GitRepository Repository) : MenuChoice;
public record ActionChoice(string Text) : MenuChoice;

public class App
{
    private const int ContextLimit = 100000; // Updated for 128k token models
    private readonly LlmService _llm;
    private readonly IConfiguration _config;

    private static readonly ActionChoice ScanChoice = new("[Scan a different directory]");
    private static readonly ActionChoice ExitChoice = new("[Exit]");

    public App(LlmService llm, IConfiguration config)
    {
        _llm = llm;
        _config = config;
    }

    public async Task RunAsync()
    {
        try
        {
            while (true)
            {
                MenuService.RenderTitle("Git Helper");
                var searchRoot = GetRepositorySearchRoot();
                var repos = await MenuService.Status<List<GitService.GitRepository>>("Scanning for git repositories...",
                    () => Task.FromResult(GitService.FindRepositories(searchRoot)));

                if (!repos.Any())
                {
                    MenuService.RenderWarning($"No git repositories found in '{searchRoot}'.");
                    if (!MenuService.ConfirmAction("Scan a different directory?"))
                    {
                        break;
                    }
                    continue;
                }

                var choices = new List<MenuChoice>();
                choices.AddRange(repos.Select(r => new RepositoryChoice(r)));
                choices.Add(ScanChoice);
                choices.Add(ExitChoice);

                var selectedChoice = MenuService.Prompt("Select a repository", choices, choice =>
                {
                    return choice switch
                    {
                        // For a repository, format it with color and escape the name/path
                        RepositoryChoice rc => $"{rc.Repository.Name.EscapeMarkup()} [grey]({rc.Repository.Path.EscapeMarkup()})[/]",
                        // For an action, just escape the text to display it literally
                        ActionChoice ac => ac.Text.EscapeMarkup(),
                        // A fallback, just in case
                        _ => "Unknown option"
                    };
                });

                if (selectedChoice == ExitChoice)
                {
                    break;
                }
                if (selectedChoice == ScanChoice)
                {
                    continue;
                }

                if (selectedChoice is RepositoryChoice repositoryChoice)
                {
                    await ProcessRepositoryActions(repositoryChoice.Repository.Path);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLoggingService.LogError(ex);
            MenuService.RenderException(ex);
        }
    }
    private async Task ProcessRepositoryActions(string repoPath)
    {
        while (true)
        {
            var actionChoices = new[]
            {
                "Generate commit message for staged files",
                "Generate commit message for unstaged files (selective)",
                "Generate PR description from commit",
                "Generate PR with preview mode and code review",
                "[Back to repository selection]"
            };

            var selectedAction = MenuService.Prompt("What would you like to do?", actionChoices, a => a.EscapeMarkup());

            if (selectedAction == "[Back to repository selection]")
            {
                break;
            }

            switch (selectedAction)
            {
                case "Generate commit message for staged files":
                    await ProcessStagedFilesAsync(repoPath);
                    break;
                case "Generate commit message for unstaged files (selective)":
                    await ProcessUnstagedFilesSelectiveAsync(repoPath);
                    break;
                case "Generate PR description from commit":
                    var selectedBranch = await SelectBranchAsync(repoPath);
                    if (selectedBranch != null)
                    {
                        await ProcessBranchAsync(repoPath, selectedBranch);
                    }
                    break;
                case "Generate PR with preview mode and code review":
                    var selectedBranchForPreview = await SelectBranchAsync(repoPath);
                    if (selectedBranchForPreview != null)
                    {
                        await ProcessBranchWithPreviewAsync(repoPath, selectedBranchForPreview);
                    }
                    break;
            }

            Console.WriteLine("\nPress any key to return to the action menu...");
            Console.ReadKey();
        }
    }

    private async Task<string?> SelectBranchAsync(string repoPath)
    {
        var branches = await MenuService.Status(
            "Fetching local branches...",
            () => GitService.GetLocalBranchesAsync(repoPath)
        );

        if (!branches.Any())
        {
            MenuService.RenderWarning("No local branches found in this repository.");
            return null;
        }

        return MenuService.Prompt("Select a branch to analyze", branches, b => b);
    }

    private async Task ProcessUnstagedFilesSelectiveAsync(string repoPath)
    {
        var unstagedFiles = await MenuService.Status(
            "Checking for unstaged changes...",
            () => GitService.GetUnstagedFilesAsync(repoPath)
        );

        if (!unstagedFiles.Any())
        {
            MenuService.RenderWarning("No unstaged (modified or new) files found.");
            return;
        }

        var selectedFiles = MenuService.PromptWithMultiSelect(
            "Select files to include in the commit message",
            unstagedFiles,
            file => file.Status == UnstagedStatus.Modified
                ? $"[yellow]MODIFIED[/] - {file.FilePath.EscapeMarkup()}"
                : $"[aqua]NEW FILE[/] - {file.FilePath.EscapeMarkup()}"
        );

        if (!selectedFiles.Any())
        {
            MenuService.RenderWarning("No files were selected. Aborting.");
            return;
        }

        var fullDiff = new StringBuilder();
        await MenuService.Status<object>("Generating diff for selected files...", async () =>
        {
            foreach (var file in selectedFiles)
            {
                var fileDiff = await GitService.GetDiffForFileAsync(repoPath, file);
                fullDiff.AppendLine(fileDiff);
            }
            return null!;
        });

        await GenerateCommitMessageFromDiffAsync(fullDiff.ToString(), "Analysis of Selected Unstaged Files");
    }

    private async Task ProcessBranchAsync(string repoPath, string branchName)
    {
        var baseBranch = _config["Settings:BaseBranchForPRs"] ?? "main";

        var commits = await MenuService.Status(
            $"Fetching commits for branch '{branchName}'...",
            () => GitService.GetCommitsForBranchAsync(repoPath, branchName, baseBranch)
        );

        if (!commits.Any())
        {
            MenuService.RenderWarning($"No unique commits found on branch '{branchName}' compared to '{baseBranch}'.");
            return;
        }

        var fullDiff = await GitService.GetBranchDiffAsync(repoPath, branchName, baseBranch);

        MenuService.RenderPanel($"Branch Details for '{branchName}'", $"Found [green]{commits.Count}[/] unique commits.\nFull diff character count: [blue]{fullDiff.Length}[/]");

        var selectedCommit = MenuService.Prompt("Select a commit to generate details for", commits, c => c);
        var commitHash = selectedCommit.Split(' ')[0];

        var commitDiff = await MenuService.Status(
            $"Getting diff for commit {commitHash}...",
            () => GitService.GetCommitDiffAsync(repoPath, commitHash)
        );

        var chunks = DiffAnalysisService.ChunkDiffContent(commitDiff);
        var primaryChunk = chunks.First();

        if (chunks.Count > 1)
        {
            MenuService.RenderPanel("Smart Analysis for Commit",
                $"Large commit detected. Using smart chunking.\n" +
                DiffAnalysisService.GetDiffSummary(chunks) +
                "\n[yellow]Note: PR details will be generated from the highest priority changes.[/]");
        }
        else if (commitDiff.Length > ContextLimit)
        {
            MenuService.RenderWarning($"Warning: The diff for this single commit is very large ({commitDiff.Length} chars) and may exceed the model's context window.");
        }

        try
        {
            var prDetails = await MenuService.Status(
                "🧠 Asking Gemma to generate PR details...",
                () => _llm.GetPrDetailsAsync(primaryChunk.Content)
            );

            MenuService.RenderPanel($"Generated Details for Commit {commitHash}", prDetails);

            if (chunks.Count > 1)
            {
                MenuService.RenderWarning($"Note: This PR description was generated from {primaryChunk.Name}. " +
                    $"There are {chunks.Count - 1} additional chunks that weren't analyzed.");

                if (MenuService.ConfirmAction("Would you like to see a code review of all changes?"))
                {
                    await ProcessCommitCodeReviewAsync(chunks);
                }
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Token limit exceeded"))
        {
            MenuService.RenderWarning("Token limit exceeded. Trying with smaller chunk...");

            if (chunks.Count > 1)
            {
                var smallerChunk = chunks.Skip(1).First();
                try
                {
                    var prDetails = await _llm.GetPrDetailsAsync(smallerChunk.Content);
                    MenuService.RenderPanel($"Generated PR Details (from {smallerChunk.Name})", prDetails);
                }
                catch (Exception)
                {
                    MenuService.RenderWarning("Unable to generate PR details - commit is too large even after chunking. Try using 'Preview Mode' for better handling of large commits.");
                }
            }
            else
            {
                MenuService.RenderWarning("Unable to generate PR details - commit is too large. Try using 'Preview Mode' for better handling of large commits.");
            }
        }
        catch (Exception ex)
        {
            MenuService.RenderWarning($"Error generating PR details: {ex.Message}");
        }
    }

    private async Task ProcessStagedFilesAsync(string repoPath)
    {
        var hasStagedChanges = await MenuService.Status(
            "Checking for staged changes...",
            () => GitService.HasStagedChangesAsync(repoPath)
        );

        if (!hasStagedChanges)
        {
            MenuService.RenderWarning("No staged changes found. Please stage your changes first with 'git add'.");
            return;
        }

        var stagedDiff = await MenuService.Status(
            "Getting staged changes diff...",
            () => GitService.GetStagedDiffAsync(repoPath)
        );

        // Use the new refactored method
        await GenerateCommitMessageFromDiffAsync(stagedDiff, "Analysis for Staged Changes");
    }

    private async Task GenerateCommitMessageFromDiffAsync(string diffContent, string analysisTitle)
    {
        if (string.IsNullOrWhiteSpace(diffContent))
        {
            MenuService.RenderWarning("No changes to analyze.");
            return;
        }

        var chunks = DiffAnalysisService.ChunkDiffContent(diffContent);
        var primaryChunk = chunks.First();

        if (chunks.Count > 1)
        {
            MenuService.RenderPanel($"Smart {analysisTitle}",
                $"Large diff detected. Using smart chunking.\n" +
                DiffAnalysisService.GetDiffSummary(chunks) +
                "\n[yellow]Note: Commit message will be generated from the highest priority changes.[/]");
        }

        try
        {
            var commitMessage = await MenuService.Status(
                "🧠 Generating commit message...",
                () => _llm.GetCommitMessageAsync(primaryChunk.Content)
            );

            MenuService.RenderPanel("Generated Commit Message", commitMessage);

            if (chunks.Count > 1)
            {
                MenuService.RenderWarning($"Note: This message was generated from {primaryChunk.Name}. " +
                                           $"There are {chunks.Count - 1} additional chunks that weren't analyzed.");

                // You could add the code review option back here if you wanted
            }
        }
        catch (Exception ex) when (ex.Message.Contains("context") || ex.Message.Contains("token"))
        {
            MenuService.RenderWarning("Token limit exceeded. The changes are too large to analyze, even after chunking.");
        }
    }

    private async Task ProcessStagedCodeReviewAsync(List<DiffChunk> chunks)
    {
        foreach (var chunk in chunks.Take(3)) // Limit to first 3 chunks to avoid overwhelming
        {
            try
            {
                var review = await MenuService.Status(
                    $"🔍 Reviewing {chunk.Name}...",
                    () => _llm.GetCodeReviewAsync(chunk.Content)
                );

                MenuService.RenderPanel($"Code Review - {chunk.Name}", review);

                if (chunk != chunks.Take(3).Last() && !MenuService.ConfirmAction("Continue to next chunk?"))
                    break;
            }
            catch (Exception ex)
            {
                MenuService.RenderWarning($"Error reviewing {chunk.Name}: {ex.Message}");
            }
        }
    }

    private async Task ProcessCommitCodeReviewAsync(List<DiffChunk> chunks)
    {
        foreach (var chunk in chunks.Take(3)) // Limit to first 3 chunks to avoid overwhelming
        {
            try
            {
                var review = await MenuService.Status(
                    $"🔍 Reviewing {chunk.Name}...",
                    () => _llm.GetCodeReviewAsync(chunk.Content)
                );

                MenuService.RenderPanel($"Code Review - {chunk.Name}", review);

                if (chunk != chunks.Take(3).Last() && !MenuService.ConfirmAction("Continue to next chunk?"))
                    break;
            }
            catch (Exception ex)
            {
                MenuService.RenderWarning($"Error reviewing {chunk.Name}: {ex.Message}");
            }
        }
    }

    private async Task ProcessBranchWithPreviewAsync(string repoPath, string branchName)
    {
        var baseBranch = _config["Settings:BaseBranchForPRs"] ?? "main";

        var commits = await MenuService.Status(
            $"Fetching commits for branch '{branchName}'...",
            () => GitService.GetCommitsForBranchAsync(repoPath, branchName, baseBranch)
        );

        if (!commits.Any())
        {
            MenuService.RenderWarning($"No unique commits found on branch '{branchName}' compared to '{baseBranch}'.");
            return;
        }

        var fullDiff = await GitService.GetBranchDiffAsync(repoPath, branchName, baseBranch);
        var chunks = DiffAnalysisService.ChunkDiffContent(fullDiff);

        MenuService.RenderPanel($"Smart Analysis for '{branchName}'",
            $"Found [green]{commits.Count}[/] unique commits.\n" +
            DiffAnalysisService.GetDiffSummary(chunks));

        var selectedCommit = MenuService.Prompt("Select a commit to analyze", commits, c => c);
        var commitHash = selectedCommit.Split(' ')[0];

        var commitDiff = await MenuService.Status(
            $"Getting diff for commit {commitHash}...",
            () => GitService.GetCommitDiffAsync(repoPath, commitHash)
        );

        var commitChunks = DiffAnalysisService.ChunkDiffContent(commitDiff);
        var primaryChunk = commitChunks.First();

        if (!DiffAnalysisService.IsWithinContextLimit(primaryChunk.Content))
        {
            MenuService.RenderWarning($"Large diff detected. Using smart chunking to analyze the most important changes first.");
        }

        var prDetailsTask = MenuService.Status(
            "🧠 Generating PR details...",
            () => _llm.GetPrDetailsAsync(primaryChunk.Content)
        );

        var codeReviewTask = MenuService.Status(
            "🔍 Performing code review...",
            () => _llm.GetCodeReviewAsync(primaryChunk.Content)
        );

        await Task.WhenAll(prDetailsTask, codeReviewTask);

        var prDetails = await prDetailsTask;
        var codeReview = await codeReviewTask;

        MenuService.Clear();
        MenuService.RenderTitle($"Preview for {commitHash}");
        MenuService.RenderPreviewMode(
            TruncateForDisplay(primaryChunk.Content, 2000),
            prDetails,
            codeReview
        );

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey();

        if (commitChunks.Count > 1)
        {
            if (MenuService.ConfirmAction($"Would you like to analyze the remaining {commitChunks.Count - 1} chunks?"))
            {
                await ProcessRemainingChunksAsync(commitChunks.Skip(1).ToList());
            }
        }
    }

    private async Task ProcessRemainingChunksAsync(List<DiffChunk> remainingChunks)
    {
        foreach (var chunk in remainingChunks)
        {
            MenuService.RenderPanel($"Analyzing {chunk.Name}", "Processing additional chunk...");

            var review = await _llm.GetCodeReviewAsync(chunk.Content);
            MenuService.RenderPanel($"Code Review - {chunk.Name}", review);

            if (!MenuService.ConfirmAction("Continue to next chunk?"))
                break;
        }
    }

    private static string TruncateForDisplay(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content.Substring(0, maxLength) + "\n\n... [Content truncated for display] ...";
    }

    private string GetRepositorySearchRoot()
    {
        var configuredRoot = _config["Settings:RepositorySearchRoot"];

        if (!string.IsNullOrEmpty(configuredRoot) && Directory.Exists(configuredRoot))
        {
            var useConfigured = MenuService.Prompt(
                 $"Use configured repository root or browse for a different one?",
                 new[] { $"Use configured: {configuredRoot.EscapeMarkup()}", "Browse for different directory" },
                 choice => choice
             );

            if (useConfigured.StartsWith("Use configured:"))
            {
                return configuredRoot;
            }
        }

        return MenuService.BrowseForDirectory(
            "Select the root directory to search for Git repositories",
            configuredRoot
        );
    }
}