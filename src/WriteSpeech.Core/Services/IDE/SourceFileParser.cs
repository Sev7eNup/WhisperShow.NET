using System.Text.RegularExpressions;

namespace WriteSpeech.Core.Services.IDE;

public static partial class SourceFileParser
{
    private static readonly HashSet<string> SourceExtensions =
    [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".java",
        ".rb", ".rs", ".cpp", ".c", ".h", ".hpp", ".vue", ".svelte",
        ".swift", ".kt", ".scala", ".php", ".lua", ".zig"
    ];

    private static readonly HashSet<string> ExcludedDirs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", "dist", "build",
            "__pycache__", ".vs", "packages", "vendor", ".next",
            ".nuget", "target", ".idea", ".vscode", "coverage",
            "out", ".cache", ".output"
        };

    private static readonly HashSet<string> CommonKeywords =
        new(StringComparer.Ordinal)
        {
            // C# / Java / general
            "if", "else", "for", "while", "do", "switch", "case", "break",
            "continue", "return", "var", "let", "const", "function", "class",
            "public", "private", "protected", "internal", "static", "void",
            "string", "int", "bool", "float", "double", "long", "byte",
            "char", "short", "decimal", "object", "true", "false", "null",
            "new", "this", "base", "using", "namespace", "import", "from",
            "export", "default", "async", "await", "try", "catch", "throw",
            "finally", "abstract", "interface", "enum", "struct", "record",
            "sealed", "virtual", "override", "readonly", "partial",
            "yield", "where", "select", "get", "set", "value", "typeof",
            "sizeof", "is", "as", "in", "out", "ref", "params", "delegate",
            // TypeScript / JavaScript
            "type", "extends", "implements", "constructor", "super",
            "require", "module", "exports", "undefined", "any", "number",
            "boolean", "symbol", "never", "unknown", "void", "declare",
            // Python
            "def", "self", "cls", "elif", "except", "raise", "pass",
            "with", "lambda", "nonlocal", "global", "assert", "del",
            "None", "True", "False", "and", "or", "not",
            // Go
            "func", "package", "defer", "chan", "map", "range", "make",
            "append", "len", "cap", "nil",
            // Rust
            "fn", "impl", "trait", "pub", "mod", "use", "crate",
            "mut", "match", "Some", "None", "Ok", "Err",
            // Common short words that are not useful
            "the", "and", "for", "not", "are", "but", "had", "has",
            "was", "all", "can", "her", "one", "our", "out", "end",
            "TODO", "FIXME", "HACK", "NOTE", "XXX"
        };

    [GeneratedRegex(@"\b[A-Za-z_]\w{2,}\b", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    public static IReadOnlyList<string> ExtractIdentifiers(string workspacePath, int maxResults = 200)
    {
        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in EnumerateSourceFiles(workspacePath))
        {
            try
            {
                // Skip very large files (>500KB)
                var info = new FileInfo(file);
                if (info.Length > 512_000) continue;

                var content = File.ReadAllText(file);
                foreach (var id in ExtractIdentifiersFromContent(content))
                {
                    frequency[id] = frequency.GetValueOrDefault(id) + 1;
                }
            }
            catch
            {
                // Skip files we can't read
            }
        }

        return frequency
            .OrderByDescending(kv => kv.Value)
            .Take(maxResults)
            .Select(kv => kv.Key)
            .ToList();
    }

    public static IReadOnlyList<string> ExtractIdentifiersFromContent(string content)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var match in IdentifierRegex().EnumerateMatches(content.AsSpan()))
        {
            var value = content.Substring(match.Index, match.Length);
            if (!CommonKeywords.Contains(value) && !IsAllLowerSingleWord(value))
                identifiers.Add(value);
        }

        return [.. identifiers];
    }

    public static IReadOnlyList<string> CollectFileNames(string workspacePath, int maxResults = 100)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateSourceFiles(workspacePath))
        {
            fileNames.Add(Path.GetFileName(file));
            if (fileNames.Count >= maxResults) break;
        }

        return [.. fileNames.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> EnumerateSourceFiles(string workspacePath)
    {
        var stack = new Stack<string>();
        stack.Push(workspacePath);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (SourceExtensions.Contains(ext))
                    yield return file;
            }

            try
            {
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(subDir);
                    if (!ExcludedDirs.Contains(name))
                        stack.Push(subDir);
                }
            }
            catch
            {
                // Skip directories we can't access
            }
        }
    }

    /// <summary>
    /// Filters out short all-lowercase identifiers that are likely common English words
    /// or generic variable names (e.g., "name", "data", "text", "result").
    /// Keeps camelCase, PascalCase, UPPER_CASE, and names with underscores.
    /// </summary>
    private static bool IsAllLowerSingleWord(string value)
    {
        if (value.Length > 8) return false; // Long lowercase words might be meaningful identifiers

        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '_' || char.IsUpper(c) || char.IsDigit(c))
                return false;
        }

        return true;
    }
}
