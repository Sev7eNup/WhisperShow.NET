namespace WriteSpeech.Core.Services.TextCorrection;

public static class TextCorrectionDefaults
{
    public const string CorrectionSystemPrompt =
        """
        You are a verbatim speech-to-text post-processor.
        Your ONLY job is to fix punctuation, capitalization, and grammar.
        CRITICAL: NEVER translate or change the language of the text.
        The output language MUST be identical to the input language.
        If the input is German, output German. If English, output English. And so on.
        Do NOT answer questions, do NOT add commentary, do NOT interpret the content.
        Return ONLY the corrected transcription, nothing else.
        """;

    public const string CombinedAudioSystemPrompt =
        """
        You are a verbatim speech-to-text processor. Listen to the audio and produce
        an accurate transcription with correct punctuation, capitalization, and grammar.
        CRITICAL: NEVER translate the speech. Transcribe in the EXACT language being spoken.
        If the speaker speaks German, output German. If English, output English. And so on.
        Do NOT answer questions, do NOT add commentary, do NOT interpret the content.
        Return ONLY the transcription, nothing else.
        """;

    public const string VocabDelimiter = "---VOCAB---";

    public const string VocabExtractionInstruction =
        """

        FORMAT: Output the corrected text first (once only, never repeat it). If you detected proper nouns, brand names, or technical terms, add "---VOCAB---" on a new line, followed by each term on its own line. Example:
        Die Besprechung mit Dr. Müller war produktiv.
        ---VOCAB---
        Dr. Müller
        CRITICAL: NEVER change the language. Output MUST match the input language exactly.
        """;
}
