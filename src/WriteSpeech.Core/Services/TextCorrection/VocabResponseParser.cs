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
            return (DeduplicateText(response.Trim()), []);

        var text = DeduplicateText(response[..delimiterIndex].Trim());
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
    /// Removes duplicate lines/paragraphs that the model may produce.
    /// If the text contains the same sentence repeated multiple times, keeps only one copy.
    /// </summary>
    internal static string DeduplicateText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length <= 1)
            return text;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (var line in lines)
        {
            if (seen.Add(line))
                unique.Add(line);
        }

        return string.Join("\n", unique);
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
