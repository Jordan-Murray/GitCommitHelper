using Microsoft.Extensions.Configuration;

namespace GitCommitHelper.Services;

public class App
{
    private const int ContextLimit = 15000;
    private readonly LlmService _llm;
    private readonly IConfiguration _config;

    public App(LlmService llm, IConfiguration config)
    {
        _llm = llm;
        _config = config;
    }

    public async Task RunAsync()
    {
        try
        {
            MenuService.RenderTitle("Git Helper");

            var searchRoot = GetRepositorySearchRoot();
            var repos = await MenuService.Status("Scanning for git repositories...",
                () => Task.FromResult(GitService.FindRepositories(searchRoot)));

            if (!repos.Any())
            {
                MenuService.RenderWarning($"No git repositories found in '{searchRoot}'.");
                MenuService.RenderWarning("You can choose a different directory by selecting 'Browse for repository root' from the main menu.");
                return;
            }

            var selectedRepo = MenuService.Prompt("Select a repository", repos,
                repo => $"{repo.Name} [grey]({repo.Path})[/]");

            var actionChoices = new[]
            {
                "Generate commit message for staged files",
                "Generate PR description from commit"
            };

            var selectedAction = MenuService.Prompt("What would you like to do?", actionChoices, a => a);

            if (selectedAction == "Generate commit message for staged files")
            {
                await ProcessStagedFilesAsync(selectedRepo.Path);
            }
            else
            {
                var selectedBranch = await SelectBranchAsync(selectedRepo.Path);
                if (selectedBranch != null)
                {
                    await ProcessBranchAsync(selectedRepo.Path, selectedBranch);
                }
            }
        }
        catch (Exception ex)
        {
            MenuService.RenderException(ex);
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

        if (commitDiff.Length > ContextLimit)
        {
            MenuService.RenderWarning($"Warning: The diff for this single commit is very large ({commitDiff.Length} chars) and may exceed the model's context window.");
        }

        var prDetails = await MenuService.Status(
            "🧠 Asking Gemma to generate PR details...",
            () => _llm.GetPrDetailsAsync(commitDiff)
        );

        MenuService.RenderPanel($"Generated Details for Commit {commitHash}", prDetails);
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

        if (stagedDiff.Length > ContextLimit)
        {
            MenuService.RenderWarning($"Warning: The staged diff is very large ({stagedDiff.Length} chars) and may exceed the model's context window.");
        }

        var commitMessage = await MenuService.Status(
            "🧠 Generating commit message...",
            () => _llm.GetCommitMessageAsync(stagedDiff)
        );

        MenuService.RenderPanel("Generated Commit Message", commitMessage);
    }

    private string GetRepositorySearchRoot()
    {
        var configuredRoot = _config["Settings:RepositorySearchRoot"];
        
        if (!string.IsNullOrEmpty(configuredRoot) && Directory.Exists(configuredRoot))
        {
            var useConfigured = MenuService.Prompt(
                $"Use configured repository root or browse for a different one?",
                new[] { $"Use configured: {configuredRoot}", "Browse for different directory" },
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