namespace WifiProfileSwitcher.Core;

public sealed record SetupStartResult(bool Started, Exception? StartError = null, Exception? StopError = null);

public static class SetupStartFlow
{
    // Keep saved settings when activation fails. Even a partially completed
    // activation must be paused before the UI can report that switching is off.
    public static async Task<SetupStartResult> Run(Func<Task> save, Func<Task> start, Func<Task> stop)
    {
        await save();
        try
        {
            await start();
            return new(true);
        }
        catch (Exception startError)
        {
            try { await stop(); }
            catch (Exception stopError) { return new(false, startError, stopError); }
            return new(false, startError);
        }
    }
}
