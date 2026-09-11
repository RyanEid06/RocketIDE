using System.IO;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.Diagnostics;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.App.Integration;

public sealed record RocketSessionDocument(string Path, string Text, int Version, string DisplayName);

public sealed class RocketSessionCoordinator : IAsyncDisposable, IRocketEditorFeatureService
{
    private readonly Func<CancellationToken, Task<RocketToolSettings>> _settingsProvider;
    private readonly Func<RocketToolSettings, IRocketToolLocator> _locatorFactory;
    private readonly Func<IRocketLanguageClient> _clientFactory;
    private readonly Func<IReadOnlyList<RocketSessionDocument>> _documentsProvider;
    private readonly Action<string> _setRocketSdkStatus;
    private readonly Action<string> _setLspStatus;
    private readonly Action<string> _appendOutput;
    private readonly Action _showOutput;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRocketLanguageClient? _languageClient;
    private DocumentSynchronizer? _documentSynchronizer;
    private SemanticTokensClient? _semanticTokensClient;
    private readonly Dictionary<string, LspDocumentSyncState> _documentSyncStates = new(StringComparer.OrdinalIgnoreCase);
    private string? _workspacePath;
    private string? _lastDiscoveryProblem;
    private long _diagnosticGeneration;
    private long _activeDiagnosticGeneration;

    public RocketSessionCoordinator(
        Func<CancellationToken, Task<RocketToolSettings>> settingsProvider,
        Func<RocketToolSettings, IRocketToolLocator> locatorFactory,
        Func<IRocketLanguageClient> clientFactory,
        Func<IReadOnlyList<RocketSessionDocument>> documentsProvider,
        Action<string> setRocketSdkStatus,
        Action<string> setLspStatus,
        Action<string> appendOutput,
        Action showOutput)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _locatorFactory = locatorFactory ?? throw new ArgumentNullException(nameof(locatorFactory));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _documentsProvider = documentsProvider ?? throw new ArgumentNullException(nameof(documentsProvider));
        _setRocketSdkStatus = setRocketSdkStatus ?? throw new ArgumentNullException(nameof(setRocketSdkStatus));
        _setLspStatus = setLspStatus ?? throw new ArgumentNullException(nameof(setLspStatus));
        _appendOutput = appendOutput ?? throw new ArgumentNullException(nameof(appendOutput));
        _showOutput = showOutput ?? throw new ArgumentNullException(nameof(showOutput));
    }

    public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;
    public event EventHandler<RocketTransportFaultedEventArgs>? Faulted;
    public event EventHandler<RocketDiagnosticSessionChangedEventArgs>? DiagnosticSessionChanged;
    public event EventHandler<RocketDiagnosticsPublishedEventArgs>? DiagnosticsPublished;
    public event EventHandler<RocketDocumentSyncStateChangedEventArgs>? DocumentSyncStateChanged;

    public IReadOnlyList<string> CompletionTriggerCharacters =>
        _languageClient?.Capabilities.CompletionTriggerCharacters ?? Array.Empty<string>();

    public IReadOnlyList<string> SignatureTriggerCharacters =>
        _languageClient?.Capabilities.SignatureTriggerCharacters ?? Array.Empty<string>();

    public IReadOnlyList<string> SignatureRetriggerCharacters =>
        _languageClient?.Capabilities.SignatureRetriggerCharacters ?? Array.Empty<string>();

    public Task<RocketCompletionResult?> RequestCompletionAsync(
        string path,
        LspPosition position,
        string? triggerCharacter,
        CancellationToken cancellationToken) =>
        RequestFeatureAsync(
            path,
            capabilities => capabilities.SupportsCompletion,
            (client, _) => new CompletionClient(client).RequestAsync(path, position, triggerCharacter, cancellationToken),
            "completion",
            cancellationToken);

    public Task<RocketHover?> RequestHoverAsync(
        string path,
        LspPosition position,
        CancellationToken cancellationToken) =>
        RequestFeatureAsync(
            path,
            capabilities => capabilities.SupportsHover,
            (client, _) => new HoverClient(client).RequestAsync(path, position, cancellationToken),
            "hover",
            cancellationToken);

    public Task<RocketSignatureHelp?> RequestSignatureHelpAsync(
        string path,
        LspPosition position,
        string? triggerCharacter,
        bool isRetrigger,
        CancellationToken cancellationToken) =>
        RequestFeatureAsync(
            path,
            capabilities => capabilities.SupportsSignatureHelp,
            (client, _) => new SignatureHelpClient(client).RequestAsync(path, position, triggerCharacter, isRetrigger, cancellationToken),
            "signature help",
            cancellationToken);

    public Task<RocketSemanticTokensResult?> RequestSemanticTokensAsync(
        string path,
        CancellationToken cancellationToken) =>
        RequestFeatureAsync(
            path,
            capabilities => capabilities.SupportsSemanticTokens,
            (_, semanticTokens) => semanticTokens is null
                ? Task.FromResult<RocketSemanticTokensResult?>(null)
                : semanticTokens.RequestAsync(path, cancellationToken),
            "semantic tokens",
            cancellationToken);

    public void InvalidateSemanticTokens(string path) => _semanticTokensClient?.Invalidate(path);

    public async Task EnsureAsync(string? activePath, string? workspacePath, CancellationToken cancellationToken)
    {
        // Capture UI-owned document state before the first await. MainWindow's provider reads
        // its ObservableCollection and must never be called after ConfigureAwait(false) resumes
        // this coordinator on a pool thread. Document events queue behind the same gate and will
        // reconcile any open/close changes that happen while the server is starting.
        var openDocuments = _documentsProvider();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var desiredWorkspace = NormalizeWorkspace(workspacePath);
            if (_languageClient is { IsInitialized: true } &&
                string.Equals(_workspacePath, desiredWorkspace, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            await StartCoreAsync(activePath, desiredWorkspace, openDocuments, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestartAsync(string? activePath, string? workspacePath, CancellationToken cancellationToken)
    {
        var openDocuments = _documentsProvider();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            await StartCoreAsync(activePath, NormalizeWorkspace(workspacePath), openDocuments, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task OpenDocumentAsync(RocketSessionDocument document, CancellationToken cancellationToken)
    {
        await WithSynchronizerAsync(
            synchronizer => synchronizer.OpenAsync(document.Path, document.Text, document.Version, cancellationToken),
            document,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeDocumentAsync(RocketSessionDocument document, CancellationToken cancellationToken)
    {
        await WithSynchronizerAsync(
            synchronizer => synchronizer.ChangeAsync(document.Path, document.Text, document.Version, cancellationToken),
            document,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_documentSynchronizer is not null)
            {
                await _documentSynchronizer.SaveAsync(path, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_documentSynchronizer is not null)
            {
                await _documentSynchronizer.CloseAsync(path, cancellationToken).ConfigureAwait(false);
            }
            _documentSyncStates.Remove(path);
            _semanticTokensClient?.Invalidate(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Fault cleanup can still be queued behind the same gate when the host is shutting down.
        // Do not dispose the semaphore out from under an in-flight waiter; the coordinator has
        // process-lifetime scope and SemaphoreSlim owns no unmanaged resource unless its wait
        // handle is explicitly requested (which RocketIDE never does).
        await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task StartCoreAsync(
        string? activePath,
        string? workspacePath,
        IReadOnlyList<RocketSessionDocument> openDocuments,
        CancellationToken cancellationToken)
    {
        if (workspacePath is null)
        {
            _setLspStatus("LSP: offline");
            return;
        }

        var settings = await _settingsProvider(cancellationToken).ConfigureAwait(false);
        var discovery = await _locatorFactory(settings).DiscoverAsync(activePath, cancellationToken).ConfigureAwait(false);
        _setRocketSdkStatus(discovery.CompilerPath is not null && discovery.CompilerVersion is not null
            ? $"Rocket SDK: {discovery.CompilerVersion}"
            : "Rocket SDK: not found");

        if (discovery.LanguageServerPath is null || discovery.LanguageServerVersion is null)
        {
            _setLspStatus("LSP: offline");
            var problem = string.Join(" ", discovery.Problems);
            if (!string.Equals(_lastDiscoveryProblem, problem, StringComparison.Ordinal))
            {
                _lastDiscoveryProblem = problem;
                foreach (var item in discovery.Problems)
                {
                    _appendOutput($"Rocket tool discovery: {item}");
                }
                _showOutput();
            }
            return;
        }

        var client = _clientFactory();
        client.LogReceived += Client_LogReceived;
        client.NotificationReceived += Client_NotificationReceived;
        client.Faulted += Client_Faulted;
        // Publish ownership before starting the process. If the child/transport faults during
        // initialization, fault cleanup can still identify this exact client instead of racing
        // a not-yet-assigned session field.
        _languageClient = client;
        try
        {
            _setLspStatus("LSP: starting…");
            await client.StartAsync(discovery.LanguageServerPath, workspacePath, cancellationToken).ConfigureAwait(false);
            if (!client.IsInitialized)
            {
                throw new IOException("rocket-lsp terminated during initialization.");
            }

            _documentSynchronizer = new DocumentSynchronizer(client);
            _semanticTokensClient = client.Capabilities.SupportsSemanticTokens
                ? new SemanticTokensClient(client, client.Capabilities.SemanticTokenLegend, client.Capabilities.SupportsSemanticTokenDelta)
                : null;
            _documentSyncStates.Clear();
            _workspacePath = workspacePath;
            _lastDiscoveryProblem = null;
            _setLspStatus($"LSP: online ({discovery.LanguageServerVersion})");
            _appendOutput($"rocket-lsp initialized: {discovery.LanguageServerVersion}");
            _appendOutput($"rocket-lsp path: {discovery.LanguageServerPath}");
            StartDiagnosticSession();
            await SyncOpenDocumentsCoreAsync(openDocuments, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(_languageClient, client))
            {
                _languageClient = null;
                _documentSynchronizer = null;
                _semanticTokensClient = null;
                _documentSyncStates.Clear();
                _workspacePath = null;
            }
            Unsubscribe(client);
            await client.DisposeAsync().ConfigureAwait(false);
            InvalidateDiagnosticSession();
            _setLspStatus("LSP: offline");
            throw;
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var client = _languageClient;
        _languageClient = null;
        _documentSynchronizer = null;
        _semanticTokensClient = null;
        _documentSyncStates.Clear();
        _workspacePath = null;
        if (client is null)
        {
            return;
        }

        InvalidateDiagnosticSession();
        Unsubscribe(client);
        try
        {
            await client.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            _setLspStatus("LSP: offline");
        }
    }

    private async Task SyncOpenDocumentsCoreAsync(
        IReadOnlyList<RocketSessionDocument> openDocuments,
        CancellationToken cancellationToken)
    {
        if (_documentSynchronizer is null)
        {
            return;
        }

        foreach (var document in openDocuments)
        {
            var state = await _documentSynchronizer.OpenAsync(document.Path, document.Text, document.Version, cancellationToken).ConfigureAwait(false);
            UpdateDocumentSyncState(document, state);
        }
    }

    private async Task WithSynchronizerAsync(
        Func<DocumentSynchronizer, Task<LspDocumentSyncState>> operation,
        RocketSessionDocument document,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_documentSynchronizer is null)
            {
                return;
            }

            var state = await operation(_documentSynchronizer).ConfigureAwait(false);
            UpdateDocumentSyncState(document, state);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void UpdateDocumentSyncState(RocketSessionDocument document, LspDocumentSyncState state)
    {
        _documentSyncStates[document.Path] = state;
        if (state != LspDocumentSyncState.Synchronized)
        {
            _semanticTokensClient?.Invalidate(document.Path);
        }

        RaiseEventSafely(
            DocumentSyncStateChanged,
            new RocketDocumentSyncStateChangedEventArgs(document.Path, document.Version, state),
            "document-sync");

        if (state == LspDocumentSyncState.LargeFileUnsupportedByLsp)
        {
            _setLspStatus($"LSP: large-file mode ({document.DisplayName})");
            _appendOutput($"LSP disabled for '{document.DisplayName}': document exceeds Rocket's 4 MiB open-document limit.");
        }
    }

    private void Client_LogReceived(object? sender, string line) => _appendOutput($"[rocket-lsp] {line}");

    private void Client_NotificationReceived(object? sender, RocketServerNotificationEventArgs e)
    {
        if (sender is not IRocketLanguageClient client || !ReferenceEquals(client, _languageClient))
        {
            return;
        }

        var generation = Volatile.Read(ref _activeDiagnosticGeneration);
        if (generation <= 0)
        {
            return;
        }

        if (string.Equals(e.Method, "textDocument/publishDiagnostics", StringComparison.Ordinal))
        {
            try
            {
                var publication = LspDiagnosticMapper.Map(e.Parameters, generation);
                RaiseEventSafely(
                    DiagnosticsPublished,
                    new RocketDiagnosticsPublishedEventArgs(publication),
                    "diagnostics");
            }
            catch (LspProtocolException exception)
            {
                _appendOutput($"Invalid textDocument/publishDiagnostics payload: {exception.Message}");
            }
        }

        RaiseEventSafely(NotificationReceived, e, "notification");
    }

    private void Client_Faulted(object? sender, RocketTransportFaultedEventArgs e)
    {
        if (sender is not IRocketLanguageClient client || !ReferenceEquals(client, _languageClient))
        {
            return;
        }

        InvalidateDiagnosticSession();
        _setLspStatus("LSP: offline");
        _appendOutput($"rocket-lsp connection failed: {e.Exception.Message}");
        _showOutput();
        RaiseEventSafely(Faulted, e, "fault");
        _ = Task.Run(() => CleanupFaultedClientAsync(client));
    }

    private async Task CleanupFaultedClientAsync(IRocketLanguageClient client)
    {
        var acquired = false;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            acquired = true;
            if (!ReferenceEquals(client, _languageClient))
            {
                return;
            }

            _languageClient = null;
            _documentSynchronizer = null;
            _semanticTokensClient = null;
            _documentSyncStates.Clear();
            _workspacePath = null;
            Unsubscribe(client);
            await client.DisposeAsync().ConfigureAwait(false);
            _setLspStatus("LSP: offline");
        }
        catch (Exception exception)
        {
            _appendOutput($"rocket-lsp fault cleanup failed: {exception.Message}");
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }
    }


    private async Task<T?> RequestFeatureAsync<T>(
        string path,
        Func<RocketLanguageServerCapabilities, bool> capabilityPredicate,
        Func<IRocketLanguageClient, SemanticTokensClient?, Task<T?>> request,
        string featureName,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(capabilityPredicate);
        ArgumentNullException.ThrowIfNull(request);

        IRocketLanguageClient client;
        SemanticTokensClient? semanticTokens;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidate = _languageClient;
            semanticTokens = _semanticTokensClient;
            if (candidate is null || !candidate.IsInitialized ||
                !_documentSyncStates.TryGetValue(path, out var syncState) ||
                syncState != LspDocumentSyncState.Synchronized ||
                !capabilityPredicate(candidate.Capabilities))
            {
                return null;
            }
            client = candidate;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var result = await request(client, semanticTokens).ConfigureAwait(false);
            return ReferenceEquals(client, _languageClient) && client.IsInitialized ? result : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonRpcResponseException or LspProtocolException or IOException or InvalidOperationException or ObjectDisposedException)
        {
            _appendOutput($"Rocket LSP {featureName} request failed: {exception.Message}");
            return null;
        }
    }


    private void StartDiagnosticSession()
    {
        var generation = Interlocked.Increment(ref _diagnosticGeneration);
        Volatile.Write(ref _activeDiagnosticGeneration, generation);
        RaiseEventSafely(
            DiagnosticSessionChanged,
            new RocketDiagnosticSessionChangedEventArgs(generation, isOnline: true),
            "diagnostic-session");
    }

    private void InvalidateDiagnosticSession()
    {
        Volatile.Write(ref _activeDiagnosticGeneration, 0);
        var generation = Interlocked.Increment(ref _diagnosticGeneration);
        RaiseEventSafely(
            DiagnosticSessionChanged,
            new RocketDiagnosticSessionChangedEventArgs(generation, isOnline: false),
            "diagnostic-session");
    }

    private void RaiseEventSafely<TEventArgs>(EventHandler<TEventArgs>? handlers, TEventArgs args, string eventName)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                _appendOutput($"Rocket session {eventName} observer failed: {exception.Message}");
            }
        }
    }

    private void Unsubscribe(IRocketLanguageClient client)
    {
        client.LogReceived -= Client_LogReceived;
        client.NotificationReceived -= Client_NotificationReceived;
        client.Faulted -= Client_Faulted;
    }

    private static string? NormalizeWorkspace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }
}
