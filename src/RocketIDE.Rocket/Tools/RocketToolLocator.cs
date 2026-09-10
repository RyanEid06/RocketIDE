namespace RocketIDE.Rocket.Tools;

public sealed class RocketToolLocator : IRocketToolLocator
{
    private static readonly string[] CheckoutOutputs =
    [
        Path.Combine("out", "build", "windows-debug"),
        Path.Combine("out", "build", "windows-release"),
        Path.Combine("out", "package", "bin"),
    ];

    private static readonly string[] AdjacentCheckoutNames = ["Rocket", "rocket"];

    private readonly RocketToolDiscoveryOptions _options;
    private readonly IRocketToolVersionProbe _versionProbe;
    private readonly Func<string, string?> _environmentVariable;

    public RocketToolLocator(
        RocketToolDiscoveryOptions options,
        IRocketToolVersionProbe? versionProbe = null,
        Func<string, string?>? environmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.InstallationDirectory);
        _options = options with { InstallationDirectory = Path.GetFullPath(options.InstallationDirectory) };
        _versionProbe = versionProbe ?? new ProcessRocketToolVersionProbe();
        _environmentVariable = environmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken)
    {
        var result = await DiscoverAsync(activePath, cancellationToken).ConfigureAwait(false);
        return result.Toolchain;
    }

    public async Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var compiler = FindCompiler(activePath);
        var lsp = FindLanguageServer(activePath, compiler);
        string? compilerVersion = null;
        string? languageServerVersion = null;

        if (compiler is null)
        {
            problems.Add("rocketc.exe was not found. Configure a compiler path or make Rocket available through the environment/PATH.");
        }
        else
        {
            compilerVersion = await TryProbeAsync(compiler, "rocketc.exe", problems, cancellationToken).ConfigureAwait(false);
        }

        if (lsp is null)
        {
            problems.Add("rocket-lsp.exe was not found. Configure a language-server path or build/install the Rocket language server.");
        }
        else
        {
            languageServerVersion = await TryProbeAsync(lsp, "rocket-lsp.exe", problems, cancellationToken).ConfigureAwait(false);
        }

        return new RocketToolDiscoveryResult(compiler, lsp, compilerVersion, languageServerVersion, problems);
    }

    private string? FindCompiler(string? activePath) => FindFirstExisting(
        ExplicitCandidate(_options.CompilerPath),
        ExplicitCandidate(_environmentVariable("ROCKET_COMPILER")),
        ActiveCheckoutCandidates(activePath, "rocketc.exe"),
        AdjacentCheckoutCandidates(activePath, "rocketc.exe"),
        PathCandidates("rocketc.exe"),
        BundledCandidates("rocketc.exe"));

    private string? FindLanguageServer(string? activePath, string? compilerPath) => FindFirstExisting(
        ExplicitCandidate(_options.LanguageServerPath),
        ExplicitCandidate(_environmentVariable("ROCKET_LANGUAGE_SERVER")),
        SiblingCandidate(compilerPath, "rocket-lsp.exe"),
        ActiveCheckoutCandidates(activePath, "rocket-lsp.exe"),
        AdjacentCheckoutCandidates(activePath, "rocket-lsp.exe"),
        PathCandidates("rocket-lsp.exe"),
        BundledCandidates("rocket-lsp.exe"));

    private async Task<string?> TryProbeAsync(
        string path,
        string displayName,
        ICollection<string> problems,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _versionProbe.GetVersionAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            problems.Add($"{displayName} was found at '{path}' but version validation failed: {exception.Message}");
            return null;
        }
    }

    private static string? FindFirstExisting(params IEnumerable<string>[] candidateGroups)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidateGroups)
        {
            foreach (var candidate in group)
            {
                if (!TryNormalize(candidate, out var fullPath) || !seen.Add(fullPath))
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExplicitCandidate(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            yield return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        }
    }

    private static IEnumerable<string> SiblingCandidate(string? executablePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            yield break;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            yield return Path.Combine(directory, fileName);
        }
    }

    private static IEnumerable<string> ActiveCheckoutCandidates(string? activePath, string fileName)
    {
        foreach (var ancestor in EnumerateAncestors(activePath))
        {
            foreach (var output in CheckoutOutputs)
            {
                yield return Path.Combine(ancestor, output, fileName);
            }
        }
    }

    private IEnumerable<string> AdjacentCheckoutCandidates(string? activePath, string fileName)
    {
        foreach (var siblingRoot in EnumerateAdjacentCheckouts(activePath))
        {
            foreach (var output in CheckoutOutputs)
            {
                yield return Path.Combine(siblingRoot, output, fileName);
            }
        }
    }

    private IEnumerable<string> PathCandidates(string fileName)
    {
        var path = _environmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = directory.Trim().Trim('"');
            if (cleaned.Length > 0)
            {
                yield return Path.Combine(cleaned, fileName);
            }
        }
    }

    private IEnumerable<string> BundledCandidates(string fileName)
    {
        yield return Path.Combine(_options.InstallationDirectory, "sdk", "bin", fileName);
        yield return Path.Combine(_options.InstallationDirectory, "sdk", fileName);
        yield return Path.Combine(_options.InstallationDirectory, fileName);
    }

    private IEnumerable<string> EnumerateAdjacentCheckouts(string? activePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in EnumerateAncestors(activePath).Concat(EnumerateAncestors(_options.InstallationDirectory)))
        {
            DirectoryInfo? parent;
            try
            {
                parent = Directory.GetParent(anchor);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                parent = null;
            }

            if (parent is null)
            {
                continue;
            }

            foreach (var name in AdjacentCheckoutNames)
            {
                var candidate = Path.Combine(parent.FullName, name);
                if (Directory.Exists(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateAncestors(string? activePath)
    {
        if (string.IsNullOrWhiteSpace(activePath))
        {
            yield break;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(activePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            yield break;
        }

        var directoryPath = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            yield break;
        }

        for (var directory = new DirectoryInfo(directoryPath); directory is not null; directory = directory.Parent)
        {
            yield return directory.FullName;
        }
    }

    private static bool TryNormalize(string path, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
