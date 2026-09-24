using System.IO;
using RocketIDE.App.Editor;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Integration;

public sealed record NavigationLocation(string Path, SourceRange Range);

public sealed class NavigationHistoryService
{
    private readonly IEditorContext _editorContext;
    private readonly IEditorNavigation _navigation;
    private readonly int _capacity;
    private readonly List<NavigationLocation> _back = [];
    private readonly List<NavigationLocation> _forward = [];

    public NavigationHistoryService(IEditorContext editorContext, IEditorNavigation navigation, int capacity = 100)
    {
        _editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public event EventHandler? Changed;
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public async Task<bool> NavigateAsync(string path, SourceRange range, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(range);
        var source = CaptureCurrent();
        var destination = new NavigationLocation(Path.GetFullPath(path), range);
        var view = await _navigation.OpenOrRevealAsync(destination.Path, destination.Range, cancellationToken);
        if (view is null) return false;
        if (source is not null && !Equivalent(source, destination)) PushBounded(_back, source);
        _forward.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> GoBackAsync(CancellationToken cancellationToken)
    {
        if (_back.Count == 0) return false;
        var target = _back[^1];
        var current = CaptureCurrent();
        var view = await _navigation.OpenOrRevealAsync(target.Path, target.Range, cancellationToken);
        if (view is null) return false;
        _back.RemoveAt(_back.Count - 1);
        if (current is not null && !Equivalent(current, target)) PushBounded(_forward, current);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> GoForwardAsync(CancellationToken cancellationToken)
    {
        if (_forward.Count == 0) return false;
        var target = _forward[^1];
        var current = CaptureCurrent();
        var view = await _navigation.OpenOrRevealAsync(target.Path, target.Range, cancellationToken);
        if (view is null) return false;
        _forward.RemoveAt(_forward.Count - 1);
        if (current is not null && !Equivalent(current, target)) PushBounded(_back, current);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private NavigationLocation? CaptureCurrent()
    {
        var view = _editorContext.ActiveView;
        var target = view?.CommandTarget;
        if (view is null || target is null) return null;
        var line = Math.Max(0, target.CaretLine - 1);
        var column = Math.Max(0, target.CaretColumn - 1);
        return new NavigationLocation(
            Path.GetFullPath(view.Document.Path),
            new SourceRange(line, column, line, column));
    }

    private void PushBounded(List<NavigationLocation> list, NavigationLocation location)
    {
        if (list.Count > 0 && Equivalent(list[^1], location)) return;
        list.Add(location);
        if (list.Count > _capacity) list.RemoveAt(0);
    }

    private static bool Equivalent(NavigationLocation left, NavigationLocation right) =>
        string.Equals(left.Path, right.Path, PathComparison) && left.Range == right.Range;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
