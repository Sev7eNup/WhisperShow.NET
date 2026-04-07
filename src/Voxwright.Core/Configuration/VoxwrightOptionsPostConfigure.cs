using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using Voxwright.Core.Services.Configuration;

namespace Voxwright.Core.Configuration;

/// <summary>
/// Decrypts DPAPI-encrypted API keys after configuration binding.
/// Runs on every <see cref="IOptionsMonitor{T}"/> reload so that services always
/// see plaintext keys regardless of the on-disk format.
/// </summary>
[SupportedOSPlatform("windows")]
public class VoxwrightOptionsPostConfigure : IPostConfigureOptions<VoxwrightOptions>
{
    public void PostConfigure(string? name, VoxwrightOptions options)
    {
        options.OpenAI.ApiKey = ApiKeyProtector.Unprotect(options.OpenAI.ApiKey);
        options.GroqTranscription.ApiKey = ApiKeyProtector.Unprotect(options.GroqTranscription.ApiKey);
        options.CustomTranscription.ApiKey = ApiKeyProtector.Unprotect(options.CustomTranscription.ApiKey);
        options.TextCorrection.Anthropic.ApiKey = ApiKeyProtector.Unprotect(options.TextCorrection.Anthropic.ApiKey);
        options.TextCorrection.Google.ApiKey = ApiKeyProtector.Unprotect(options.TextCorrection.Google.ApiKey);
        options.TextCorrection.Groq.ApiKey = ApiKeyProtector.Unprotect(options.TextCorrection.Groq.ApiKey);
        options.TextCorrection.Custom.ApiKey = ApiKeyProtector.Unprotect(options.TextCorrection.Custom.ApiKey);
    }
}
