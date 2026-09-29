using WifiProfileSwitcher.Core;

namespace WifiProfileSwitcher.Core.Tests;

public sealed class SetupStartFlowTests
{
    [Fact]
    public async Task StartsOnlyAfterSettingsHaveBeenSaved()
    {
        var persisted = false;
        var running = false;
        var result = await SetupStartFlow.Run(
            () => { persisted = true; return Task.CompletedTask; },
            () => { Assert.True(persisted); running = true; return Task.CompletedTask; },
            () => { running = false; return Task.CompletedTask; });
        Assert.True(result.Started);
        Assert.True(running);
    }

    [Fact]
    public async Task FailedSaveNeverStartsSwitching()
    {
        var started = false;
        await Assert.ThrowsAsync<IOException>(() => SetupStartFlow.Run(
            () => throw new IOException(),
            () => { started = true; return Task.CompletedTask; },
            () => Task.CompletedTask));
        Assert.False(started);
    }

    [Fact]
    public async Task StartFailurePreservesSettingsAndStopsPartialActivation()
    {
        var persisted = false;
        var running = false;
        var failure = new InvalidOperationException();
        var result = await SetupStartFlow.Run(
            () => { persisted = true; return Task.CompletedTask; },
            () => { running = true; throw failure; },
            () => { running = false; return Task.CompletedTask; });
        Assert.True(persisted);
        Assert.False(running);
        Assert.False(result.Started);
        Assert.Same(failure, result.StartError);
        Assert.Null(result.StopError);
    }

    [Fact]
    public async Task FailedCleanupCannotBeReportedAsSafelyStopped()
    {
        var stopError = new IOException();
        var result = await SetupStartFlow.Run(() => Task.CompletedTask,
            () => throw new InvalidOperationException(), () => throw stopError);
        Assert.False(result.Started);
        Assert.Same(stopError, result.StopError);
    }
}
