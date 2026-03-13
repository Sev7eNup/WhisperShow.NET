using Voxwright.Core.Models;
using Voxwright.Core.Services.TextCorrection;
using Voxwright.Core.Services.Transcription;

namespace Voxwright.Tests.TestHelpers;

/// <summary>
/// Test override of <see cref="TranscriptionProviderFactory"/> that returns the injected mock
/// regardless of the requested provider type.
/// </summary>
internal class TestProviderFactory : TranscriptionProviderFactory
{
    private readonly ITranscriptionService _provider;

    public TestProviderFactory(ITranscriptionService provider) : base([provider])
        => _provider = provider;

    public override ITranscriptionService GetProvider(TranscriptionProvider type) => _provider;
}

/// <summary>
/// Test override of <see cref="TextCorrectionProviderFactory"/> that returns the injected mock
/// for all providers except <see cref="TextCorrectionProvider.Off"/> (which returns null).
/// </summary>
internal class TestCorrectionProviderFactory : TextCorrectionProviderFactory
{
    private readonly ITextCorrectionService? _provider;

    public TestCorrectionProviderFactory(ITextCorrectionService? provider) : base([])
        => _provider = provider;

    public override ITextCorrectionService? GetProvider(TextCorrectionProvider type)
        => type == TextCorrectionProvider.Off ? null : _provider;
}
