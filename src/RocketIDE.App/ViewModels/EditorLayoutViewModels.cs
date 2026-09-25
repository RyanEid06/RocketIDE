using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.App.Editor;
using RocketIDE.App.Editor.Snippets;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Recovery;

namespace RocketIDE.App.ViewModels;

public enum EditorSplitOrientation
{
    None,
    Vertical,
    Horizontal,
}

public sealed class EditorViewNavigationRequestedEventArgs(SourceRange range) : EventArgs
{
    public SourceRange Range { get; } = range ?? throw new ArgumentNullException(nameof(range));
}

public sealed class EditorViewViewModel : INotifyPropertyChanged, IEditorViewContext, IDisposable
{
    private IEditorCommandTarget? _commandTarget;
    private int _caretLine = 1;
    private int _caretColumn = 1;
    private int _caretOffset;
    private int _selectionStart;
    private int _selectionLength;
    private double _horizontalOffset;
    private double _verticalOffset;
    private IReadOnlyList<SourceRange> _collapsedFolds = [];
    private SourceRange? _pendingNavigation;
    private RocketSnippetSession? _snippetSession;

    public EditorViewViewModel(DocumentTabViewModel document, Guid? viewId = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        ViewId = viewId ?? Guid.NewGuid();
        Document.PropertyChanged += Document_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? FocusRequested;
    public event EventHandler<EditorViewNavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler? ViewStateChanged;

    public Guid ViewId { get; }
    public DocumentTabViewModel Document { get; }
    public IEditorCommandTarget? CommandTarget => _commandTarget;
    public string HeaderText => Document.HeaderText;
    public string DisplayName => Document.DisplayName;
    public int CaretLine => _caretLine;
    public int CaretColumn => _caretColumn;
    public int CaretOffset => _caretOffset;
    public int SelectionStart => _selectionStart;
    public int SelectionLength => _selectionLength;
    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;
    public IReadOnlyList<SourceRange> CollapsedFolds => _collapsedFolds;
    public RocketSnippetSession? SnippetSession
    {
        get => _snippetSession;
        set => SetField(ref _snippetSession, value, notifySession: false);
    }

    public void Focus()
    {
        FocusRequested?.Invoke(this, EventArgs.Empty);
        _commandTarget?.FocusEditor();
    }

    public void AttachCommandTarget(IEditorCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(_commandTarget, target)) return;
        _commandTarget = target;
        OnPropertyChanged(nameof(CommandTarget));
    }

    public void DetachCommandTarget(IEditorCommandTarget target)
    {
        if (!ReferenceEquals(_commandTarget, target)) return;
        SnippetSession?.Cancel();
        SnippetSession = null;
        _commandTarget = null;
        OnPropertyChanged(nameof(CommandTarget));
    }

    public void UpdateCaret(int line, int column, int offset)
    {
        line = Math.Max(1, line);
        column = Math.Max(1, column);
        offset = Math.Max(0, offset);
        if (_caretLine == line && _caretColumn == column && _caretOffset == offset) return;
        _caretLine = line;
        _caretColumn = column;
        _caretOffset = offset;
        OnPropertyChanged(nameof(CaretLine));
        OnPropertyChanged(nameof(CaretColumn));
        OnPropertyChanged(nameof(CaretOffset));
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSelection(int start, int length)
    {
        start = Math.Max(0, start);
        length = Math.Max(0, length);
        if (_selectionStart == start && _selectionLength == length) return;
        _selectionStart = start;
        _selectionLength = length;
        OnPropertyChanged(nameof(SelectionStart));
        OnPropertyChanged(nameof(SelectionLength));
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateScroll(double horizontalOffset, double verticalOffset)
    {
        horizontalOffset = SanitizeOffset(horizontalOffset);
        verticalOffset = SanitizeOffset(verticalOffset);
        if (_horizontalOffset.Equals(horizontalOffset) && _verticalOffset.Equals(verticalOffset)) return;
        _horizontalOffset = horizontalOffset;
        _verticalOffset = verticalOffset;
        OnPropertyChanged(nameof(HorizontalOffset));
        OnPropertyChanged(nameof(VerticalOffset));
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCollapsedFolds(IEnumerable<SourceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var normalized = ranges
            .Where(IsValidRange)
            .Distinct()
            .ToArray();
        if (_collapsedFolds.SequenceEqual(normalized)) return;
        _collapsedFolds = normalized;
        OnPropertyChanged(nameof(CollapsedFolds));
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestNavigation(SourceRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        _pendingNavigation = range;
        NavigationRequested?.Invoke(this, new EditorViewNavigationRequestedEventArgs(range));
    }

    public SourceRange? TakePendingNavigation()
    {
        var pending = _pendingNavigation;
        _pendingNavigation = null;
        return pending;
    }

    public void RestoreState(EditorViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _caretOffset = Math.Max(0, state.CaretOffset);
        _selectionStart = Math.Max(0, state.SelectionStart);
        _selectionLength = Math.Max(0, state.SelectionLength);
        _horizontalOffset = SanitizeOffset(state.HorizontalOffset);
        _verticalOffset = SanitizeOffset(state.VerticalOffset);
        _collapsedFolds = (state.CollapsedFolds ?? [])
            .Where(range => range is not null)
            .Select(range => new SourceRange(range.StartLine, range.StartCharacter, range.EndLine, range.EndCharacter))
            .Where(IsValidRange)
            .ToArray();
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Document.PropertyChanged -= Document_PropertyChanged;
        SnippetSession?.Cancel();
        SnippetSession = null;
        _commandTarget = null;
    }

    public EditorViewState CaptureState() => new()
    {
        ViewId = ViewId.ToString("D"),
        DocumentPath = Document.Path,
        CaretOffset = CaretOffset,
        SelectionStart = SelectionStart,
        SelectionLength = SelectionLength,
        HorizontalOffset = HorizontalOffset,
        VerticalOffset = VerticalOffset,
        CollapsedFolds = CollapsedFolds
            .Select(range => new EditorFoldState(range.StartLine, range.StartCharacter, range.EndLine, range.EndCharacter))
            .ToList(),
    };

    private static bool IsValidRange(SourceRange range) =>
        range.StartLine >= 0 && range.StartCharacter >= 0 &&
        range.EndLine >= range.StartLine && range.EndCharacter >= 0;

    private static double SanitizeOffset(double value) =>
        double.IsFinite(value) && value >= 0 ? value : 0;

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentTabViewModel.HeaderText) or nameof(DocumentTabViewModel.DisplayName))
        {
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, bool notifySession = true)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (notifySession) ViewStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class EditorGroupViewModel : INotifyPropertyChanged
{
    private EditorViewViewModel? _selectedView;

    public EditorGroupViewModel(Guid? groupId = null) => GroupId = groupId ?? Guid.NewGuid();

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid GroupId { get; }
    public ObservableCollection<EditorViewViewModel> Views { get; } = new();

    public EditorViewViewModel? SelectedView
    {
        get => _selectedView;
        set
        {
            if (ReferenceEquals(_selectedView, value)) return;
            if (value is not null && !Views.Contains(value))
                throw new InvalidOperationException("Selected editor view must belong to the group.");
            _selectedView = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedView)));
        }
    }

    public EditorViewViewModel? Find(DocumentTabViewModel document) =>
        Views.FirstOrDefault(view => ReferenceEquals(view.Document, document));

    public EditorViewViewModel Add(DocumentTabViewModel document, Guid? viewId = null)
    {
        if (Find(document) is not null)
            throw new InvalidOperationException("A document may appear at most once in an editor group.");
        var view = new EditorViewViewModel(document, viewId);
        Views.Add(view);
        SelectedView = view;
        return view;
    }

    public bool Remove(EditorViewViewModel view)
    {
        var index = Views.IndexOf(view);
        if (index < 0) return false;
        Views.RemoveAt(index);
        if (ReferenceEquals(SelectedView, view))
        {
            SelectedView = Views.Count == 0 ? null : Views[Math.Min(index, Views.Count - 1)];
        }
        return true;
    }
}

public sealed class EditorLayoutViewModel : INotifyPropertyChanged
{
    private EditorSplitOrientation _orientation;
    private EditorGroupViewModel _activeGroup;
    private EditorViewViewModel? _activeViewSubscription;

    public EditorLayoutViewModel()
    {
        var group = new EditorGroupViewModel();
        Groups.Add(group);
        _activeGroup = group;
        group.PropertyChanged += Group_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActiveContextChanged;
    public event EventHandler? LayoutChanged;
    public event EventHandler? SessionStateChanged;

    public ObservableCollection<EditorGroupViewModel> Groups { get; } = new();

    public EditorSplitOrientation Orientation
    {
        get => _orientation;
        private set
        {
            if (_orientation == value) return;
            _orientation = value;
            OnPropertyChanged();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public EditorGroupViewModel ActiveGroup
    {
        get => _activeGroup;
        private set
        {
            if (ReferenceEquals(_activeGroup, value)) return;
            _activeGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveView));
            RewireActiveView();
            ActiveContextChanged?.Invoke(this, EventArgs.Empty);
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public EditorViewViewModel? ActiveView => ActiveGroup.SelectedView;

    public int CountViews(DocumentTabViewModel document) =>
        Groups.Sum(group => group.Views.Count(view => ReferenceEquals(view.Document, document)));

    public EditorViewViewModel OpenOrActivate(DocumentTabViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var inActive = ActiveGroup.Find(document);
        if (inActive is not null)
        {
            ActivateView(inActive);
            return inActive;
        }

        var other = Groups.FirstOrDefault(group => !ReferenceEquals(group, ActiveGroup) && group.Find(document) is not null);
        if (other?.Find(document) is { } existing)
        {
            ActiveGroup = other;
            ActivateView(existing);
            return existing;
        }

        var created = AddView(ActiveGroup, document);
        ActivateView(created);
        return created;
    }

    public EditorViewViewModel SplitActive(EditorSplitOrientation orientation)
    {
        if (orientation == EditorSplitOrientation.None)
            throw new ArgumentOutOfRangeException(nameof(orientation));
        var source = ActiveView ?? throw new InvalidOperationException("There is no active editor to split.");

        if (Groups.Count == 1)
        {
            var second = new EditorGroupViewModel();
            AttachGroup(second);
            Groups.Add(second);
            Orientation = orientation;
            var duplicate = AddView(second, source.Document);
            ActiveGroup = second;
            ActivateView(duplicate);
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return duplicate;
        }

        Orientation = orientation;
        var other = Groups.First(group => !ReferenceEquals(group, ActiveGroup));
        ActiveGroup = other;
        var target = other.Find(source.Document) ?? AddView(other, source.Document);
        ActivateView(target);
        return target;
    }

    public bool FocusNextGroup()
    {
        if (Groups.Count < 2) return false;
        var index = Groups.IndexOf(ActiveGroup);
        ActiveGroup = Groups[(index + 1) % Groups.Count];
        if (ActiveGroup.SelectedView is { } selected) ActivateView(selected);
        return true;
    }

    public bool MoveActiveToOtherGroup()
    {
        if (Groups.Count < 2 || ActiveView is not { } source) return false;
        var sourceGroup = ActiveGroup;
        var targetGroup = Groups.First(group => !ReferenceEquals(group, sourceGroup));
        var existing = targetGroup.Find(source.Document);

        if (existing is not null)
        {
            RemoveViewCore(sourceGroup, source);
            ActiveGroup = targetGroup;
            ActivateView(existing);
        }
        else
        {
            sourceGroup.Remove(source);
            targetGroup.Views.Add(source);
            targetGroup.SelectedView = source;
            ActiveGroup = targetGroup;
            ActivateView(source);
        }

        CollapseEmptyGroup(sourceGroup);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool CloseView(EditorViewViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var group = Groups.FirstOrDefault(candidate => candidate.Views.Contains(view));
        if (group is null) return false;

        RemoveViewCore(group, view);
        CollapseEmptyGroup(group);
        EnsureActiveSelection();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RemoveDocument(DocumentTabViewModel document)
    {
        foreach (var group in Groups.ToArray())
        {
            foreach (var view in group.Views.Where(view => ReferenceEquals(view.Document, document)).ToArray())
            {
                RemoveViewCore(group, view);
            }
            CollapseEmptyGroup(group);
        }
        EnsureActiveSelection();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ActivateView(EditorViewViewModel view)
    {
        var group = Groups.FirstOrDefault(candidate => candidate.Views.Contains(view))
            ?? throw new InvalidOperationException("Editor view is not part of this layout.");
        var changed = !ReferenceEquals(ActiveGroup, group) || !ReferenceEquals(group.SelectedView, view);
        if (!ReferenceEquals(ActiveGroup, group)) ActiveGroup = group;
        group.SelectedView = view;
        if (changed)
        {
            OnPropertyChanged(nameof(ActiveView));
            RewireActiveView();
            ActiveContextChanged?.Invoke(this, EventArgs.Empty);
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public EditorLayoutState CaptureState() => new()
    {
        SplitOrientation = Orientation.ToString(),
        ActiveGroupId = ActiveGroup.GroupId.ToString("D"),
        ActiveViewId = ActiveView?.ViewId.ToString("D"),
        Groups = Groups.Select(group => new EditorGroupState
        {
            GroupId = group.GroupId.ToString("D"),
            SelectedViewId = group.SelectedView?.ViewId.ToString("D"),
            Views = group.Views.Select(view => view.CaptureState()).ToList(),
        }).ToList(),
    };

    public void Restore(EditorLayoutState? state, Func<string, DocumentTabViewModel?> resolveDocument)
    {
        ArgumentNullException.ThrowIfNull(resolveDocument);
        if (state?.Groups is not { Count: >= 1 and <= 2 }) return;

        var restored = new List<EditorGroupViewModel>();
        foreach (var groupState in state.Groups)
        {
            if (groupState is null) continue;
            var groupId = Guid.TryParse(groupState.GroupId, out var parsedGroupId) ? parsedGroupId : Guid.NewGuid();
            var group = new EditorGroupViewModel(groupId);
            AttachGroup(group);
            foreach (var viewState in groupState.Views ?? [])
            {
                if (viewState is null || string.IsNullOrWhiteSpace(viewState.DocumentPath)) continue;
                DocumentTabViewModel? document;
                try
                {
                    document = resolveDocument(viewState.DocumentPath);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.IOException)
                {
                    continue;
                }
                if (document is null || group.Find(document) is not null) continue;
                var viewId = Guid.TryParse(viewState.ViewId, out var parsedViewId) ? parsedViewId : Guid.NewGuid();
                var view = AddView(group, document, viewId);
                view.RestoreState(viewState);
            }
            if (group.Views.Count == 0) continue;
            group.SelectedView = group.Views.FirstOrDefault(view =>
                string.Equals(view.ViewId.ToString("D"), groupState.SelectedViewId, StringComparison.OrdinalIgnoreCase))
                ?? group.Views[0];
            restored.Add(group);
        }

        if (restored.Count == 0) return;

        foreach (var group in Groups)
        {
            foreach (var view in group.Views)
            {
                view.ViewStateChanged -= View_ViewStateChanged;
                view.Dispose();
            }
            DetachGroup(group);
        }
        Groups.Clear();
        foreach (var group in restored) Groups.Add(group);
        _activeGroup = Groups.FirstOrDefault(group =>
            string.Equals(group.GroupId.ToString("D"), state.ActiveGroupId, StringComparison.OrdinalIgnoreCase))
            ?? Groups[0];

        if (state.ActiveViewId is { Length: > 0 })
        {
            var active = Groups.SelectMany(group => group.Views).FirstOrDefault(view =>
                string.Equals(view.ViewId.ToString("D"), state.ActiveViewId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                _activeGroup = Groups.First(group => group.Views.Contains(active));
                _activeGroup.SelectedView = active;
            }
        }

        Orientation = Groups.Count == 2 && Enum.TryParse<EditorSplitOrientation>(state.SplitOrientation, true, out var orientation)
            && orientation != EditorSplitOrientation.None
            ? orientation
            : Groups.Count == 2 ? EditorSplitOrientation.Vertical : EditorSplitOrientation.None;

        RewireActiveView();
        OnPropertyChanged(nameof(ActiveGroup));
        OnPropertyChanged(nameof(ActiveView));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        ActiveContextChanged?.Invoke(this, EventArgs.Empty);
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private EditorViewViewModel AddView(EditorGroupViewModel group, DocumentTabViewModel document, Guid? viewId = null)
    {
        var view = group.Add(document, viewId);
        view.ViewStateChanged += View_ViewStateChanged;
        return view;
    }

    private void RemoveViewCore(EditorGroupViewModel group, EditorViewViewModel view)
    {
        view.ViewStateChanged -= View_ViewStateChanged;
        view.Dispose();
        group.Remove(view);
    }

    private void CollapseEmptyGroup(EditorGroupViewModel group)
    {
        if (Groups.Count != 2 || group.Views.Count != 0) return;
        DetachGroup(group);
        Groups.Remove(group);
        Orientation = EditorSplitOrientation.None;
        _activeGroup = Groups[0];
        OnPropertyChanged(nameof(ActiveGroup));
    }

    private void EnsureActiveSelection()
    {
        if (!Groups.Contains(_activeGroup)) _activeGroup = Groups[0];
        if (_activeGroup.SelectedView is null && _activeGroup.Views.Count > 0)
            _activeGroup.SelectedView = _activeGroup.Views[0];
        OnPropertyChanged(nameof(ActiveGroup));
        OnPropertyChanged(nameof(ActiveView));
        RewireActiveView();
        ActiveContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AttachGroup(EditorGroupViewModel group) => group.PropertyChanged += Group_PropertyChanged;
    private void DetachGroup(EditorGroupViewModel group) => group.PropertyChanged -= Group_PropertyChanged;

    private void Group_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorGroupViewModel.SelectedView) || !ReferenceEquals(sender, ActiveGroup)) return;
        OnPropertyChanged(nameof(ActiveView));
        RewireActiveView();
        ActiveContextChanged?.Invoke(this, EventArgs.Empty);
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RewireActiveView()
    {
        if (_activeViewSubscription is not null)
            _activeViewSubscription.PropertyChanged -= ActiveView_PropertyChanged;
        _activeViewSubscription = ActiveView;
        if (_activeViewSubscription is not null)
            _activeViewSubscription.PropertyChanged += ActiveView_PropertyChanged;
    }

    private void ActiveView_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewViewModel.CaretLine) or nameof(EditorViewViewModel.CaretColumn))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveView)));
    }

    private void View_ViewStateChanged(object? sender, EventArgs e) => SessionStateChanged?.Invoke(this, EventArgs.Empty);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
