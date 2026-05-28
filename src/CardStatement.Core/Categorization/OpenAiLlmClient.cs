using System.ClientModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace CardStatement.Core.Categorization;

public sealed class OpenAiLlmClient : ILlmClient
{
    private readonly ChatClient _chat;
    private readonly ILogger<OpenAiLlmClient> _logger;
    private readonly bool _useJsonMode;

    public OpenAiLlmClient(OpenAiOptions options, ILogger<OpenAiLlmClient>? logger = null)
    {
        var apiKey = options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                apiKey = "lm-studio"; // Fallback placeholder key for local custom base URL
            }
            else
            {
                throw new InvalidOperationException("Categorization:OpenAi:ApiKey is not configured.");
            }
        }

        OpenAIClient client;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl),
                NetworkTimeout = TimeSpan.FromMinutes(5) // High timeout for local LLM inference
            };
            client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        }
        else
        {
            client = new OpenAIClient(new ApiKeyCredential(apiKey));
        }

        _chat = client.GetChatClient(options.Model);
        _logger = logger ?? NullLogger<OpenAiLlmClient>.Instance;
        _useJsonMode = options.UseJsonMode;
    }

    public async Task<IReadOnlyList<LlmCategoryChoice>> CategorizeBatchAsync(
        IReadOnlyList<LlmCategorizationItem> items,
        IReadOnlyList<Category> allowedCategories,
        CancellationToken ct = default)
    {
        var prompt = BuildPrompt(items, allowedCategories);
        var combinedPrompt = 
            "Instruction: You categorize credit-card merchant transactions. " +
            "Output STRICT JSON only, no prose. " +
            "Each item must be assigned exactly one category id from the provided list.\n\n" +
            prompt;

        _logger.LogDebug("Sending prompt to LLM (Length: {Length} chars):\n{Prompt}", combinedPrompt.Length, combinedPrompt);

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(combinedPrompt),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.0f,
        };
        if (_useJsonMode)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }

        var response = await _chat.CompleteChatAsync(messages, options, ct).ConfigureAwait(false);
        var content = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : "{}";
        var cleaned = CleanJsonContent(content);

        try
        {
            var parsed = JsonSerializer.Deserialize<LlmResponse>(cleaned);
            if (parsed?.Items is null)
                return ReturnNulls(items);

            var lookup = parsed.Items.ToDictionary(i => i.ItemId, i => i.CategoryId);
            return items.Select(i =>
            {
                Guid? id = lookup.TryGetValue(i.ItemId, out var raw) && Guid.TryParse(raw, out var parsedId)
                    ? parsedId
                    : null;
                return new LlmCategoryChoice(i.ItemId, id);
            }).ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI returned non-JSON content: {Content}", content);
            return ReturnNulls(items);
        }
    }

    public static string CleanJsonContent(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = content.IndexOf('\n');
            if (firstNewLine != -1)
            {
                content = content.Substring(firstNewLine + 1);
            }
            else
            {
                content = content.Substring(3);
            }

            if (content.EndsWith("```", StringComparison.Ordinal))
            {
                content = content.Substring(0, content.Length - 3);
            }
        }
        return content.Trim();
    }

    private static IReadOnlyList<LlmCategoryChoice> ReturnNulls(IReadOnlyList<LlmCategorizationItem> items) =>
        items.Select(i => new LlmCategoryChoice(i.ItemId, null)).ToList();

    private static string BuildPrompt(
        IReadOnlyList<LlmCategorizationItem> items,
        IReadOnlyList<Category> allowedCategories)
    {
        var sb = new StringBuilder(8192);
        sb.AppendLine("Allowed categories (id, name):");
        foreach (var c in allowedCategories)
        {
            sb.Append("  ").Append(c.Id).Append("  ").AppendLine(c.Name);
        }
        sb.AppendLine();
        sb.AppendLine("Transactions to categorize:");
        foreach (var i in items)
        {
            sb.Append("  itemId=").Append(i.ItemId)
                .Append("  amount=").Append(i.Amount.ToString(CultureInfo.InvariantCulture))
                .Append("  description=").AppendLine(i.Description);
        }
        sb.AppendLine();
        sb.AppendLine("Respond with JSON: { \"items\": [ { \"itemId\": \"...\", \"categoryId\": \"<one id from the list>\" }, ... ] }");
        sb.AppendLine("Use exactly one id per itemId. Never invent ids. If unsure, pick the closest match in the list.");
        return sb.ToString();
    }

    private sealed class LlmResponse
    {
        [JsonPropertyName("items")]
        public LlmResponseItem[]? Items { get; set; }
    }

    private sealed class LlmResponseItem
    {
        [JsonPropertyName("itemId")]
        public string ItemId { get; set; } = "";

        [JsonPropertyName("categoryId")]
        public string CategoryId { get; set; } = "";
    }
}
