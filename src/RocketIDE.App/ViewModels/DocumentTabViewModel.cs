using System.ComponentModel;
using System.Runtime.CompilerServices;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.ViewModels;

public sealed class DocumentTabViewModel : INotifyPropertyChanged
{
    private readonly IDocumentStore _documentStore;
    private DocumentSnapshot _snapshot;
    private bool _suppressEditorDocumentChange;
    private int _caretLine = 1;
    private int _caretColumn = 1;

    public DocumentTabViewModel(IDocumentStore documentStore, DocumentSnapshot snapshot)
    {
        _documentStore = documentStore;
        _snapshot = snapshot;
        EditorDocument = new TextDocument(snapshot.Text);
        EditorDocument.UndoStack.MarkAsOriginalFile();
        EditorDocument.Changed += EditorDocument_Changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CaretChanged;

    public DocumentId Id => _snapshot.Id;

    public string Path => _snapshot.Path;

    public string Text => _snapshot.Text;

    public int Version => _snapshot.Version;

    public long ByteLength => _snapshot.ByteLength;

    public bool IsDirty => _snapshot.IsDirty;

    public string DisplayName => System.IO.Path.GetFileName(_snapshot.Path);

    public string HeaderText => _snapshot.IsDirty ? $"{DisplayName} ●" : DisplayName;

    public int CaretLine => _caretLine;

    public int CaretColumn => _caretColumn;

    public TextDocument EditorDocument { get; }

    public void UpdateSnapshot(DocumentSnapshot snapshot)
    {
        if (snapshot.Id != Id)
        {
            throw new ArgumentException("Cannot apply a different document to this tab.", nameof(snapshot));
        }

        _snapshot = snapshot;

        if (!string.Equals(EditorDocument.Text, snapshot.Text, StringComparison.Ordinal))
        {
            _suppressEditorDocumentChange = true;
            try
            {
                EditorDocument.Text = snapshot.Text;
                EditorDocument.UndoStack.ClearAll();
            }
            finally
            {
                _suppressEditorDocumentChange = false;
            }
        }

        if (!snapshot.IsDirty)
        {
            EditorDocument.UndoStack.MarkAsOriginalFile();
        }

        RaiseSnapshotPropertiesChanged();
    }

    public void UpdateCaret(int line, int column)
    {
        line = Math.Max(1, line);
        column = Math.Max(1, column);

        if (_caretLine == line && _caretColumn == column)
        {
            return;
        }

        _caretLine = line;
        _caretColumn = column;
        OnPropertyChanged(nameof(CaretLine));
        OnPropertyChanged(nameof(CaretColumn));
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EditorDocument_Changed(object? sender, DocumentChangeEventArgs e)
    {
        if (_suppressEditorDocumentChange)
        {
            return;
        }

        _snapshot = _documentStore.UpdateText(Id, EditorDocument.Text);
        RaiseSnapshotPropertiesChanged();
    }

    private void RaiseSnapshotPropertiesChanged()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(ByteLength));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(HeaderText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
