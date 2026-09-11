using System.IO;
using RocketIDE.App.ViewModels.Explorer;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class WorkspaceExplorerViewModelTests
{
    [TestMethod]
    public async Task OpenAsync_FailedEnumerationPreservesPreviousWorkspaceAndTree()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.Results[first.Path] = [new WorkspaceEntry(Path.Combine(first.Path, "main.rocket"), "main.rocket", false, false)];
        fileSystem.Failures.Add(second.Path);
        var viewModel = new WorkspaceExplorerViewModel(fileSystem);
        await viewModel.OpenAsync(first.Path);
        var originalRoot = viewModel.Roots.Single();

        await Assert.ThrowsExactlyAsync<IOException>(() => viewModel.OpenAsync(second.Path));

        Assert.AreEqual(first.Path, viewModel.Workspace?.Path);
        Assert.AreSame(originalRoot, viewModel.Roots.Single());
    }

    private sealed class FakeWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public Dictionary<string, IReadOnlyList<WorkspaceEntry>> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken)
        {
            var fullPath = Path.GetFullPath(directoryPath);
            if (Failures.Contains(fullPath))
            {
                throw new IOException("simulated enumeration failure");
            }

            return Task.FromResult(Results.TryGetValue(fullPath, out var result) ? result : (IReadOnlyList<WorkspaceEntry>)[]);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-explorer-{Guid.NewGuid():N}");
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
