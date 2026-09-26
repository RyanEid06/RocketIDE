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
    private string? _compilerDiagnostic;
    private bool _hasSummary;

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
        _compilerDiagnostic = null;
        _hasSummary = false;
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
            case "diagnostic" when message.Level == "error":
                _compilerDiagnostic = $"{message.Code}: {message.Message}";
                break;
            case "test-summary":
                _hasSummary = true;
                SummaryText = $"{message.Passed ?? 0} passed · {message.Failed ?? 0} failed · {message.ExpectedFailures ?? 0} expected failure(s) · {message.Selected ?? 0} selected";
                break;
        }
        HeaderText = Items.Count == 0 ? "TESTS" : $"TESTS ({Items.Count})";
    }

    public void CompleteRun(int? exitCode, bool cancelled, string? error)
    {
        foreach (var item in Items.Where(item => item.Status == "RUNNING"))
            item.Finish(cancelled ? "CANCELLED" : "INCOMPLETE", null);

        var terminal = cancelled ? "Test run cancelled."
            : error is not null ? $"Test run failed: {error}"
            : exitCode is { } code ? $"Test process exited with code {code}."
            : "Test run ended without a process result.";
        SummaryText = _hasSummary ? $"{SummaryText} - {terminal}"
            : $"{terminal} No test summary received.";
        if (_compilerDiagnostic is not null) SummaryText += $" {_compilerDiagnostic}";
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
