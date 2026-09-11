using RocketIDE.Core.Diagnostics;

namespace RocketIDE.Core.Tests.Diagnostics;

[TestClass]
public sealed class LiveDiagnosticsStoreTests
{
    [TestMethod]
    public void TrackDocument_RepeatedSameVersionWithoutPublicationRemainsAwaiting()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(1, isOnline: true);

        store.TrackDocument(path, version: 0, isSupported: true);
        store.TrackDocument(path, version: 0, isSupported: true);

        Assert.AreEqual(LiveDiagnosticDocumentState.Awaiting, store.GetSnapshot(path)!.State);
    }

    [TestMethod]
    public void TrackDocument_ReenablingSameVersionAfterUnsupportedReturnsToAwaiting()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "large.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(1, isOnline: true);
        store.TrackDocument(path, version: 4, isSupported: false);

        store.TrackDocument(path, version: 4, isSupported: true);

        Assert.AreEqual(LiveDiagnosticDocumentState.Awaiting, store.GetSnapshot(path)!.State);
    }

    [TestMethod]
    public void UpdateBeforeFirstPublication_RemainsAwaitingRatherThanStale()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(1, isOnline: true);
        store.TrackDocument(path, version: 0, isSupported: true);

        store.UpdateDocument(path, version: 1, isSupported: true);

        Assert.AreEqual(LiveDiagnosticDocumentState.Awaiting, store.GetSnapshot(path)!.State);
    }

    [TestMethod]
    public void UnversionedPublication_BecomesStaleWhenOpenDocumentAdvances()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(1, isOnline: true);
        store.TrackDocument(path, version: 0, isSupported: true);
        Assert.IsTrue(store.Publish(1, UriFor(path), path, version: null, diagnostics: [Diagnostic(path, "R2001")]));

        store.UpdateDocument(path, version: 1, isSupported: true);
        store.UpdateDocument(path, version: 2, isSupported: true);

        Assert.AreEqual(LiveDiagnosticDocumentState.Stale, store.GetSnapshot(path)!.State);
        Assert.AreEqual(0, store.GetSnapshot(path)!.Diagnostics.Count);
    }

    [TestMethod]
    public void Publish_ReplacesPreviousDiagnosticsForSameDocumentAndVersion()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(7, isOnline: true);
        store.TrackDocument(path, version: 3, isSupported: true);

        Assert.IsTrue(store.Publish(7, UriFor(path), path, 3, [Diagnostic(path, "R2001")]));
        Assert.IsTrue(store.Publish(7, UriFor(path), path, 3, [Diagnostic(path, "R4001")]));

        var snapshot = store.GetSnapshot(path);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, snapshot.State);
        Assert.AreEqual(1, snapshot.Diagnostics.Count);
        Assert.AreEqual("R4001", snapshot.Diagnostics[0].Code);
    }

    [TestMethod]
    public void UpdateDocumentVersion_MarksExistingDiagnosticsStaleAndHidesThemUntilMatchingPublication()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(2, isOnline: true);
        store.TrackDocument(path, version: 1, isSupported: true);
        store.Publish(2, UriFor(path), path, 1, [Diagnostic(path, "R2001")]);

        store.UpdateDocument(path, version: 2, isSupported: true);

        var stale = store.GetSnapshot(path);
        Assert.IsNotNull(stale);
        Assert.AreEqual(LiveDiagnosticDocumentState.Stale, stale.State);
        Assert.AreEqual(0, stale.Diagnostics.Count);
        Assert.IsFalse(store.Publish(2, UriFor(path), path, 1, [Diagnostic(path, "R3001")]));
        Assert.AreEqual(LiveDiagnosticDocumentState.Stale, store.GetSnapshot(path)!.State);

        Assert.IsTrue(store.Publish(2, UriFor(path), path, 2, []));
        var current = store.GetSnapshot(path)!;
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, current.State);
        Assert.AreEqual(0, current.Diagnostics.Count);
    }

    [TestMethod]
    public void Publish_IgnoresOldSessionGenerationAfterRestart()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(10, isOnline: true);
        store.TrackDocument(path, version: 4, isSupported: true);
        store.Publish(10, UriFor(path), path, 4, [Diagnostic(path, "R2001")]);

        store.BeginSession(11, isOnline: true);

        Assert.IsFalse(store.Publish(10, UriFor(path), path, 4, [Diagnostic(path, "R9999")]));
        var snapshot = store.GetSnapshot(path)!;
        Assert.AreEqual(LiveDiagnosticDocumentState.Awaiting, snapshot.State);
        Assert.AreEqual(0, snapshot.Diagnostics.Count);
    }

    [TestMethod]
    public void BeginSessionOffline_ClearsDiagnosticsAndDistinguishesOfflineFromZeroProblems()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(1, isOnline: true);
        store.TrackDocument(path, version: 0, isSupported: true);
        store.Publish(1, UriFor(path), path, 0, []);
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, store.GetSnapshot(path)!.State);

        store.BeginSession(2, isOnline: false);

        var snapshot = store.GetSnapshot(path)!;
        Assert.AreEqual(LiveDiagnosticDocumentState.Offline, snapshot.State);
        Assert.AreEqual(0, snapshot.Diagnostics.Count);
        Assert.IsFalse(store.IsOnline);
    }

    [TestMethod]
    public void UnsupportedDocument_ClearsDiagnosticsAndRejectsPublications()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "large.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(1, isOnline: true);
        store.TrackDocument(path, version: 0, isSupported: true);
        store.Publish(1, UriFor(path), path, 0, [Diagnostic(path, "R2001")]);

        store.UpdateDocument(path, version: 1, isSupported: false);

        var snapshot = store.GetSnapshot(path)!;
        Assert.AreEqual(LiveDiagnosticDocumentState.Unsupported, snapshot.State);
        Assert.AreEqual(0, snapshot.Diagnostics.Count);
        Assert.IsFalse(store.Publish(1, UriFor(path), path, 1, [Diagnostic(path, "R2001")]));
    }


    [TestMethod]
    public void Publish_OldVersionAfterCurrentPublicationDoesNotEraseCurrentDiagnostics()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(3, isOnline: true);
        store.TrackDocument(path, version: 2, isSupported: true);
        Assert.IsTrue(store.Publish(3, UriFor(path), path, 2, [Diagnostic(path, "R4001")]));

        Assert.IsFalse(store.Publish(3, UriFor(path), path, 1, [Diagnostic(path, "R9999")]));

        var snapshot = store.GetSnapshot(path)!;
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, snapshot.State);
        Assert.AreEqual(2, snapshot.PublicationVersion);
        Assert.AreEqual("R4001", snapshot.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void TrackDocument_PreservesUnopenedPublicationWhenOpenedAtSameVersion()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "other.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(5, isOnline: true);
        Assert.IsTrue(store.Publish(5, UriFor(path), path, version: 7, [Diagnostic(path, "R3001")]));

        store.TrackDocument(path, version: 7, isSupported: true);

        var snapshot = store.GetSnapshot(path)!;
        Assert.IsTrue(snapshot.IsOpen);
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, snapshot.State);
        Assert.AreEqual("R3001", snapshot.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void Publish_CanRepresentUnopenedFileAndUntrackClearsIt()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "other.rocket");
        var store = new LiveDiagnosticsStore();
        store.BeginSession(5, isOnline: true);

        Assert.IsTrue(store.Publish(5, UriFor(path), path, version: null, [Diagnostic(path, "R3001")]));
        var snapshot = store.GetSnapshot(path)!;
        Assert.IsFalse(snapshot.IsOpen);
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, snapshot.State);

        store.UntrackDocument(path);
        Assert.IsNull(store.GetSnapshot(path));
    }


    [TestMethod]
    public void Publish_OlderVersionForUnopenedFileDoesNotReplaceNewerPublication()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "dependency.rocket");
        var uri = new Uri(path).AbsoluteUri;
        var store = new LiveDiagnosticsStore();
        store.BeginSession(6, isOnline: true);
        Assert.IsTrue(store.Publish(6, uri, path, version: 8, [Diagnostic(path, "R8000")]));

        Assert.IsFalse(store.Publish(6, uri, path, version: 7, [Diagnostic(path, "R7000")]));

        var snapshot = store.GetSnapshot(path)!;
        Assert.AreEqual(8, snapshot.PublicationVersion);
        Assert.AreEqual("R8000", snapshot.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void UntrackDocument_RejectsLateNonEmptyPublicationUntilDocumentIsReopened()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "close-race.rocket");
        var uri = new Uri(path).AbsoluteUri;
        var store = new LiveDiagnosticsStore();
        store.BeginSession(7, isOnline: true);
        store.TrackDocument(path, version: 3, isSupported: true);
        Assert.IsTrue(store.Publish(7, uri, path, 3, [Diagnostic(path, "R2001")]));

        store.UntrackDocument(path);

        Assert.IsFalse(store.Publish(7, uri, path, 3, [Diagnostic(path, "R9999")]));
        Assert.IsNull(store.GetSnapshot(path));

        store.TrackDocument(path, version: 4, isSupported: true);
        Assert.IsTrue(store.Publish(7, uri, path, 4, [Diagnostic(path, "R4002")]));
        Assert.AreEqual("R4002", store.GetSnapshot(path)!.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void UntrackDocument_ThenEmptyClosePublicationDoesNotCreateGhostEntry()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "close.rocket");
        var uri = new Uri(path).AbsoluteUri;
        var store = new LiveDiagnosticsStore();
        store.BeginSession(7, isOnline: true);
        store.TrackDocument(path, version: 3, isSupported: true);
        Assert.IsTrue(store.Publish(7, uri, path, 3, [Diagnostic(path, "R2001")]));

        store.UntrackDocument(path);
        Assert.IsTrue(store.Publish(7, uri, path, 3, []));

        Assert.IsNull(store.GetSnapshot(path));
    }

    [TestMethod]
    public void Publish_PreservesNormalizedDocumentUriInSnapshot()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "uri.rocket");
        var uri = new Uri(path).AbsoluteUri;
        var store = new LiveDiagnosticsStore();
        store.BeginSession(8, isOnline: true);

        Assert.IsTrue(store.Publish(8, uri, path, 1, [Diagnostic(path, "R1001")]));

        Assert.AreEqual(new Uri(uri).AbsoluteUri, store.GetSnapshot(path)!.DocumentUri);
    }

    private static string UriFor(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static RocketDiagnostic Diagnostic(string path, string code) =>
        new("rocketc", code, "message", DiagnosticSeverity.Error, path, new SourceRange(0, 0, 0, 1));

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-diag-store-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
