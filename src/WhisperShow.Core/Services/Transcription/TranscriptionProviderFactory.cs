using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Transcription;

public class TranscriptionProviderFactory
{
    private readonly IEnumerable<ITranscriptionService> _providers;

    public TranscriptionProviderFactory(IEnumerable<ITranscriptionService> providers)
    {
        _providers = providers;
    }

    public virtual ITranscriptionService GetProvider(TranscriptionProvider provider)
    {
        return provider switch
        {
            TranscriptionProvider.OpenAI => _providers.OfType<OpenAiTranscriptionService>().First(),
            TranscriptionProvider.Local => _providers.OfType<LocalTranscriptionService>().First(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
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
