using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.App.Services;

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

    public IDEDetectionService(ILogger<IDEDetectionService> logger)
    {
        _logger = logger;
    }

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
                _logger.LogDebug("Could not resolve workspace path for folder: {Folder}", folderName);

            return new IDEInfo(processName, workspacePath, currentFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IDE detection failed");
            return null;
        }
    }

    internal static string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return "";

        var buffer = new char[length + 1];
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

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

    internal static string? ResolveWorkspacePath(string processName, string? folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var storagePath = Path.Combine(appData, processName, "storage.json");

            if (!File.Exists(storagePath)) return null;

            var json = File.ReadAllText(storagePath);
            using var doc = JsonDocument.Parse(json);

            // Navigate to openedPathsList.entries
            if (!doc.RootElement.TryGetProperty("openedPathsList", out var pathsList)) return null;
            if (!pathsList.TryGetProperty("entries", out var entries)) return null;

            foreach (var entry in entries.EnumerateArray())
            {
                var folderUri = GetFolderUri(entry);
                if (folderUri is null) continue;

                // Parse file:///path URI to local path
                if (!Uri.TryCreate(folderUri, UriKind.Absolute, out var uri)) continue;
                if (!uri.IsFile) continue;

                var localPath = uri.LocalPath;
                var dirName = Path.GetFileName(localPath.TrimEnd('/', '\\'));

                if (string.Equals(dirName, folderName, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(localPath))
                {
                    return localPath;
                }
            }
        }
        catch
        {
            // storage.json might be malformed or inaccessible
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
