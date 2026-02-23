using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperShow.Core.Services;

namespace WhisperShow.Tests.Services;

public class DebouncedSaveHelperTests
{
    [Fact]
    public async Task Schedule_InvokesSaveAfterDelay()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 50);

        helper.Schedule();
        await Task.Delay(150);

        saveCount.Should().Be(1);
    }

    [Fact]
    public async Task Schedule_MultipleCalls_OnlyLastOneExecutes()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 100);

        helper.Schedule();
        await Task.Delay(30);
        helper.Schedule();
        await Task.Delay(30);
        helper.Schedule();
        await Task.Delay(200);

        saveCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_CancelsPendingSave()
    {
        var saveCount = 0;
        var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 200);

        helper.Schedule();
        await Task.Delay(50);
        helper.Dispose();
        await Task.Delay(300);

        saveCount.Should().Be(0);
    }

    [Fact]
    public async Task Schedule_SaveExceptionDoesNotThrow()
    {
        using var helper = new DebouncedSaveHelper(
            () => throw new InvalidOperationException("Disk full"),
            NullLogger.Instance, delayMs: 10);

        helper.Schedule();
        await Task.Delay(100);

        // Should not throw — exception is logged internally
    }
}
