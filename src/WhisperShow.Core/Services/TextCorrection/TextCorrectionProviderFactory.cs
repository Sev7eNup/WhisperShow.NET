using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.TextCorrection;

public class TextCorrectionProviderFactory
{
    private readonly Dictionary<TextCorrectionProvider, ITextCorrectionService> _providerMap;

    public TextCorrectionProviderFactory(IEnumerable<ITextCorrectionService> providers)
    {
        _providerMap = providers.ToDictionary(p => p.ProviderType);
    }

    public virtual ITextCorrectionService? GetProvider(TextCorrectionProvider provider)
    {
        if (provider == TextCorrectionProvider.Off) return null;

        return _providerMap.TryGetValue(provider, out var service)
            ? service
            : throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown text correction provider");
    }
}
