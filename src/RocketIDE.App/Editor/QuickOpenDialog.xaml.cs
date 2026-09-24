using System.IO;
using System.Windows;
using System.Windows.Input;
using RocketIDE.App.Integration;

namespace RocketIDE.App.Editor;

public partial class QuickOpenDialog : Window
{
    private const int MaximumFiles = 10_000;
    private const int MaximumResults = 250;

    private readonly string _rootPath;
    private readonly QuickOpenSearchService _searchService;
    private readonly IReadOnlyCollection<string> _openPaths;
    private readonly IReadOnlyList<string> _recentPaths;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _queryCancellation;
    private IReadOnlyList<string> _files = [];
    private long _queryGeneration;

    public QuickOpenDialog(
        string rootPath,
        QuickOpenSearchService searchService,
        IReadOnlyCollection<string> openPaths,
        IReadOnlyList<string> recentPaths)
    {
        InitializeComponent();
        _rootPath = Path.GetFullPath(rootPath);
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _openPaths = openPaths ?? throw new ArgumentNullException(nameof(openPaths));
        _recentPaths = recentPaths ?? throw new ArgumentNullException(nameof(recentPaths));
    }

    public string? SelectedPath { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
        StatusTextBlock.Text = "Scanning workspace…";
        try
        {
            _files = await _searchService.DiscoverAsync(_rootPath, MaximumFiles, _lifetimeCancellation.Token);
            await RefreshItemsAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusTextBlock.Text = $"Quick Open scan failed: {exception.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetimeCancellation.Cancel();
        var query = Interlocked.Exchange(ref _queryCancellation, null);
        query?.Cancel();
        query?.Dispose();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    private async void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        try
        {
            await RefreshItemsAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshItemsAsync()
    {
        if (FilesList is null || FilterBox is null || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _queryGeneration);
        var previous = Interlocked.Exchange(
            ref _queryCancellation,
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token));
        previous?.Cancel();
        previous?.Dispose();
        var cancellation = _queryCancellation;
        if (cancellation is null)
        {
            return;
        }

        var query = FilterBox.Text.Trim();
        var files = _files;
        var results = await _searchService.SearchAsync(
            _rootPath,
            files,
            query,
            _openPaths,
            _recentPaths,
            MaximumResults,
            cancellation.Token);

        if (cancellation.IsCancellationRequested || generation != Volatile.Read(ref _queryGeneration))
        {
            return;
        }

        FilesList.ItemsSource = results;
        if (FilesList.Items.Count > 0)
        {
            FilesList.SelectedIndex = 0;
        }
        StatusTextBlock.Text = files.Count >= MaximumFiles
            ? $"Showing {results.Count} matches · scanned first {MaximumFiles:N0} files"
            : $"Showing {results.Count} matches · {files.Count:N0} files indexed";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && FilterBox.IsKeyboardFocusWithin && FilesList.Items.Count > 0)
        {
            FilesList.Focus();
            FilesList.SelectedIndex = Math.Max(0, FilesList.SelectedIndex);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
        }
    }

    private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void Open_Click(object sender, RoutedEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (FilesList.SelectedItem is not QuickOpenMatch item)
        {
            return;
        }

        SelectedPath = item.FullPath;
        DialogResult = true;
    }
}
