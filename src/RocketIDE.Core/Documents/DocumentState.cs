using System.Text;

namespace RocketIDE.Core.Documents;

public sealed class DocumentState
{
    private string _text;
    private string _savedText;
    private int _version;
    private bool _isDirty;
    private bool _forceDirty;
    private long _byteLength;

    public DocumentState(DocumentId id, string path, string text, long byteLength)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A document path is required.", nameof(path));
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        Id = id;
        Path = path;
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _savedText = text;
        _byteLength = byteLength;
    }

    public DocumentId Id { get; }

    public string Path { get; }

    public DocumentSnapshot Snapshot => new(Id, Path, _text, _version, _isDirty, _byteLength);

    public DocumentSnapshot ApplyEdit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(_text, text, StringComparison.Ordinal))
        {
            return Snapshot;
        }

        _text = text;
        _byteLength = Encoding.UTF8.GetByteCount(text);
        _version = checked(_version + 1);
        _isDirty = _forceDirty || !string.Equals(_text, _savedText, StringComparison.Ordinal);
        return Snapshot;
    }

    public DocumentSnapshot ReplaceFromDisk(string text, long byteLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        if (!string.Equals(_text, text, StringComparison.Ordinal))
        {
            _text = text;
            _version = checked(_version + 1);
        }

        _savedText = text;
        _byteLength = byteLength;
        _forceDirty = false;
        _isDirty = false;
        return Snapshot;
    }

    public DocumentSnapshot MarkPersisted(string persistedText, long persistedByteLength)
    {
        ArgumentNullException.ThrowIfNull(persistedText);
        if (persistedByteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(persistedByteLength));
        }

        _savedText = persistedText;
        _forceDirty = false;
        if (string.Equals(_text, persistedText, StringComparison.Ordinal))
        {
            _byteLength = persistedByteLength;
        }

        _isDirty = !string.Equals(_text, _savedText, StringComparison.Ordinal);
        return Snapshot;
    }

    public DocumentSnapshot MarkRecoveredUnsaved(int bufferVersion)
    {
        if (bufferVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferVersion));
        }

        _version = Math.Max(_version, bufferVersion);
        _forceDirty = true;
        _isDirty = true;
        return Snapshot;
    }

    public DocumentSnapshot MarkSaved(long byteLength) => MarkPersisted(_text, byteLength);
}
