using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.ViewModels;

public enum OutlineState
{
    Unavailable,
    Loading,
    Ready,
    Empty,
    Error,
}

public sealed class OutlineNodeViewModel
{
    public OutlineNodeViewModel(RocketDocumentSymbol symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        Children = new ObservableCollection<OutlineNodeViewModel>(symbol.Children.Select(child => new OutlineNodeViewModel(child)));
    }

    public RocketDocumentSymbol Symbol { get; }
    public string Name => Symbol.Name;
    public string KindText => SymbolKindNames.GetName(Symbol.Kind);
    public ObservableCollection<OutlineNodeViewModel> Children { get; }
}

public static class SymbolKindNames
{
    public static string GetName(int kind) => kind switch
    {
        1 => "File",
        2 => "Module",
        3 => "Namespace",
        4 => "Package",
        5 => "Class",
        6 => "Method",
        7 => "Property",
        8 => "Field",
        9 => "Constructor",
        10 => "Enum",
        11 => "Interface",
        12 => "Function",
        13 => "Variable",
        14 => "Constant",
        15 => "String",
        16 => "Number",
        17 => "Boolean",
        18 => "Array",
        19 => "Object",
        20 => "Key",
        21 => "Null",
        22 => "Enum Member",
        23 => "Struct",
        24 => "Event",
        25 => "Operator",
        26 => "Type Parameter",
        _ => "Symbol",
    };
}

public sealed class OutlineViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEditorContext _editorContext;
    private readonly DocumentSymbolService _symbols;
    private readonly NavigationHistoryService _navigation;
    private readonly CancellationToken _workToken;
    private CancellationTokenSource? _refreshCancellation;
    private DocumentTabViewModel? _observedDocument;
    private OutlineState _state = OutlineState.Unavailable;
    private string _statusText = "Open a Rocket document to view its outline.";
    private long _refreshSerial;

    public OutlineViewModel(
        IEditorContext editorContext,
        DocumentSymbolService symbols,
        NavigationHistoryService navigation,
        CancellationToken workToken)
    {
        _editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _workToken = workToken;
        _editorContext.ActiveContextChanged += EditorContext_ActiveContextChanged;
        ObserveActiveDocument();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<OutlineNodeViewModel> Items { get; } = new();

    public OutlineState State
    {
        get => _state;
        private set => SetField(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public Task RefreshAsync() => RefreshCoreAsync(debounce: false);

    public async Task NavigateAsync(OutlineNodeViewModel node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var symbol = node.Symbol;
        var range = new SourceRange(
            symbol.SelectionRange.Start.Line,
            symbol.SelectionRange.Start.Character,
            symbol.SelectionRange.End.Line,
            symbol.SelectionRange.End.Character);
        await _navigation.NavigateAsync(symbol.Path, range, cancellationToken);
    }

    private void EditorContext_ActiveContextChanged(object? sender, EventArgs e)
    {
        ObserveActiveDocument();
        _ = RefreshCoreAsync(debounce: false);
    }

    private void ObserveActiveDocument()
    {
        if (_observedDocument is not null) _observedDocument.PropertyChanged -= Document_PropertyChanged;
        _observedDocument = _editorContext.ActiveDocument;
        if (_observedDocument is not null) _observedDocument.PropertyChanged += Document_PropertyChanged;
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentTabViewModel.Version)) return;
        if (sender is DocumentTabViewModel document) _symbols.Invalidate(document.Path);
        _ = RefreshCoreAsync(debounce: true);
    }

    private async Task RefreshCoreAsync(bool debounce)
    {
        var serial = Interlocked.Increment(ref _refreshSerial);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_workToken);
        var previous = Interlocked.Exchange(ref _refreshCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            if (debounce) await Task.Delay(225, cancellation.Token);
            var document = _editorContext.ActiveDocument;
            Items.Clear();
            if (document is null || !string.Equals(Path.GetExtension(document.Path), ".rocket", StringComparison.OrdinalIgnoreCase))
            {
                State = OutlineState.Unavailable;
                StatusText = "Open a Rocket document to view its outline.";
                return;
            }
            if (!document.AllowLsp)
            {
                State = OutlineState.Unavailable;
                StatusText = "Document symbols are unavailable for this large file.";
                return;
            }

            State = OutlineState.Loading;
            StatusText = "Loading symbols from rocket-lsp…";
            var snapshot = await _symbols.GetAsync(document, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (serial != Volatile.Read(ref _refreshSerial) || !ReferenceEquals(document, _editorContext.ActiveDocument)) return;
            if (snapshot is null)
            {
                State = OutlineState.Unavailable;
                StatusText = "Document symbols are unavailable from rocket-lsp.";
                return;
            }

            foreach (var symbol in snapshot.Symbols) Items.Add(new OutlineNodeViewModel(symbol));
            State = Items.Count == 0 ? OutlineState.Empty : OutlineState.Ready;
            StatusText = Items.Count == 0 ? "No symbols in this document." : $"{Items.Count} top-level symbol(s)";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            if (serial != Volatile.Read(ref _refreshSerial)) return;
            Items.Clear();
            State = OutlineState.Error;
            StatusText = $"Outline unavailable: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, cancellation), cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _editorContext.ActiveContextChanged -= EditorContext_ActiveContextChanged;
        if (_observedDocument is not null) _observedDocument.PropertyChanged -= Document_PropertyChanged;
        var cancellation = Interlocked.Exchange(ref _refreshCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
