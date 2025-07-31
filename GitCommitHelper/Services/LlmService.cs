using GitCommitHelper.Services.Interfaces;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public class LlmService : ILlmService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _modelRunnerUrl = "http://localhost:12434/engines/v1/chat/completions";
    private readonly string _modelName;

    public LlmService(string modelName)
    {
        _modelName = modelName;
    }

    public Task<string> GetCommitMessageAsync(string diffContent)
    {
        var systemPrompt = "You are an expert programmer. You write concise, high-quality git commit messages in the conventional commit format. Based on the following git diff, generate a descriptive commit message. Do not include any preamble or extra text, only the commit message itself.";
        return SendRequestAsync(systemPrompt, diffContent);
    }

    public Task<string> GetPrDetailsAsync(string diffContent)
    {
        var systemPrompt = "You are a senior software developer writing a pull request. Based on the following git diff, generate a PR Title and a PR Description. The description should be in Markdown format, outlining the key changes and their purpose. Respond ONLY in the format: [TITLE]: Your PR Title\n\n[DESCRIPTION]:\nYour PR Description in markdown." +
            "Keep it short and sweet.";
        return SendRequestAsync(systemPrompt, diffContent);
    }

    public Task<string> GetCodeReviewAsync(string diffContent)
    {
        var systemPrompt = "You are a senior C# developer conducting a code review. Analyze the following git diff and provide constructive feedback. Focus on: " +
            "1. Code quality and best practices " +
            "2. Potential bugs or issues " +
            "3. Performance considerations " +
            "4. Security concerns " +
            "5. Maintainability improvements " +
            "Format your response as markdown with clear sections. Be concise but thorough. If the code looks good, mention what's done well.";
        return SendRequestAsync(systemPrompt, diffContent, maxTokens: 1000);
    }

    private async Task<string> SendRequestAsync(string systemPrompt, string userPrompt, int maxTokens = 500)
    {
        Console.WriteLine("    [LlmService] Sending request to local Gemma model...");
        var payload = new
        {
            model = _modelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.3,
            max_tokens = maxTokens
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(_modelRunnerUrl, httpContent);
        }
        catch (TaskCanceledException ex)
        {
            throw new HttpRequestException($"The request to the local model at '{_modelRunnerUrl}' timed out. Please ensure the model is running and responsive.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            
            // Check for token limit errors
            if (errorContent.Contains("context length") || errorContent.Contains("token limit") || 
                errorContent.Contains("too many tokens") || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                throw new InvalidOperationException($"Token limit exceeded. The diff content is too large for the model's context window. Try with smaller changes or use chunking.");
            }
            
            throw new HttpRequestException($"Request failed with status code {response.StatusCode}. Response: {errorContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var jsonNode = JsonNode.Parse(responseBody);
        var messageContent = jsonNode?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

        return messageContent?.Trim() ?? "Error: Could not parse response from model.";
    }
}