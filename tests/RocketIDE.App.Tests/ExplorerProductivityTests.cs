using System.IO;
using RocketIDE.App.ViewModels.Explorer;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class ExplorerProductivityTests
{
    [TestMethod]
    public async Task CollapseAll_CollapsesLoadedDescendants()
    {
        using var temp = new TempDirectory();
        var src = Path.Combine(temp.Path, "src");
        var nested = Path.Combine(src, "nested");
        var fileSystem = new FakeWorkspaceFileSystem(new Dictionary<string, IReadOnlyList<WorkspaceEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [temp.Path] = [new WorkspaceEntry(src, "src", true, true)],
            [src] = [new WorkspaceEntry(nested, "nested", true, true)],
            [nested] = [new WorkspaceEntry(Path.Combine(nested, "main.rocket"), "main.rocket", false, false)],
        });
        var explorer = new WorkspaceExplorerViewModel(fileSystem);
        await explorer.OpenAsync(temp.Path);
        var root = explorer.Roots.Single();
        var srcNode = root.Children.Single();
        srcNode.IsExpanded = true;
        await srcNode.LoadChildrenAsync();
        var nestedNode = srcNode.Children.Single();
        nestedNode.IsExpanded = true;
        await nestedNode.LoadChildrenAsync();

        explorer.CollapseAll();

        Assert.IsFalse(root.IsExpanded);
        Assert.IsFalse(srcNode.IsExpanded);
        Assert.IsFalse(nestedNode.IsExpanded);
    }

    [TestMethod]
    public async Task RevealAsync_LoadsOnlyRequiredAncestorsAndPreservesUnrelatedExpansion()
    {
        using var temp = new TempDirectory();
        var src = Path.Combine(temp.Path, "src");
        var nested = Path.Combine(src, "nested");
        var target = Path.Combine(nested, "main.rocket");
        var unrelated = Path.Combine(temp.Path, "unrelated");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(target, "fn main():\n    return 0\n");
        var fileSystem = new FakeWorkspaceFileSystem(new Dictionary<string, IReadOnlyList<WorkspaceEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [temp.Path] =
            [
                new WorkspaceEntry(src, "src", true, true),
                new WorkspaceEntry(unrelated, "unrelated", true, false),
            ],
            [src] = [new WorkspaceEntry(nested, "nested", true, true)],
            [nested] = [new WorkspaceEntry(target, "main.rocket", false, false)],
            [unrelated] = [],
        });
        var explorer = new WorkspaceExplorerViewModel(fileSystem);
        await explorer.OpenAsync(temp.Path);
        var root = explorer.Roots.Single();
        var unrelatedNode = root.Children.Single(node => PathEquals(node.Path, unrelated));
        unrelatedNode.IsExpanded = true;

        var revealed = await explorer.RevealAsync(target);

        Assert.IsNotNull(revealed);
        Assert.IsTrue(revealed.IsSelected);
        Assert.IsTrue(root.IsExpanded);
        var srcNode = root.Children.Single(node => PathEquals(node.Path, src));
        var nestedNode = srcNode.Children.Single(node => PathEquals(node.Path, nested));
        Assert.IsTrue(srcNode.IsExpanded);
        Assert.IsTrue(nestedNode.IsExpanded);
        Assert.IsTrue(unrelatedNode.IsExpanded, "Reveal must not destroy unrelated expansion state.");
    }

    [TestMethod]
    public async Task RevealAsync_OutsideWorkspaceOrMissingPath_IsCleanNoOp()
    {
        using var temp = new TempDirectory();
        var fileSystem = new FakeWorkspaceFileSystem(new Dictionary<string, IReadOnlyList<WorkspaceEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [temp.Path] = [],
        });
        var explorer = new WorkspaceExplorerViewModel(fileSystem);
        await explorer.OpenAsync(temp.Path);

        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.rocket");
        var missingInside = Path.Combine(temp.Path, "missing.rocket");

        Assert.IsNull(await explorer.RevealAsync(outside));
        Assert.IsNull(await explorer.RevealAsync(missingInside));
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private sealed class FakeWorkspaceFileSystem(IReadOnlyDictionary<string, IReadOnlyList<WorkspaceEntry>> entries) : IWorkspaceFileSystem
    {
        public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(entries.TryGetValue(Path.GetFullPath(directoryPath), out var children)
                ? children
                : (IReadOnlyList<WorkspaceEntry>)[]);
        }

        public Task CreateFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public string Rename(string path, string newName) => throw new NotSupportedException();
        public void DeleteToRecycleBin(string path) => throw new NotSupportedException();
        public string ResolveChildPath(string directoryPath, string leafName) => throw new NotSupportedException();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-wp04-explorer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
