
using System;
using System.IO;

namespace GitCommitHelper.Services;

public static class ErrorLoggingService
{
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "error_log.txt");

    public static void LogError(Exception ex)
    {
        try
        {
            var errorMessage = $"""
            --------------------------------------------------
            Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            Error: {ex.Message}
            Stack Trace:
            {ex.StackTrace}
            --------------------------------------------------

            """;

            File.AppendAllText(LogFilePath, errorMessage);
        }
        catch (Exception logEx)
        {
            // Fallback if logging fails
            Console.WriteLine("FATAL: Could not write to log file.");
            Console.WriteLine(logEx.ToString());
        }
    }
}
