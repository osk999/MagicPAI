// MagicPAI.Activities/Contracts/SmartImproveActivityContracts.cs
// Value-object records that flow through SmartImprove activities AND need
// to be visible to the workflows that call those activities. Lives in
// Activities/Contracts because the project-reference direction is
// MagicPAI.Workflows → MagicPAI.Activities (workflows can see activity
// records, not the reverse).
//
// Workflow-level Input/Output records (SmartImproveInput, SmartIterativeLoopInput,
// SmartIterativeLoopOutput, etc.) live in MagicPAI.Workflows.Contracts.
namespace MagicPAI.Activities.Contracts;

// ─── Rubric and verification records ──────────────────────────────────────

/// <summary>
/// A project-type-specific completion rubric. Generated once during preprocess.
/// </summary>
public record DoneRubric(
    /// <summary><c>"game" | "web" | "api" | "cli" | "library" | "desktop" | "unknown"</c></summary>
    string ProjectType,
    /// <summary>Free-form rationale string. Useful for debugging / Studio display.</summary>
    string Rationale,
    IReadOnlyList<RubricItem> Items);

public record RubricItem(
    string Id,
    string Description,
    /// <summary><c>"P0" | "P1" | "P2" | "P3"</c></summary>
    string Priority,
    /// <summary>Bash command, or "playwright:&lt;spec&gt;", or "http:&lt;url&gt;:&lt;status&gt;"</summary>
    string VerificationCommand,
    /// <summary>How to interpret the result. <c>"exit-zero" | "regex:&lt;pattern&gt;" | "json-path:&lt;jq&gt;" | "none"</c></summary>
    string PassCriteria,
    /// <summary>True for tests that existed before SmartImprove ran. Only trusted items count toward termination.</summary>
    bool IsTrusted = true);

/// <summary>
/// Result of running the verification harness once.
/// </summary>
public record VerifyHarnessOutput(
    int RealP0Count,
    int RealP1Count,
    int RealP2Count,
    int RealP3Count,
    int StructuralCount,
    int EnvironmentalCount,
    IReadOnlyList<RubricFailure> Failures,
    /// <summary>Hash of the failure-id set, used for "same failures across iterations" detection.</summary>
    string FailureSetHash);

public record RubricFailure(
    string RubricItemId,
    string Priority,                              // P0/P1/P2/P3
    /// <summary><c>"real" | "structural" | "environmental"</c></summary>
    string Classification,
    string Evidence);

// ─── Filesystem delta records (drive silence countdown + tests/ tripwire) ─

public record FilesystemSnapshot(
    IReadOnlyDictionary<string, string> FileHashes,
    long CapturedAtUnixSeconds);

public record FilesystemDelta(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Deleted)
{
    /// <summary>True when no files were created, modified, or deleted.</summary>
    public bool IsEmpty => Created.Count == 0 && Modified.Count == 0 && Deleted.Count == 0;

    /// <summary>True when ANY change touched a file matching <c>tests/</c>, <c>*.spec.*</c>, or <c>*.test.*</c>.</summary>
    public bool TouchedTestFiles =>
        Created.Concat(Modified).Concat(Deleted).Any(IsTestPath);

    private static bool IsTestPath(string path)
    {
        // Normalize separators so Windows paths handed in from local fs ops
        // collide with Unix paths from container snapshots.
        var p = path.Replace('\\', '/');

        // Folder-conventions (covers ASP.NET, Node, Python, Rust, generic)
        if (p.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("test/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("__tests__/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/test/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/__tests__/", StringComparison.OrdinalIgnoreCase))
            return true;

        // File-naming conventions:
        //   xunit:        FooTests.cs
        //   Go:           foo_test.go
        //   Python:       test_foo.py / foo_test.py
        //   JS/TS Jest:   foo.test.ts / foo.spec.ts
        //   Rust:         tests.rs (handled by folder above mostly)
        if (p.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)
            || p.Contains("_test.", StringComparison.OrdinalIgnoreCase)
            || p.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || p.Contains(".spec.", StringComparison.OrdinalIgnoreCase))
            return true;

        // Python "test_<name>.py" — the underscore distinguishes it from
        // accidentally matching "testdata/...".
        var fileName = p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p;
        if (fileName.StartsWith("test_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
