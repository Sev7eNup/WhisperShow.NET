using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;

namespace WhisperShow.Tests.TestHelpers;

public static class OptionsHelper
{
    public static IOptions<WhisperShowOptions> Create(Action<WhisperShowOptions>? configure = null)
    {
        var options = new WhisperShowOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }
}
