using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WriteSpeech.Core.Services.TextCorrection;

namespace WriteSpeech.Tests.Services;

public class VocabResponseParserTests
{
    // --- Parse: no delimiter ---

    [Fact]
    public void Parse_NoDelimiter_ReturnsFullText()
    {
        var (text, vocab) = VocabResponseParser.Parse("Hello world, this is a test.");

        text.Should().Be("Hello world, this is a test.");
        vocab.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var (text, vocab) = VocabResponseParser.Parse(null);

        text.Should().BeEmpty();
        vocab.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        var (text, vocab) = VocabResponseParser.Parse("");

        text.Should().BeEmpty();
        vocab.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Whitespace_ReturnsOriginalAndEmptyVocab()
    {
        var (text, vocab) = VocabResponseParser.Parse("   ");

        text.Should().Be("   ");
        vocab.Should().BeEmpty();
    }

    // --- Parse: with delimiter ---

    [Fact]
    public void Parse_WithDelimiter_SplitsCorrectly()
    {
        var response = "The meeting with Dr. Mueller was productive.\n---VOCAB---\nDr. Mueller";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("The meeting with Dr. Mueller was productive.");
        vocab.Should().ContainSingle().Which.Should().Be("Dr. Mueller");
    }

    [Fact]
    public void Parse_MultipleWords_ExtractsAll()
    {
        var response = "We discussed TensorFlow and Kubernetes.\n---VOCAB---\nTensorFlow\nKubernetes";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("We discussed TensorFlow and Kubernetes.");
        vocab.Should().HaveCount(2);
        vocab.Should().Contain("TensorFlow");
        vocab.Should().Contain("Kubernetes");
    }

    [Fact]
    public void Parse_TrimsWhitespace()
    {
        var response = "  Some text.  \n---VOCAB---\n  Word  \n";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("Some text.");
        vocab.Should().ContainSingle().Which.Should().Be("Word");
    }

    [Fact]
    public void Parse_FiltersEmptyLines()
    {
        var response = "Text.\n---VOCAB---\n\n\nWord\n\n";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().ContainSingle().Which.Should().Be("Word");
    }

    [Fact]
    public void Parse_FiltersSingleCharWords()
    {
        var response = "Text.\n---VOCAB---\nA\nTensorFlow\nB";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().ContainSingle().Which.Should().Be("TensorFlow");
    }

    [Fact]
    public void Parse_FiltersOverlyLongWords()
    {
        var longWord = new string('A', 101);
        var response = $"Text.\n---VOCAB---\n{longWord}\nValid";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().ContainSingle().Which.Should().Be("Valid");
    }

    [Fact]
    public void Parse_DeduplicatesCaseInsensitive()
    {
        var response = "Text.\n---VOCAB---\nTensorFlow\ntensorflow\nTENSORFLOW";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().ContainSingle();
    }

    [Fact]
    public void Parse_DelimiterOnly_ReturnsBothEmpty()
    {
        var response = "---VOCAB---";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().BeEmpty();
        vocab.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TextWithNoVocabAfterDelimiter_ReturnsTextAndEmptyVocab()
    {
        var response = "Some corrected text.\n---VOCAB---\n";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("Some corrected text.");
        vocab.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CleansMarkdownListMarkers()
    {
        var response = "Text.\n---VOCAB---\n- TensorFlow\n* Kubernetes\n-- PyTorch";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().HaveCount(3);
        vocab.Should().Contain("TensorFlow");
        vocab.Should().Contain("Kubernetes");
        vocab.Should().Contain("PyTorch");
    }

    [Fact]
    public void Parse_WordAtExactMinLength_IsIncluded()
    {
        var response = "Text.\n---VOCAB---\nAI";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().ContainSingle().Which.Should().Be("AI");
    }

    [Fact]
    public void Parse_WordAtExactMaxLength_IsIncluded()
    {
        var word = new string('A', 100);
        var response = $"Text.\n---VOCAB---\n{word}";

        var (text, vocab) = VocabResponseParser.Parse(response);

        vocab.Should().ContainSingle().Which.Should().Be(word);
    }

    // --- Deduplication ---

    [Fact]
    public void Parse_RepeatedLines_DeduplicatesText()
    {
        var response = "Da bin ich mal gespannt.\nDa bin ich mal gespannt.\nDa bin ich mal gespannt.\n---VOCAB---\nSperenzkes";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("Da bin ich mal gespannt.");
        vocab.Should().ContainSingle().Which.Should().Be("Sperenzkes");
    }

    [Fact]
    public void Parse_RepeatedLinesWithoutDelimiter_DeduplicatesText()
    {
        var response = "Hello world.\nHello world.\nHello world.";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("Hello world.");
        vocab.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DistinctLines_PreservesAll()
    {
        var response = "First sentence.\nSecond sentence.\n---VOCAB---\nWord";

        var (text, vocab) = VocabResponseParser.Parse(response);

        text.Should().Be("First sentence.\nSecond sentence.");
    }

    // --- AddExtractedVocabulary ---

    [Fact]
    public void AddExtractedVocabulary_CallsAddEntryForEach()
    {
        var dictionary = Substitute.For<IDictionaryService>();
        var logger = Substitute.For<ILogger>();
        var words = new List<string> { "TensorFlow", "Kubernetes" };

        VocabResponseParser.AddExtractedVocabulary(words, dictionary, logger);

        dictionary.Received(1).AddEntry("TensorFlow");
        dictionary.Received(1).AddEntry("Kubernetes");
    }

    [Fact]
    public void AddExtractedVocabulary_EmptyList_DoesNotCallAddEntry()
    {
        var dictionary = Substitute.For<IDictionaryService>();
        var logger = Substitute.For<ILogger>();

        VocabResponseParser.AddExtractedVocabulary([], dictionary, logger);

        dictionary.DidNotReceiveWithAnyArgs().AddEntry(default!);
    }
}
