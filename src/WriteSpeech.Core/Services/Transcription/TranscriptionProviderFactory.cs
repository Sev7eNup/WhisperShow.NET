using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.Transcription;

public class TranscriptionProviderFactory
{
    private readonly IEnumerable<ITranscriptionService> _providers;
    private readonly Dictionary<TranscriptionProvider, ITranscriptionService> _providerMap;

    public TranscriptionProviderFactory(IEnumerable<ITranscriptionService> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
        _providerMap = providers.ToDictionary(p => p.ProviderType);
    }

    public virtual ITranscriptionService GetProvider(TranscriptionProvider provider)
    {
        return _providerMap.TryGetValue(provider, out var service)
            ? service
            : throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown transcription provider");
    }

    public IReadOnlyList<ITranscriptionService> GetAvailableProviders()
    {
        return _providers.Where(p => p.IsAvailable).ToList();
    }

    public IReadOnlyList<ITranscriptionService> GetAllProviders()
    {
        return _providers.ToList();
    }
}
