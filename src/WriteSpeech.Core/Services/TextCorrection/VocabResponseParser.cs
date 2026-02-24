using Microsoft.Extensions.Logging;

namespace WriteSpeech.Core.Services.TextCorrection;

/// <summary>
/// Parses AI correction responses that may contain a vocabulary section after
/// the ---VOCAB--- delimiter. Returns the clean text and extracted vocabulary words.
/// </summary>
public static class VocabResponseParser
{
    /// <summary>
    /// Splits the AI response into corrected text and vocabulary entries.
    /// If no delimiter is found, the entire response is treated as corrected text.
    /// </summary>
    public static (string Text, IReadOnlyList<string> Vocabulary) Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return (response ?? string.Empty, []);

        var delimiterIndex = response.IndexOf(
            TextCorrectionDefaults.VocabDelimiter, StringComparison.Ordinal);

        if (delimiterIndex < 0)
            return (response.Trim(), []);

        var text = response[..delimiterIndex].Trim();
        var vocabSection = response[(delimiterIndex + TextCorrectionDefaults.VocabDelimiter.Length)..];

        var words = vocabSection
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.TrimStart('-', '*', ' '))
            .Where(w => w.Length >= 2 && w.Length <= 100)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (text, words);
    }

    /// <summary>
    /// Adds extracted vocabulary words to the dictionary service.
    /// </summary>
    public static void AddExtractedVocabulary(
        IReadOnlyList<string> vocabulary,
        IDictionaryService dictionaryService,
        ILogger logger)
    {
        foreach (var word in vocabulary)
        {
            dictionaryService.AddEntry(word);
            logger.LogDebug("Auto-added vocabulary: {Word}", word);
        }

        if (vocabulary.Count > 0)
            logger.LogInformation("Auto-added {Count} vocabulary entries to dictionary", vocabulary.Count);
    }
}
