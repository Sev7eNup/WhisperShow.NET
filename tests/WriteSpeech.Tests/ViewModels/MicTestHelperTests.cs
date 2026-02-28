using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class MicTestHelperTests
{
    private readonly SynchronousDispatcherService _dispatcher = new();

    [Fact]
    public void Constructor_NullDispatcher_Throws()
    {
        var act = () => new MicTestHelper(null!, NullLogger.Instance, _ => { });

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dispatcher");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new MicTestHelper(_dispatcher, null!, _ => { });

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullCallback_Throws()
    {
        var act = () => new MicTestHelper(_dispatcher, NullLogger.Instance, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("onLevelChanged");
    }

    [Fact]
    public void IsTesting_DefaultsFalse()
    {
        using var helper = new MicTestHelper(_dispatcher, NullLogger.Instance, _ => { });

        helper.IsTesting.Should().BeFalse();
    }

    [Fact]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        using var helper = new MicTestHelper(_dispatcher, NullLogger.Instance, _ => { });

        var act = () => helper.Stop();

        act.Should().NotThrow();
    }

    [Fact]
    public void Stop_SetsIsTestingFalse()
    {
        using var helper = new MicTestHelper(_dispatcher, NullLogger.Instance, _ => { });

        helper.Stop();

        helper.IsTesting.Should().BeFalse();
    }

    [Fact]
    public void Stop_CallsCallbackWithZero()
    {
        float lastLevel = -1;
        using var helper = new MicTestHelper(_dispatcher, NullLogger.Instance, level => lastLevel = level);

        helper.Stop();

        lastLevel.Should().Be(0);
    }

    [Fact]
    public void Dispose_WhenNotStarted_DoesNotThrow()
    {
        var helper = new MicTestHelper(_dispatcher, NullLogger.Instance, _ => { });

        var act = () => helper.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var helper = new MicTestHelper(_dispatcher, NullLogger.Instance, _ => { });

        helper.Dispose();
        var act = () => helper.Dispose();

        act.Should().NotThrow();
    }

}
