using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;
using Voxwright.Core.Services.IDE;

namespace Voxwright.Core.Services.TextCorrection;

public class AnthropicTextCorrectionService : CloudTextCorrectionServiceBase
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;

    public override TextCorrectionProvider ProviderType => TextCorrectionProvider.Anthropic;

    public AnthropicTextCorrectionService(
        ILogger<AnthropicTextCorrectionService> logger,
        IOptionsMonitor<VoxwrightOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        IIDEContextService ideContextService,
        IHttpClientFactory httpClientFactory)
        : base(logger, optionsMonitor, dictionaryService, ideContextService)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<string?> SendCorrectionRequestAsync(
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        var anthropic = OptionsMonitor.CurrentValue.TextCorrection.Anthropic;

        if (string.IsNullOrWhiteSpace(anthropic.ApiKey))
        {
            Logger.LogWarning("Anthropic API key not configured, skipping text correction");
            return null;
        }

        var client = _httpClientFactory.CreateClient("Anthropic");

        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", anthropic.ApiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        var body = new AnthropicRequest
        {
            Model = anthropic.Model,
            MaxTokens = 4096,
            System = systemPrompt,
            Messages =
            [
                new AnthropicMessage { Role = "user", Content = userMessage }
            ]
        };

        request.Content = JsonContent.Create(body, options: JsonOptions);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, ct);

        return result?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class AnthropicRequest
    {
        public string Model { get; set; } = "";
        public int MaxTokens { get; set; }
        public string? System { get; set; }
        public List<AnthropicMessage> Messages { get; set; } = [];
    }

    private sealed class AnthropicMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private sealed class AnthropicResponse
    {
        public List<AnthropicContentBlock>? Content { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
    }
}
