using System.Text;
using System.Text.RegularExpressions;

namespace GitCommitHelper.Services;

public static class DiffAnalysisService
{
    private const int MaxContextTokens = 100000; // Conservative estimate for 128k token limit
    private const int EstimatedTokensPerChar = 4; // Rough estimate for token-to-character ratio

    public static bool IsWithinContextLimit(string content)
    {
        return content.Length * EstimatedTokensPerChar < MaxContextTokens;
    }

    public static List<DiffChunk> ChunkDiffContent(string diffContent)
    {
        if (IsWithinContextLimit(diffContent))
        {
            return new List<DiffChunk> { new DiffChunk("Full Diff", diffContent, ChunkPriority.High) };
        }

        var chunks = new List<DiffChunk>();
        var files = SplitDiffByFiles(diffContent);

        foreach (var file in files)
        {
            var priority = GetFilePriority(file.FileName);
            
            if (IsWithinContextLimit(file.Content))
            {
                chunks.Add(new DiffChunk(file.FileName, file.Content, priority));
            }
            else
            {
                // Further chunk large files by methods/classes
                var subChunks = ChunkLargeFile(file);
                chunks.AddRange(subChunks);
            }
        }

        return chunks.OrderByDescending(c => c.Priority).ToList();
    }

    private static List<DiffFile> SplitDiffByFiles(string diffContent)
    {
        var files = new List<DiffFile>();
        var filePattern = @"^diff --git a/(.+) b/(.+)$";
        var lines = diffContent.Split('\n');
        
        DiffFile? currentFile = null;
        var currentContent = new StringBuilder();

        foreach (var line in lines)
        {
            var match = Regex.Match(line, filePattern, RegexOptions.Multiline);
            if (match.Success)
            {
                if (currentFile != null)
                {
                    files.Add(currentFile with { Content = currentContent.ToString() });
                }
                
                var fileName = match.Groups[1].Value;
                currentFile = new DiffFile(fileName, "");
                currentContent.Clear();
            }
            
            currentContent.AppendLine(line);
        }

        if (currentFile != null)
        {
            files.Add(currentFile with { Content = currentContent.ToString() });
        }

        return files;
    }

    private static ChunkPriority GetFilePriority(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        return extension switch
        {
            ".cs" => ChunkPriority.High,
            ".json" when fileName.Contains("appsettings") => ChunkPriority.High,
            ".csproj" => ChunkPriority.Medium,
            ".md" => ChunkPriority.Low,
            ".txt" => ChunkPriority.Low,
            _ when IsGeneratedFile(fileName) => ChunkPriority.Low,
            _ => ChunkPriority.Medium
        };
    }

    private static bool IsGeneratedFile(string fileName)
    {
        var lowerFileName = fileName.ToLowerInvariant();
        return lowerFileName.Contains("generated") || 
               lowerFileName.Contains(".designer.") ||
               lowerFileName.Contains("obj/") ||
               lowerFileName.Contains("bin/");
    }

    private static List<DiffChunk> ChunkLargeFile(DiffFile file)
    {
        var chunks = new List<DiffChunk>();
        var lines = file.Content.Split('\n');
        var currentChunk = new StringBuilder();
        var chunkIndex = 0;
        
        foreach (var line in lines)
        {
            currentChunk.AppendLine(line);
            
            if (currentChunk.Length * EstimatedTokensPerChar > MaxContextTokens / 3) // Smaller chunks for large files
            {
                chunks.Add(new DiffChunk(
                    $"{file.FileName} (Part {++chunkIndex})", 
                    currentChunk.ToString(), 
                    GetFilePriority(file.FileName)
                ));
                currentChunk.Clear();
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(new DiffChunk(
                $"{file.FileName} (Part {++chunkIndex})", 
                currentChunk.ToString(), 
                GetFilePriority(file.FileName)
            ));
        }

        return chunks;
    }

    public static string GetDiffSummary(List<DiffChunk> chunks)
    {
        var totalChunks = chunks.Count;
        var totalSize = chunks.Sum(c => c.Content.Length);
        var fileTypes = chunks.GroupBy(c => Path.GetExtension(c.Name))
                              .ToDictionary(g => g.Key, g => g.Count());

        var summary = new StringBuilder();
        summary.AppendLine($"Diff Analysis Summary:");
        summary.AppendLine($"- Total chunks: {totalChunks}");
        summary.AppendLine($"- Total size: {totalSize:N0} characters");
        summary.AppendLine($"- File types: {string.Join(", ", fileTypes.Select(kv => $"{kv.Key}: {kv.Value}"))}");
        
        return summary.ToString();
    }
}

public record DiffFile(string FileName, string Content);

public record DiffChunk(string Name, string Content, ChunkPriority Priority);

public enum ChunkPriority
{
    Low = 1,
    Medium = 2,
    High = 3
}