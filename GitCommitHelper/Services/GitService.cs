using System.Diagnostics;

public static class GitService
{
    private static async Task<string> RunCommandAsync(string arguments, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git", // Assumes 'git' is in the system's PATH
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed with exit code {process.ExitCode}: {error}");
        }

        return output.Trim();
    }

    public record GitRepository(string Name, string Path);
    public static List<GitRepository> FindRepositories(string rootPath)
    {
        Console.WriteLine($"[GitService] Searching for repositories under '{rootPath}'...");
        if (!Directory.Exists(rootPath)) return new List<GitRepository>();

        return Directory.GetDirectories(rootPath, ".git", SearchOption.AllDirectories)
                        .Select(gitFolder =>
                        {
                            var repoPath = Path.GetDirectoryName(gitFolder)!;
                            var repoName = new DirectoryInfo(repoPath).Name;
                            return new GitRepository(repoName, repoPath);
                        })
                        .ToList();
    }

    public static Task<string> GetBranchDiffAsync(string repoPath, string branchName, string baseBranch)
    {
        return RunCommandAsync($"diff {baseBranch}...{branchName}", repoPath);
    }

    public static async Task<List<string>> GetCommitsForBranchAsync(string repoPath, string branchName, string baseBranch)
    {
        var arguments = $"log --pretty=format:\"%h - %an, %ar : %s\" {baseBranch}..{branchName}";
        var output = await RunCommandAsync(arguments, repoPath);
        return output.Split('\n').Select(c => c.Trim()).ToList();
    }

    public static Task<string> GetCommitDiffAsync(string repoPath, string commitHash)
    {
        return RunCommandAsync($"show {commitHash}", repoPath);
    }

    public static async Task<List<string>> GetLocalBranchesAsync(string repoPath)
    {
        var arguments = "branch --list --format=\"%(refname:short)\"";
        var output = await RunCommandAsync(arguments, repoPath);

        return output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(b => b.Trim())
                     .ToList();
    }
}