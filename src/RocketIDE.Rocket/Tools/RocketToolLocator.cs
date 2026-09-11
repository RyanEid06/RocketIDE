namespace RocketIDE.Rocket.Tools;

public sealed class RocketToolLocator : IRocketToolLocator
{
    private static readonly string[] CheckoutOutputs =
    [
        Path.Combine("out", "build", "windows-debug"),
        Path.Combine("out", "build", "windows-release"),
        Path.Combine("out", "package", "bin"),
    ];


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
            problems.Add("rocketc.exe was not found. Configure a compiler path, use the environment/PATH, bundle an SDK, keep a developer Rocket checkout beside the RocketIDE installation, or explicitly trust the active checkout in Tools > Rocket SDK Settings.");
        }
        else
        {
            compilerVersion = await TryProbeAsync(compiler, "rocketc.exe", problems, cancellationToken).ConfigureAwait(false);
        }

        if (lsp is null)
        {
            problems.Add("rocket-lsp.exe was not found. Configure a language-server path, use the environment/PATH, bundle an SDK, keep a developer Rocket checkout beside the RocketIDE installation, or explicitly trust the active checkout in Tools > Rocket SDK Settings.");
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
        TrustedCheckoutCandidates(activePath, "rocketc.exe"),
        PathCandidates("rocketc.exe"),
        BundledCandidates("rocketc.exe"),
        InstallationAdjacentCheckoutCandidates("rocketc.exe"));

    private string? FindLanguageServer(string? activePath, string? compilerPath) => FindFirstExisting(
        ExplicitCandidate(_options.LanguageServerPath),
        ExplicitCandidate(_environmentVariable("ROCKET_LANGUAGE_SERVER")),
        SiblingCandidate(compilerPath, "rocket-lsp.exe"),
        TrustedCheckoutCandidates(activePath, "rocket-lsp.exe"),
        PathCandidates("rocket-lsp.exe"),
        BundledCandidates("rocket-lsp.exe"),
        InstallationAdjacentCheckoutCandidates("rocket-lsp.exe"));

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

    private IEnumerable<string> TrustedCheckoutCandidates(string? activePath, string fileName)
    {
        if (_options.TrustedCheckoutRoots is null || string.IsNullOrWhiteSpace(activePath))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trustedRoot in _options.TrustedCheckoutRoots)
        {
            if (!TryNormalize(trustedRoot, out var root) || IsFileSystemRoot(root) || !Directory.Exists(root) || !seen.Add(root) ||
                !IsWithinRoot(activePath, root))
            {
                continue;
            }

            foreach (var output in CheckoutOutputs)
            {
                yield return Path.Combine(root, output, fileName);
            }

            foreach (var packaged in VersionedPackageCandidates(root, fileName))
            {
                yield return packaged;
            }
        }
    }


    private static bool IsFileSystemRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinRoot(string activePath, string root)
    {
        if (!TryNormalize(activePath, out var active))
        {
            return false;
        }

        if (string.Equals(active, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return active.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> VersionedPackageCandidates(string root, string fileName)
    {
        var packageRoot = Path.Combine(root, "out", "package");
        string[] packageDirectories;
        try
        {
            packageDirectories = Directory.Exists(packageRoot)
                ? Directory.GetDirectories(packageRoot)
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var packageDirectory in packageDirectories.OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(packageDirectory, "bin", fileName);
        }
    }

    private IEnumerable<string> InstallationAdjacentCheckoutCandidates(string fileName)
    {
        // Development builds commonly live beside the Rocket checkout, for example
        //   ...\Projects\RocketIDE-Build
        //   ...\Projects\Rocket
        // This fallback is derived only from RocketIDE's immediate installation parent;
        // opening a workspace never changes the candidate root. Keeping the search to one
        // parent avoids turning unrelated ancestor directories into implicit trust roots.
        var installationDirectory = _options.InstallationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installationName = Path.GetFileName(installationDirectory);
        if (!string.Equals(installationName, "RocketIDE-Build", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(installationName, "RocketIDE", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var parent = Directory.GetParent(installationDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) || IsFileSystemRoot(parent))
        {
            yield break;
        }

        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var checkoutName in new[] { "Rocket", "rocket" })
        {
            var root = Path.Combine(parent, checkoutName);
            if (!Directory.Exists(root) || !seenRoots.Add(Path.GetFullPath(root)))
            {
                continue;
            }

            foreach (var output in CheckoutOutputs)
            {
                yield return Path.Combine(root, output, fileName);
            }

            foreach (var packaged in VersionedPackageCandidates(root, fileName))
            {
                yield return packaged;
            }
        }
    }

    private IEnumerable<string> PathCandidates(string fileName)
    {
        var path = _environmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
            if (cleaned.Length > 0 && Path.IsPathRooted(cleaned))
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

    private static bool TryNormalize(string path, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
