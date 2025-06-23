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

            var searchRoot = _config["Settings:RepositorySearchRoot"]!;
            var repos = await MenuService.Status("Scanning for git repositories...",
                () => Task.FromResult(GitService.FindRepositories(searchRoot)));

            if (!repos.Any())
            {
                MenuService.RenderWarning($"No git repositories found in '{searchRoot}'.");
                return;
            }

            var selectedRepo = MenuService.Prompt("Select a repository", repos,
                repo => $"{repo.Name} [grey]({repo.Path})[/]");

            var selectedBranch = await SelectBranchAsync(selectedRepo.Path);

            if (selectedBranch != null)
            {
                await ProcessBranchAsync(selectedRepo.Path, selectedBranch);
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
}