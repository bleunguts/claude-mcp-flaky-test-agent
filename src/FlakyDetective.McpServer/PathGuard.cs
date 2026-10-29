namespace FlakyDetective.McpServer;

public static class PathGuard
{
    private static readonly string[] DeniedFolders = { ".git", ".claude", "bin", "obj", "TestResults" };
    private static readonly string[] DeniedFilePrefixes = { ".env", "GROUND_TRUTH" };
    private static readonly string[] AllowedExtensions = { ".cs", ".csproj", ".slnx", ".md", ".json", ".ps1", ".txt" };

    public static bool TryResolve(string root, string requestedPath, out string fullPath, out string? error)
    {
        fullPath = "";
        error = null;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            error = "path is required";
            return false;
        }

        // Path.Combine throws away the root when the second part is absolute, so reject that first
        if (Path.IsPathRooted(requestedPath))
        {
            error = "path must be relative to the solution root";
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        var rootWithSeparator = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;

        // GetFullPath resolves ".." and mixed separators before we compare
        var candidate = Path.GetFullPath(Path.Combine(rootFull, requestedPath));

        // the trailing separator stops "FlakyLab2" matching a root called "FlakyLab"
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = "path is outside the solution root";
            return false;
        }

        var relative = Path.GetRelativePath(rootFull, candidate);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Any(s => DeniedFolders.Contains(s, StringComparer.OrdinalIgnoreCase)))
        {
            error = "access to this location is not allowed";
            return false;
        }

        var fileName = segments[^1];
        if (DeniedFilePrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            error = "access to this file is not allowed";
            return false;
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            error = $"file type '{Path.GetExtension(fileName)}' is not readable";
            return false;
        }

        fullPath = candidate;
        return true;
    }
}