using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Editor.Completion;

internal sealed class RocketCompletionController : IDisposable
{
    private static readonly TimeSpan TypingDebounce = TimeSpan.FromMilliseconds(140);
    private readonly TextEditor _editor;
    private readonly Func<IRocketEditorFeatureService?> _serviceProvider;
    private CancellationTokenSource? _requestCancellation;
    private CompletionWindow? _window;
    private DocumentTabViewModel? _document;
    private bool _disposed;

    public RocketCompletionController(TextEditor editor, Func<IRocketEditorFeatureService?> serviceProvider)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _editor.TextArea.TextEntered += TextArea_TextEntered;
        _editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
    }

    public void Attach(DocumentTabViewModel document)
    {
        Detach();
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _editor.Document.Changed += EditorDocument_Changed;
    }

    public void Detach()
    {
        CancelPending();
        CloseWindow();
        if (_document is not null)
        {
            _editor.Document.Changed -= EditorDocument_Changed;
            _document = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Detach();
        _editor.TextArea.TextEntered -= TextArea_TextEntered;
        _editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        _editor.TextArea.PreviewKeyDown -= TextArea_PreviewKeyDown;
    }

    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control && _document is not null)
        {
            e.Handled = true;
            _ = ScheduleRequestAsync(null, TimeSpan.Zero);
        }
    }

    private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (_document is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var service = _serviceProvider();
        var trigger = service?.CompletionTriggerCharacters.Contains(e.Text, StringComparer.Ordinal) == true
            ? e.Text
            : null;
        var first = e.Text[0];
        if (trigger is not null || char.IsLetterOrDigit(first) || first == '_')
        {
            _ = ScheduleRequestAsync(trigger, TypingDebounce);
        }
    }

    private void EditorDocument_Changed(object? sender, DocumentChangeEventArgs e)
    {
        CancelPending();
        CloseWindow();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        CancelPending();
        CloseWindow();
    }

    private async Task ScheduleRequestAsync(string? triggerCharacter, TimeSpan delay)
    {
        var document = _document;
        var service = _serviceProvider();
        if (document is null || service is null)
        {
            return;
        }

        CancelPending();
        var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        var requestVersion = document.Version;
        var caretOffset = _editor.TextArea.Caret.Offset;
        var position = new LspPosition(
            Math.Max(0, _editor.TextArea.Caret.Line - 1),
            Math.Max(0, _editor.TextArea.Caret.Column - 1));
        var fallbackStart = FindIdentifierStart(caretOffset);

        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellation.Token);
            }

            var result = await service.RequestCompletionAsync(document.Path, position, triggerCharacter, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(document, _document) ||
                requestVersion != document.Version || caretOffset != _editor.TextArea.Caret.Offset ||
                result is null || result.Items.Count == 0)
            {
                return;
            }

            CloseWindow();
            var window = new CompletionWindow(_editor.TextArea);
            ApplyDarkTheme(window);
            foreach (var item in result.Items.OrderBy(item => item.SortText ?? item.Label, StringComparer.OrdinalIgnoreCase))
            {
                window.CompletionList.CompletionData.Add(new RocketCompletionData(item, fallbackStart, requestVersion));
            }
            window.Closed += CompletionWindow_Closed;
            _window = window;
            window.Show();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, cancellation))
            {
                _requestCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void ApplyDarkTheme(CompletionWindow window)
    {
        var background = FindBrush("IDE.ChromeRaisedBrush", Brushes.DimGray);
        var foreground = FindBrush("IDE.TextBrush", Brushes.WhiteSmoke);
        var border = FindBrush("IDE.BorderBrush", Brushes.Gray);
        var selection = FindBrush("IDE.SelectionBrush", Brushes.SteelBlue);

        window.Background = background;
        window.Foreground = foreground;
        window.BorderBrush = border;
        window.Resources[SystemColors.WindowBrushKey] = background;
        window.Resources[SystemColors.ControlBrushKey] = background;
        window.Resources[SystemColors.ControlTextBrushKey] = foreground;
        window.Resources[SystemColors.HighlightBrushKey] = selection;
        window.Resources[SystemColors.HighlightTextBrushKey] = foreground;
        var itemStyle = _editor.TryFindResource(typeof(ListBoxItem)) as Style;
        if (itemStyle is not null)
        {
            window.Resources[typeof(ListBoxItem)] = itemStyle;
        }

        window.CompletionList.Background = background;
        window.CompletionList.Foreground = foreground;
        var listBox = window.CompletionList.ListBox;
        if (listBox is not null)
        {
            listBox.Background = background;
            listBox.Foreground = foreground;
            listBox.BorderBrush = border;
            if (itemStyle is not null)
            {
                listBox.ItemContainerStyle = itemStyle;
            }
        }
    }

    private Brush FindBrush(string resourceKey, Brush fallback) =>
        _editor.TryFindResource(resourceKey) as Brush ?? fallback;

    private int FindIdentifierStart(int caretOffset)
    {
        var offset = Math.Clamp(caretOffset, 0, _editor.Document.TextLength);
        while (offset > 0)
        {
            var character = _editor.Document.GetCharAt(offset - 1);
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                break;
            }
            offset--;
        }
        return offset;
    }

    private void CompletionWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is CompletionWindow window)
        {
            window.Closed -= CompletionWindow_Closed;
        }
        if (ReferenceEquals(sender, _window))
        {
            _window = null;
        }
    }

    private void CancelPending()
    {
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        if (cancellation is not null)
        {
            cancellation.Cancel();
        }
    }

    private void CloseWindow()
    {
        var window = _window;
        _window = null;
        if (window is not null)
        {
            window.Closed -= CompletionWindow_Closed;
            window.Close();
        }
    }
}
