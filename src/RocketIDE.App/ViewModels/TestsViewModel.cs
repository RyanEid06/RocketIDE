using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.Rocket.Compiler;

namespace RocketIDE.App.ViewModels;

public sealed class RocketTestItemViewModel : INotifyPropertyChanged
{
    private string _status = "RUNNING";
    private int? _exitCode;

    public RocketTestItemViewModel(string name)
    {
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; }

    public string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public int? ExitCode
    {
        get => _exitCode;
        private set
        {
            if (_exitCode == value) return;
            _exitCode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExitCode)));
        }
    }

    public void Finish(string? status, int? exitCode)
    {
        Status = string.IsNullOrWhiteSpace(status) ? "DONE" : status.Trim().ToUpperInvariant();
        ExitCode = exitCode;
    }
}

public sealed class TestsViewModel : INotifyPropertyChanged
{
    private string _summaryText = "No test run yet.";
    private string _headerText = "TESTS";

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<RocketTestItemViewModel> Items { get; } = new();

    public string SummaryText
    {
        get => _summaryText;
        private set => SetField(ref _summaryText, value);
    }

    public string HeaderText
    {
        get => _headerText;
        private set => SetField(ref _headerText, value);
    }

    public void BeginRun()
    {
        Items.Clear();
        SummaryText = "Test run started…";
        HeaderText = "TESTS";
    }

    public void Apply(RocketMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message.Reason)
        {
            case "test-started" when !string.IsNullOrWhiteSpace(message.Name):
                FindOrCreate(message.Name!);
                break;
            case "test-finished" when !string.IsNullOrWhiteSpace(message.Name):
                FindOrCreate(message.Name!).Finish(message.Status, message.ExitCode);
                break;
            case "test-summary":
                SummaryText = $"{message.Passed ?? 0} passed · {message.Failed ?? 0} failed · {message.ExpectedFailures ?? 0} expected failure(s) · {message.Selected ?? 0} selected";
                break;
        }
        HeaderText = Items.Count == 0 ? "TESTS" : $"TESTS ({Items.Count})";
    }

    private RocketTestItemViewModel FindOrCreate(string name)
    {
        var existing = Items.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }
        var created = new RocketTestItemViewModel(name);
        Items.Add(created);
        return created;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
