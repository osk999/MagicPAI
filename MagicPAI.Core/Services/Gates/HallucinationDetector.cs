using System.Diagnostics;
using System.Text.RegularExpressions;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public partial class HallucinationDetector : IVerificationGate
{
    public string Name => "hallucination";
    public bool IsBlocking => true;

    public Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        return Task.FromResult(true); // Can always verify
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Get the list of all files in the workspace
        var filesResult = await container.ExecAsync(containerId,
            "find . -type f -not -path '*/.*' -not -path '*/node_modules/*' " +
            "-not -path '*/bin/*' -not -path '*/obj/*' " +
            "-not -path './artifacts/*' -not -path './publish/*' " +
            "-not -path '*/wwwroot/_content/*' -not -path '*/wwwroot/_framework/*' 2>/dev/null",
            workDir, ct);

        var existingFiles = filesResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().TrimStart('.', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Check source files for import/using/require statements referencing nonexistent files
        var sourceResult = await container.ExecAsync(containerId,
            "find . -type f \\( -name '*.cs' -o -name '*.js' -o -name '*.ts' " +
            "-o -name '*.py' \\) -not -path '*/node_modules/*' " +
            "-not -path '*/bin/*' -not -path '*/obj/*' " +
            "-not -path './artifacts/*' -not -path './publish/*' " +
            "-not -path '*/wwwroot/_content/*' -not -path '*/wwwroot/_framework/*' " +
            "| xargs grep -n -H -E '(#include|import|require|using)' 2>/dev/null || true",
            workDir, ct);
        sw.Stop();

        var issues = new List<string>();

        // Check for references to files that should exist but do not
        foreach (var line in sourceResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var referencedFile = ExtractFileReference(line);
            if (!string.IsNullOrWhiteSpace(referencedFile) &&
                referencedFile.Contains('.') &&
                !existingFiles.Contains(referencedFile) &&
                !IsStandardLibraryReference(referencedFile))
            {
                issues.Add($"Referenced file may not exist: {referencedFile} (in {line.Trim()[..Math.Min(line.Trim().Length, 150)]})");
            }
        }

        var passed = issues.Count == 0;
        var output = passed
            ? "No hallucinated file references detected."
            : $"Found {issues.Count} potential hallucinated reference(s).";

        return new GateResult(Name, passed, output, issues.ToArray(), sw.Elapsed);
    }

    private static string? ExtractFileReference(string line)
    {
        // Match patterns like: import "./foo", require("./bar"), #include "baz.h"
        var match = FileRefRegex().Match(line);
        return match.Success ? match.Groups[1].Value.TrimStart('.', '/') : null;
    }

    private static bool IsStandardLibraryReference(string reference)
    {
        // Common standard library / framework namespaces that are not local files
        var stdPrefixes = new[]
        {
            "System", "Microsoft", "React", "react", "vue", "angular",
            "express", "path", "fs", "os", "http", "crypto", "util",
            "collections", "typing", "json", "datetime", "std"
        };
        return stdPrefixes.Any(p => reference.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"(?:import|require|#include)\s*[\(""']\s*([^""'\)\s]+)", RegexOptions.None)]
    private static partial Regex FileRefRegex();
}
