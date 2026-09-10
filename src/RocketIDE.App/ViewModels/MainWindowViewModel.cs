using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private DocumentTabViewModel? _activeDocument;
    private string _rocketSdkStatus = "Rocket SDK: not configured";
    private string _lspStatus = "LSP: offline";
    private string _activeTargetStatus = "Target: none";
    private string _caretStatus = "Ln 1, Col 1";
    private string _encodingStatus = "UTF-8";

    public MainWindowViewModel()
    {
        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDocuments));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DocumentTabViewModel> Documents { get; } = new();

    public bool HasDocuments => Documents.Count > 0;

    public DocumentTabViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (ReferenceEquals(_activeDocument, value))
            {
                return;
            }

            if (_activeDocument is not null)
            {
                _activeDocument.CaretChanged -= ActiveDocument_CaretChanged;
            }

            _activeDocument = value;

            if (_activeDocument is not null)
            {
                _activeDocument.CaretChanged += ActiveDocument_CaretChanged;
            }

            UpdateCaretStatus();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle => ActiveDocument is null ? "RocketIDE" : $"{ActiveDocument.DisplayName} — RocketIDE";

    public string RocketSdkStatus
    {
        get => _rocketSdkStatus;
        set => SetField(ref _rocketSdkStatus, value);
    }

    public string LspStatus
    {
        get => _lspStatus;
        set => SetField(ref _lspStatus, value);
    }

    public string ActiveTargetStatus
    {
        get => _activeTargetStatus;
        set => SetField(ref _activeTargetStatus, value);
    }

    public string CaretStatus
    {
        get => _caretStatus;
        private set => SetField(ref _caretStatus, value);
    }

    public string EncodingStatus
    {
        get => _encodingStatus;
        set => SetField(ref _encodingStatus, value);
    }

    public DocumentTabViewModel AddOrActivate(IDocumentStore documentStore, DocumentSnapshot snapshot)
    {
        var existing = Documents.FirstOrDefault(document => document.Id == snapshot.Id);
        if (existing is not null)
        {
            ActiveDocument = existing;
            return existing;
        }

        var tab = new DocumentTabViewModel(documentStore, snapshot);
        Documents.Add(tab);
        ActiveDocument = tab;
        return tab;
    }

    public void Remove(DocumentTabViewModel tab)
    {
        var index = Documents.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasActive = ReferenceEquals(ActiveDocument, tab);
        Documents.RemoveAt(index);

        if (wasActive)
        {
            ActiveDocument = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
        }
    }

    private void ActiveDocument_CaretChanged(object? sender, EventArgs e) => UpdateCaretStatus();

    private void UpdateCaretStatus()
    {
        CaretStatus = ActiveDocument is null
            ? "Ln 1, Col 1"
            : $"Ln {ActiveDocument.CaretLine}, Col {ActiveDocument.CaretColumn}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
