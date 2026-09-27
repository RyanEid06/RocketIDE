namespace RocketIDE.Debugger;

internal static class DebuggerCreation
{
    internal static async Task<T> AwaitAsync<T>(Task<T> creation, TimeSpan timeout, Action releaseContext, CancellationToken cancellationToken)
        where T : IAsyncDisposable
    {
        try { return await creation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
        catch
        {
            // Cancellation owns the eventual result too. Closing the context now could
            // discard the constructor's completion and orphan its native engine.
            _ = DisposeLateResultAsync();
            throw;
        }

        async Task DisposeLateResultAsync()
        {
            try { await (await creation.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            finally { releaseContext(); }
        }
    }
}
