using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.Core.Services;

namespace WriteSpeech.Tests.Services;

public class DebouncedSaveHelperTests
{
    [Fact]
    public async Task Schedule_InvokesSaveAfterDelay()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 50);

        helper.Schedule();
        await helper.FlushAsync();

        saveCount.Should().Be(1);
    }

    [Fact]
    public async Task Schedule_MultipleCalls_OnlyLastOneExecutes()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 5000);

        helper.Schedule();
        helper.Schedule();
        helper.Schedule();
        await helper.FlushAsync();

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

    [Fact]
    public async Task FlushAsync_WithPendingSave_ExecutesSaveImmediately()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 5000);

        helper.Schedule();
        // Flush immediately — should not wait 5 seconds
        await helper.FlushAsync();

        saveCount.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_CancelsPendingDelay_ExecutesOnlyOnce()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 100);

        helper.Schedule();
        await Task.Delay(30);
        await helper.FlushAsync();
        // Wait past the original delay to ensure cancelled scheduled save doesn't also fire
        await Task.Delay(200);

        saveCount.Should().Be(1);
    }

    [Fact]
    public async Task Schedule_AfterFlushAsync_WorksNormally()
    {
        var saveCount = 0;
        using var helper = new DebouncedSaveHelper(() => { saveCount++; return Task.CompletedTask; },
            NullLogger.Instance, delayMs: 50);

        helper.Schedule();
        await helper.FlushAsync();
        saveCount.Should().Be(1);

        // Schedule again after flush — should work
        helper.Schedule();
        await helper.FlushAsync();

        saveCount.Should().Be(2);
    }

    [Fact]
    public async Task FlushAsync_SaveThrows_DoesNotThrow()
    {
        var callCount = 0;
        using var helper = new DebouncedSaveHelper(() =>
        {
            callCount++;
            throw new IOException("Disk full");
        }, NullLogger.Instance, delayMs: 50);

        helper.Schedule();

        var act = () => helper.FlushAsync();

        await act.Should().NotThrowAsync();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Schedule_AfterFailedSave_StillWorks()
    {
        var shouldFail = true;
        var successCount = 0;
        using var helper = new DebouncedSaveHelper(() =>
        {
            if (shouldFail) throw new IOException("Disk full");
            successCount++;
            return Task.CompletedTask;
        }, NullLogger.Instance, delayMs: 50);

        // First save fails
        helper.Schedule();
        await helper.FlushAsync();

        // Second save should still work
        shouldFail = false;
        helper.Schedule();
        await helper.FlushAsync();

        successCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var helper = new DebouncedSaveHelper(() => Task.CompletedTask,
            NullLogger.Instance, delayMs: 50);

        helper.Dispose();
        var act = () => helper.Dispose();

        act.Should().NotThrow();
    }
}
