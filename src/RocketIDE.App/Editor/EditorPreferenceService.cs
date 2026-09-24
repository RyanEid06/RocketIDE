using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.App.Editor;

public sealed class EditorPreferenceService
{
    private EditorPreferences _current = EditorPreferences.Default;

    public event EventHandler? Changed;

    public EditorPreferences Current => _current;

    public void Apply(EditorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        SetCurrent(preferences.Normalize());
    }

    public void ZoomIn() => SetFontSize(_current.FontSize + 1.0);

    public void ZoomOut() => SetFontSize(_current.FontSize - 1.0);

    public void ResetZoom() => SetFontSize(EditorPreferences.DefaultFontSize);

    public void SetWordWrap(bool enabled) => SetCurrent(_current with { WordWrap = enabled });

    public void SetFormatOnSave(bool enabled) => SetCurrent(_current with { FormatOnSave = enabled });

    private void SetFontSize(double fontSize) =>
        SetCurrent(_current with
        {
            FontSize = Math.Clamp(fontSize, EditorPreferences.MinimumFontSize, EditorPreferences.MaximumFontSize),
        });

    private void SetCurrent(EditorPreferences preferences)
    {
        if (_current == preferences)
        {
            return;
        }
        _current = preferences;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public interface IEditorPresentationTarget
{
    void ApplyEditorPreferences(EditorPreferences preferences);
}

public partial class EditorDocumentHost : IEditorPresentationTarget
{
    public void ApplyEditorPreferences(EditorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = preferences.Normalize();
        Editor.FontSize = normalized.FontSize;
        Editor.WordWrap = normalized.WordWrap;
    }
}
