using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Voxwright.Core.Models;
using Voxwright.Core.Services.IDE;

namespace Voxwright.App.Services;

/// <summary>
/// Detects whether the foreground window belongs to a known IDE (VS Code, Cursor, Windsurf)
/// and resolves the workspace path on disk.
///
/// Detection approach:
/// 1. Gets the process name from the window handle via Win32 <c>GetWindowThreadProcessId</c>.
/// 2. Matches against a known list of IDE process names.
/// 3. Parses the window title to extract the folder name and current file
///    (VS Code format: "file.ts - myproject - Visual Studio Code").
/// 4. Resolves the folder name to an absolute disk path by reading VS Code's
///    <c>storage.json</c> file, which contains recently opened workspace URIs.
///    Supports multiple storage.json formats across VS Code versions:
///    - Legacy: <c>openedPathsList.entries[].folderUri</c>
///    - Modern: <c>backupWorkspaces.folders[]</c>
///    - Newest: <c>windowsState.lastActiveWindow.folder</c> / <c>openedWindows[].folder</c>
///
/// The resolved workspace path is then used by <c>IDEContextService</c> to scan for
/// source code identifiers that improve transcription accuracy (e.g., variable names,
/// class names injected into the correction prompt).
/// </summary>
public class IDEDetectionService : IIDEDetectionService
{
    private readonly ILogger<IDEDetectionService> _logger;

    private static readonly Dictionary<string, string> KnownIDEs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Code"] = "Visual Studio Code",
            ["Cursor"] = "Cursor",
            ["Windsurf"] = "Windsurf",
        };

    /// <summary>
    /// Initializes a new instance of the <see cref="IDEDetectionService"/> class.
    /// </summary>
    public IDEDetectionService(ILogger<IDEDetectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects the IDE from the specified window handle. Returns an <see cref="IDEInfo"/>
    /// containing the process name, resolved workspace path, and current file name,
    /// or <c>null</c> if the window does not belong to a known IDE.
    /// </summary>
    /// <param name="windowHandle">The HWND of the window to inspect.</param>
    /// <returns>IDE information, or <c>null</c> if not a known IDE.</returns>
    public IDEInfo? DetectIDE(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero) return null;

            NativeMethods.GetWindowThreadProcessId(windowHandle, out var pid);
            if (pid == 0) return null;

            string processName;
            try
            {
                processName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                return null;
            }

            if (!KnownIDEs.TryGetValue(processName, out var ideSuffix))
                return null;

            var title = GetWindowTitle(windowHandle);
            if (string.IsNullOrEmpty(title))
                return null;

            var (folderName, currentFile) = ParseWindowTitle(title, ideSuffix);

            _logger.LogDebug("IDE detected: {IDE}, folder: {Folder}, file: {File}",
                processName, folderName, currentFile);

            var workspacePath = ResolveWorkspacePath(processName, folderName);

            if (workspacePath is not null)
                _logger.LogInformation("IDE workspace resolved: {Path}", workspacePath);
            else
                _logger.LogWarning("Could not resolve workspace path for folder: {Folder}", folderName);

            return new IDEInfo(processName, workspacePath, currentFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IDE detection failed");
            return null;
        }
    }

    /// <summary>
    /// Retrieves the window title text via Win32 <c>GetWindowText</c>.
    /// Uses the Unicode variant (<c>GetWindowTextW</c>) for correct character handling.
    /// </summary>
    internal static string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return "";

        var buffer = new char[length + 1];
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    /// <summary>
    /// Parses a VS Code-style window title into folder name and current file components.
    /// Handles multiple title formats: "file.ts - myproject - Visual Studio Code",
    /// "myproject - Visual Studio Code", "file.ts - myproject (Workspace) - Visual Studio Code".
    /// Strips the " (Workspace)" suffix if present.
    /// </summary>
    /// <param name="title">The full window title string.</param>
    /// <param name="ideSuffix">The IDE name suffix to strip (e.g., "Visual Studio Code").</param>
    /// <returns>A tuple of (folder name, current file name), either or both may be null.</returns>
    internal static (string? FolderName, string? CurrentFile) ParseWindowTitle(string title, string ideSuffix)
    {
        // VS Code title formats:
        // "file.ts - myproject - Visual Studio Code"
        // "myproject - Visual Studio Code"
        // "file.ts - myproject (Workspace) - Visual Studio Code"
        // "Welcome - Visual Studio Code"

        // Remove the IDE suffix
        var suffixIndex = title.LastIndexOf(" - " + ideSuffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIndex < 0) suffixIndex = title.LastIndexOf(" \u2014 " + ideSuffix, StringComparison.OrdinalIgnoreCase); // em dash
        if (suffixIndex < 0) return (null, null);

        var remaining = title[..suffixIndex].Trim();
        if (string.IsNullOrEmpty(remaining)) return (null, null);

        // Split by " - " or " — "
        var parts = remaining.Split([" - ", " \u2014 "], StringSplitOptions.TrimEntries);

        if (parts.Length == 1)
        {
            // Just folder name: "myproject"
            var folder = StripWorkspaceSuffix(parts[0]);
            return (folder, null);
        }

        if (parts.Length >= 2)
        {
            // "file.ts - myproject" or "file.ts - subfolder - myproject"
            var folder = StripWorkspaceSuffix(parts[^1]);
            var file = parts[0];
            return (folder, file);
        }

        return (null, null);
    }

    /// <summary>
    /// Resolves a workspace folder name to an absolute disk path by searching VS Code's
    /// <c>storage.json</c> file. Uses the current user's <c>%APPDATA%</c> path.
    /// </summary>
    internal static string? ResolveWorkspacePath(string processName, string? folderName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return ResolveWorkspacePath(processName, folderName, appData);
    }

    /// <summary>
    /// Resolves a workspace folder name to an absolute disk path by searching VS Code's
    /// <c>storage.json</c>. Checks two possible storage.json locations (globalStorage and root)
    /// and tries three JSON formats (legacy, backup, windowsState) for compatibility
    /// across VS Code versions. Opens the file with <c>FileShare.ReadWrite</c> because
    /// VS Code may be writing to it concurrently.
    /// </summary>
    /// <param name="processName">The IDE process name (determines the AppData subfolder).</param>
    /// <param name="folderName">The folder name from the window title to search for.</param>
    /// <param name="appDataPath">The <c>%APPDATA%</c> directory path (injectable for testing).</param>
    /// <returns>The absolute disk path to the workspace, or <c>null</c> if not found.</returns>
    internal static string? ResolveWorkspacePath(string processName, string? folderName, string appDataPath)
    {
        if (string.IsNullOrEmpty(folderName)) return null;

        var storagePaths = new[]
        {
            Path.Combine(appDataPath, processName, "User", "globalStorage", "storage.json"),
            Path.Combine(appDataPath, processName, "storage.json"),
        };

        foreach (var storagePath in storagePaths)
        {
            if (!File.Exists(storagePath)) continue;

            // Use FileShare.ReadWrite — VS Code keeps this file open for writing
            using var stream = new FileStream(storagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);

            // Try legacy format: openedPathsList.entries[].folderUri
            var result = FindInOpenedPathsList(doc, folderName);
            if (result is not null) return result;

            // Try new format: backupWorkspaces.folders[].folderUri
            result = FindInBackupWorkspaces(doc, folderName);
            if (result is not null) return result;

            // Try windowsState.lastActiveWindow.folder + openedWindows[].folder
            result = FindInWindowsState(doc, folderName);
            if (result is not null) return result;
        }

        return null;
    }

    private static string? FindInOpenedPathsList(JsonDocument doc, string folderName)
    {
        if (!doc.RootElement.TryGetProperty("openedPathsList", out var pathsList)) return null;
        if (!pathsList.TryGetProperty("entries", out var entries)) return null;

        foreach (var entry in entries.EnumerateArray())
        {
            var folderUri = GetFolderUri(entry);
            var match = MatchFolderUri(folderUri, folderName);
            if (match is not null) return match;
        }

        return null;
    }

    private static string? FindInBackupWorkspaces(JsonDocument doc, string folderName)
    {
        if (!doc.RootElement.TryGetProperty("backupWorkspaces", out var backupWorkspaces)) return null;
        if (!backupWorkspaces.TryGetProperty("folders", out var folders)) return null;

        foreach (var entry in folders.EnumerateArray())
        {
            // folders[] can be strings (URI directly) or objects with folderUri
            string? folderUri = entry.ValueKind == JsonValueKind.String
                ? entry.GetString()
                : GetFolderUri(entry);
            var match = MatchFolderUri(folderUri, folderName);
            if (match is not null) return match;
        }

        return null;
    }

    private static string? FindInWindowsState(JsonDocument doc, string folderName)
    {
        if (!doc.RootElement.TryGetProperty("windowsState", out var windowsState)) return null;

        // Check lastActiveWindow.folder
        if (windowsState.TryGetProperty("lastActiveWindow", out var lastActive)
            && lastActive.TryGetProperty("folder", out var lastFolder))
        {
            var match = MatchFolderUri(lastFolder.GetString(), folderName);
            if (match is not null) return match;
        }

        // Check openedWindows[].folder
        if (windowsState.TryGetProperty("openedWindows", out var openedWindows))
        {
            foreach (var window in openedWindows.EnumerateArray())
            {
                if (window.TryGetProperty("folder", out var folder))
                {
                    var match = MatchFolderUri(folder.GetString(), folderName);
                    if (match is not null) return match;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a <c>file://</c> URI's last path segment matches the expected folder name.
    /// Handles a Windows-specific quirk: when the drive letter colon is percent-encoded as
    /// <c>%3A</c> in the URI, <see cref="Uri.LocalPath"/> returns <c>/C:/...</c> with a
    /// leading slash that breaks <see cref="System.IO.Directory.Exists"/>. This method
    /// strips the leading slash in that case.
    /// </summary>
    /// <param name="folderUri">A <c>file://</c> URI from VS Code's storage.json.</param>
    /// <param name="folderName">The expected folder name to match against the URI's last segment.</param>
    /// <returns>The local filesystem path if it matches and exists, otherwise <c>null</c>.</returns>
    internal static string? MatchFolderUri(string? folderUri, string folderName)
    {
        if (folderUri is null) return null;
        if (!Uri.TryCreate(folderUri, UriKind.Absolute, out var uri)) return null;
        if (!uri.IsFile) return null;

        var localPath = uri.LocalPath;

        // Uri.LocalPath returns "/C:/..." when the drive letter colon is percent-encoded (%3A).
        // The leading slash breaks Directory.Exists on Windows, so strip it.
        if (localPath.Length >= 3 && localPath[0] == '/' && char.IsLetter(localPath[1]) && localPath[2] == ':')
            localPath = localPath[1..];

        var dirName = Path.GetFileName(localPath.TrimEnd('/', '\\'));

        if (string.Equals(dirName, folderName, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(localPath))
        {
            return localPath;
        }

        return null;
    }

    private static string? GetFolderUri(JsonElement entry)
    {
        // Format varies between VS Code versions:
        // { "folderUri": "file:///path" }
        // or nested: { "workspace": { "folderUri": "file:///path" } }
        if (entry.TryGetProperty("folderUri", out var uri))
            return uri.GetString();

        if (entry.TryGetProperty("workspace", out var workspace)
            && workspace.TryGetProperty("folderUri", out var wsUri))
            return wsUri.GetString();

        return null;
    }

    private static string StripWorkspaceSuffix(string name)
    {
        // Remove " (Workspace)" suffix if present
        const string suffix = " (Workspace)";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : name;
    }
}
