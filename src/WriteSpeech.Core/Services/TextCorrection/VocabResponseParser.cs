using Microsoft.Extensions.Logging;

namespace WriteSpeech.Core.Services.TextCorrection;

/// <summary>
/// Parses AI correction responses that may contain a vocabulary section after
/// the ---VOCAB--- delimiter. Returns the clean text and extracted vocabulary words.
/// </summary>
public static class VocabResponseParser
{
    /// <summary>
    /// Maximum number of vocabulary entries extracted per AI response.
    /// Prevents the model from over-extracting low-quality entries.
    /// </summary>
    internal const int MaxEntriesPerResponse = 5;

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
            .Select(w => w.Trim('-', '*', ' ', ':'))
            .Where(w => w.Length >= 2 && w.Length <= 100)
            .Where(w => !w.Contains("---"))
            .Where(IsValidVocabEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntriesPerResponse)
            .ToList();

        return (text, words);
    }

    /// <summary>
    /// Validates that a vocab entry looks like a proper noun, brand name, or technical term —
    /// not a common word, sentence, or prompt fragment.
    /// </summary>
    internal static bool IsValidVocabEntry(string entry)
    {
        // Must contain at least one uppercase letter (proper nouns, brands, abbreviations)
        if (!entry.Any(char.IsUpper))
            return false;

        // Max 4 words — longer entries are sentences, not terms
        var wordCount = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 4)
            return false;

        // Multi-word entries ending in sentence punctuation are sentences, not terms
        // (single-word like "Dr." or "Inc." is OK)
        if (wordCount > 1 && (entry.EndsWith('.') || entry.EndsWith('?') || entry.EndsWith('!')))
            return false;

        // Reject hyphenated descriptions with 3+ segments (e.g., "Maus-Low-Level-Hook",
        // "Overlay-Mikrofon-Icon") — real terms rarely have that many hyphens
        var hyphenSegments = entry.Split('-', StringSplitOptions.RemoveEmptyEntries).Length;
        if (hyphenSegments >= 3)
            return false;

        // Single unhyphenated words must show unusual casing/structure to qualify as
        // a brand, acronym, or technical term. Plain Title Case (e.g., "Hausverwaltung",
        // "Großvater", "Test") is rejected — German capitalizes ALL nouns, so Title Case
        // alone is not a signal for proper nouns. Well-known proper nouns like "Kubernetes"
        // or "Berlin" don't need to be in the custom dictionary — the AI already knows them.
        if (wordCount == 1 && hyphenSegments < 2)
        {
            // All-uppercase with ≥2 letters (CUDA, AI, API, GPU)
            if (entry.Length >= 2 && entry.All(c => !char.IsLetter(c) || char.IsUpper(c))
                && entry.Count(char.IsUpper) >= 2)
                return true;

            // Internal uppercase — uppercase letter after position 0 (TensorFlow, iPhone, macOS)
            if (entry.Skip(1).Any(char.IsUpper))
                return true;

            // Contains a digit (GPT4, H264)
            if (entry.Any(char.IsDigit))
                return true;

            // Contains a dot (Inc., Dr.)
            if (entry.Contains('.'))
                return true;

            // Plain Title Case single word — reject
            return false;
        }

        return true;
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
