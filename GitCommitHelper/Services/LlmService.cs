using GitCommitHelper.Services.Interfaces;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public class LlmService : ILlmService
{
    private static readonly HttpClient _httpClient = new();
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
        var systemPrompt = "You are a senior software developer writing a pull request. Based on the following git diff, generate a PR Title and a PR Description. The description should be in Markdown format, outlining the key changes and their purpose. Respond ONLY in the format: [TITLE]: Your PR Title\n\n[DESCRIPTION]:\nYour PR Description in markdown.";
        return SendRequestAsync(systemPrompt, diffContent);
    }

    private async Task<string> SendRequestAsync(string systemPrompt, string userPrompt)
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
            max_tokens = 500
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_modelRunnerUrl, httpContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Request failed with status code {response.StatusCode}. Response: {errorContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var jsonNode = JsonNode.Parse(responseBody);
        var messageContent = jsonNode?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

        return messageContent?.Trim() ?? "Error: Could not parse response from model.";
    }
}