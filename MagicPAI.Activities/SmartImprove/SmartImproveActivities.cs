// MagicPAI.Activities/SmartImprove/SmartImproveActivities.cs
// Filesystem and AST utility activities used by SmartImproveWorkflow and
// SmartIterativeLoopWorkflow to drive (a) the silence-countdown anti-
// reward-hacking guard and (b) the AST-hash signal in the multi-signal
// no-progress detector.
//
// These intentionally don't depend on the AI runner stack — they only need
// IContainerManager so they can run shell pipelines (find | xargs sha256sum,
// etc.) inside the workspace container.
//
// See newplan.md §2.2 (activity inventory), §4 (anti-reward-hacking
// guards), and §3 (verification harness).
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using Temporalio.Exceptions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.SmartImprove;

/// <summary>
/// Filesystem-snapshot and lexical-hash activities. Designed to be cheap
/// (typically &lt; 2s on a small repo) and deterministic enough that the
/// SmartIterativeLoop workflow can compare consecutive iteration outputs
/// to drive its silence-countdown and no-progress signals.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why "lexical hash" instead of true Roslyn AST hash for v1.</b> Doing a
/// real AST parse requires either copying every <c>.cs</c> out of the
/// container into the activity host (latency + complexity), or installing a
/// dotnet roslyn parser inside the container (image bloat). Per
/// newplan.md §10 risk register, v1 ships a normalize-and-hash pipeline that
/// strips whitespace and single/multi-line comments, then sha256-aggregates
/// the per-file hashes. This defeats the threats we care about — whitespace
/// or comment churn used to fool the git-HEAD signal — without the
/// engineering cost of a true AST. v2 will replace with Tree-sitter for
/// multi-language coverage.
/// </para>
/// <para>
/// <b>Why bash -c.</b> The pipelines need <c>find</c>, <c>xargs</c>,
/// <c>sha256sum</c>, and <c>sort</c> — there's no portable single-binary
/// equivalent inside the workspace image. The shell payloads are built from
/// constants plus the workspace path; no untrusted input is concatenated in.
/// We still pass through <see cref="ContainerExecRequest"/> so the docker
/// exec call uses argv-passing (per the "always use safe parsers" memo).
/// </para>
/// </remarks>
public class SmartImproveActivities
{
    // Default exclusions: build artifacts, vendored deps, VCS metadata,
    // SmartImprove's own scratch folder. These would otherwise dominate the
    // hash and produce false positive deltas.
    private static readonly IReadOnlyList<string> DefaultExcludes = new[]
    {
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".vs",
        ".idea",
        "dist",
        "build",
        "target",
        ".smartimprove",
        ".cache",
    };

    private readonly IContainerManager _docker;
    private readonly ILogger<SmartImproveActivities> _log;

    public SmartImproveActivities(
        IContainerManager docker,
        ILogger<SmartImproveActivities>? log = null)
    {
        _docker = docker;
        _log = log ?? NullLogger<SmartImproveActivities>.Instance;
    }

    // ─── SnapshotFilesystemAsync ───────────────────────────────────────────

    /// <summary>
    /// Walk the workspace and return a content-only SHA-256 per file. Used by
    /// SmartIterativeLoop's silence countdown — comparing two consecutive
    /// snapshots tells us whether the model wrote anything during a verification
    /// pass. Comparison must be content-based (mtime/ctime ignored) because the
    /// model could touch files without changing them.
    /// </summary>
    [Activity]
    public async Task<SnapshotFilesystemOutput> SnapshotFilesystemAsync(
        SnapshotFilesystemInput input)
    {
        ValidateContainerId(input.ContainerId);
        var workspace = NormalizeWorkspace(input.WorkspacePath);
        var ct = ActivityExecutionContext.Current.CancellationToken;

        var excludes = MergeExcludes(input.ExcludeGlobs);
        var maxFiles = input.MaxFiles <= 0 ? 50_000 : input.MaxFiles;

        // Build a deterministic find pipeline:
        //   find . -type f \( <exclude clauses> \) -prune -o -print0
        // Then xargs into sha256sum, then sort by path so the output ordering
        // is stable across iterations regardless of filesystem traversal order.
        var pipeline = BuildSnapshotPipeline(workspace, excludes, maxFiles);

        var request = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", pipeline },
            WorkingDirectory: workspace);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ApplicationFailureException(
                $"Snapshot failed: {ex.Message}",
                errorType: "SnapshotError",
                nonRetryable: false);
        }

        // sha256sum exit code is non-zero if some files disappeared mid-walk
        // (race) or if any individual hash failed. The Output still contains
        // the successful entries — surface a warning but use what we have.
        if (result.ExitCode != 0)
        {
            _log.LogWarning(
                "Snapshot pipeline exited {ExitCode}; partial results parsed. Stderr: {Err}",
                result.ExitCode, Truncate(result.Error, 256));
        }

        var (hashes, truncated) = ParseShaSumOutput(result.Output ?? "", maxFiles);
        var captured = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new SnapshotFilesystemOutput(
            FileHashes: hashes,
            CapturedAtUnixSeconds: captured,
            FileCount: hashes.Count,
            TruncatedByMaxFiles: truncated);
    }

    // ─── ComputeAstHashAsync ───────────────────────────────────────────────

    /// <summary>
    /// Compute a workspace-wide lexical hash over .cs files, normalized to
    /// strip whitespace and comments. Two iterations producing the same hash
    /// — even if git status reports modifications — means the model only
    /// shuffled comments or whitespace. Combined with the git signal, this
    /// catches the "churn whitespace to defeat no-progress detection"
    /// failure mode.
    /// </summary>
    [Activity]
    public async Task<ComputeAstHashOutput> ComputeAstHashAsync(
        ComputeAstHashInput input)
    {
        ValidateContainerId(input.ContainerId);
        var workspace = NormalizeWorkspace(input.WorkspacePath);
        var ct = ActivityExecutionContext.Current.CancellationToken;

        // Build the per-file normalize pipeline. Order matters:
        //   1. strip /* ... */ block comments (greedy across lines via tr+sed)
        //   2. strip // line comments
        //   3. strip all whitespace
        // Then sha256sum; sort by path; aggregate-hash the result.
        var pipeline = BuildAstHashPipeline(workspace, input.Files);

        var request = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", pipeline },
            WorkingDirectory: workspace);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ApplicationFailureException(
                $"AST-hash pipeline failed: {ex.Message}",
                errorType: "AstHashError",
                nonRetryable: false);
        }

        // The pipeline emits two lines: the per-file count, then the aggregate hash.
        // Handle the no-files case gracefully — empty .cs set is legitimate
        // for non-C# projects, the workflow falls back to git+failure-set signals.
        var lines = (result.Output ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            return new ComputeAstHashOutput(
                AstHash: EmptyHash,
                FilesHashed: 0,
                NoCSharpFiles: true);
        }

        // Format: line 0 = count, line 1 = aggregate hash.
        if (!int.TryParse(lines[0].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var count))
        {
            count = 0;
        }

        var hash = lines.Length > 1 ? lines[1].Trim() : EmptyHash;
        if (string.IsNullOrWhiteSpace(hash))
        {
            hash = EmptyHash;
        }

        return new ComputeAstHashOutput(
            AstHash: hash,
            FilesHashed: count,
            NoCSharpFiles: count == 0);
    }

    // ─── GetGitStateAsync ─────────────────────────────────────────────────

    /// <summary>
    /// Capture the workspace's git HEAD + dirty count atomically. The
    /// SmartIterativeLoop workflow uses this between iterations as one of
    /// the multi-signal no-progress detector inputs (alongside AST hash and
    /// — between bursts — failure-set hash).
    /// </summary>
    [Activity]
    public async Task<GetGitStateOutput> GetGitStateAsync(GetGitStateInput input)
    {
        ValidateContainerId(input.ContainerId);
        var workspace = NormalizeWorkspace(input.WorkspacePath);
        var ct = ActivityExecutionContext.Current.CancellationToken;

        // First check if .git exists at all. Workflows running on a fresh
        // scratch directory (early SmartImprove iterations on a brand-new
        // project) may not have a git repo — they should fall back to the
        // filesystem-delta signal rather than try to interpret git output.
        var checkScript =
            "set -uo pipefail; cd " + ShellQuote(workspace) + " && " +
            "if [ ! -d .git ]; then echo 'NOT_GIT'; exit 0; fi; " +
            "echo HEAD=$(git rev-parse HEAD 2>/dev/null || echo ''); " +
            "echo DIRTY_COUNT=$(git status --porcelain 2>/dev/null | wc -l | tr -d '[:space:]')";

        var req = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", checkScript },
            WorkingDirectory: workspace);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, req, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ApplicationFailureException(
                $"GetGitState failed: {ex.Message}",
                errorType: "GitStateError",
                nonRetryable: false);
        }

        var output = result.Output ?? "";
        if (output.Contains("NOT_GIT", StringComparison.Ordinal))
        {
            return new GetGitStateOutput(
                HeadSha: "",
                DirtyCount: 0,
                IsClean: true,
                NotAGitRepo: true);
        }

        // Parse "HEAD=<sha>" and "DIRTY_COUNT=<n>" lines.
        string head = "";
        int dirty = 0;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.StartsWith("HEAD=", StringComparison.Ordinal))
            {
                head = line[5..].Trim();
            }
            else if (line.StartsWith("DIRTY_COUNT=", StringComparison.Ordinal))
            {
                int.TryParse(line[12..].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out dirty);
            }
        }

        return new GetGitStateOutput(
            HeadSha: head,
            DirtyCount: dirty,
            IsClean: dirty == 0,
            NotAGitRepo: false);
    }

    // ─── VerifyHarnessAsync ────────────────────────────────────────────────

    /// <summary>
    /// Run the previously-planned verification harness inside the workspace
    /// container and parse per-rubric-item results. ALL failures default to
    /// classification "real" — the workflow's next step is to call
    /// <c>AiActivities.ClassifyFailuresAsync</c> which downgrades some to
    /// "structural" or "environmental" via LLM-as-judge.
    /// </summary>
    /// <remarks>
    /// <para><b>CleanRebuild semantics.</b> When true (the second of two
    /// separated verifier runs per the dual-clean-verify termination rule),
    /// we delete <c>bin/</c>, <c>obj/</c>, <c>node_modules/.cache</c>,
    /// <c>dist/</c>, <c>.next/</c>, <c>target/</c>, and <c>__pycache__/</c>
    /// before running the harness. This defeats cached/false-green results
    /// — see newplan.md §4 (anti-reward-hacking).
    /// </para>
    /// <para><b>Seed</b> is exposed as the <c>SMARTIMPROVE_SEED</c> env var
    /// so tests that consume it (Playwright random ordering, property-based
    /// tests, etc.) can be deterministic per run but different across runs.
    /// </para>
    /// </remarks>
    [Activity]
    public async Task<VerifyHarnessOutput> VerifyHarnessAsync(VerifyHarnessInput input)
    {
        ValidateContainerId(input.ContainerId);
        if (string.IsNullOrWhiteSpace(input.HarnessScriptPath))
            throw new ApplicationFailureException(
                "HarnessScriptPath is required.",
                errorType: "ConfigError",
                nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var workspace = NormalizeWorkspace(input.WorkspacePath);
        var timeout = input.TimeoutSeconds <= 0 ? 1800 : input.TimeoutSeconds;
        var seed = input.Seed == 0 ? Random.Shared.Next() : input.Seed;

        // Optional clean rebuild before run #2.
        if (input.CleanRebuild)
        {
            await CleanRebuildArtifactsAsync(input.ContainerId, workspace, ct);
        }

        // Prepare a heartbeat-on-timer task that pumps every 30 seconds — long
        // build/test/playwright runs would otherwise blow HeartbeatTimeout.
        // Activity is wrapped in ActivityProfiles.Verify (30 min S2C + 10 min
        // heartbeat) at the workflow side.
        var ctx = ActivityExecutionContext.Current;
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = HeartbeatLoopAsync(ctx, heartbeatCts.Token);

        ExecResult result;
        try
        {
            // bash <script> — wrapped in `timeout` so a hung subprocess (e.g.
            // a server that never returns) is killed within bound. The seed
            // env var lets the harness make deterministic runs distinguishable.
            var pipeline =
                $"export SMARTIMPROVE_SEED={seed} && " +
                $"timeout --signal=KILL {timeout} bash {ShellQuote(input.HarnessScriptPath)} || true";

            var request = new ContainerExecRequest(
                FileName: "bash",
                Arguments: new[] { "-lc", pipeline },
                WorkingDirectory: workspace);

            result = await _docker.ExecAsync(input.ContainerId, request, ct);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { /* swallow */ }
        }

        return ParseHarnessOutput(result.Output ?? "", input.RubricJson);
    }

    /// <summary>
    /// Pure parser: given the raw harness stdout (one JSON object per line)
    /// and the rubric JSON, produce the bucketed VerifyHarnessOutput.
    /// Exposed internal so tests can drive it without a container.
    /// </summary>
    internal static VerifyHarnessOutput ParseHarnessOutput(string harnessStdout, string rubricJson)
    {
        // Build a lookup of rubricId → priority so each failure can be
        // bucketed P0/P1/P2/P3 without re-parsing the rubric per line.
        var priorityById = ParseRubricPriorities(rubricJson);

        var failures = new List<RubricFailure>();
        var failureIds = new SortedSet<string>(StringComparer.Ordinal);

        var lines = harnessStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] != '{') continue;

            string id;
            string status;
            string evidence;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                evidence = root.TryGetProperty("evidence", out var e) ? e.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                continue; // ignore non-JSON noise lines
            }

            if (string.IsNullOrEmpty(id) || !string.Equals(status, "fail",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var priority = priorityById.TryGetValue(id, out var p) ? p : "P1";
            // Default classification = "real". ClassifyFailuresAsync downgrades.
            failures.Add(new RubricFailure(
                RubricItemId: id,
                Priority: priority,
                Classification: "real",
                Evidence: TruncateEvidence(evidence)));
            failureIds.Add(id);
        }

        var realP0 = failures.Count(f => f.Priority == "P0" && f.Classification == "real");
        var realP1 = failures.Count(f => f.Priority == "P1" && f.Classification == "real");
        var realP2 = failures.Count(f => f.Priority == "P2" && f.Classification == "real");
        var realP3 = failures.Count(f => f.Priority == "P3" && f.Classification == "real");

        return new VerifyHarnessOutput(
            RealP0Count: realP0,
            RealP1Count: realP1,
            RealP2Count: realP2,
            RealP3Count: realP3,
            StructuralCount: 0,
            EnvironmentalCount: 0,
            Failures: failures,
            FailureSetHash: HashFailureIdSet(failureIds));
    }

    /// <summary>
    /// SHA-256 of the sorted, comma-joined failure-id set. Workflow uses
    /// this to detect "same failures across N iterations" (one of the three
    /// no-progress signals). Returns the empty-hash sentinel for an empty set
    /// so a clean verify run is comparable to itself. Sorts defensively so
    /// callers don't have to.
    /// </summary>
    internal static string HashFailureIdSet(IEnumerable<string> ids)
    {
        // Defensive sort with deduplication so caller order doesn't matter.
        // The activity already passes a SortedSet but the public helper is
        // exposed for tests + future callers — keep it idempotent.
        var sorted = new SortedSet<string>(ids ?? Array.Empty<string>(), StringComparer.Ordinal);
        var joined = string.Join(",", sorted);
        if (string.IsNullOrEmpty(joined)) return EmptyHash;

        var bytes = Encoding.UTF8.GetBytes(joined);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Dictionary<string, string> ParseRubricPriorities(string rubricJson)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rubricJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(rubricJson);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return result;
            if (items.ValueKind != JsonValueKind.Array) return result;

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var id)) continue;
                var idStr = id.GetString();
                if (string.IsNullOrEmpty(idStr)) continue;
                var prio = item.TryGetProperty("priority", out var p)
                    ? p.GetString() ?? "P1" : "P1";
                result[idStr] = prio;
            }
        }
        catch (JsonException)
        {
            // Caller will treat all failures as P1 (safe default).
        }

        return result;
    }

    private static string TruncateEvidence(string evidence) =>
        string.IsNullOrEmpty(evidence) ? "" :
        (evidence.Length <= 200 ? evidence : evidence[..200] + "...");

    /// <summary>
    /// Wipe build artifacts so the second separated verifier run gets a
    /// truly fresh build. Anything cached (.NET incremental compile,
    /// Node module cache, Playwright traces) is fair game.
    /// </summary>
    private async Task CleanRebuildArtifactsAsync(string containerId, string workspace, CancellationToken ct)
    {
        // -mindepth 2 ensures the workspace root itself (e.g. /workspace) is
        // not removed if a directory matched; -delete skips unmatched paths
        // gracefully. List explicitly to avoid mass-deletion of user state.
        var script =
            "set -uo pipefail; cd " + ShellQuote(workspace) + " && " +
            "rm -rf bin obj dist build target .next .nuxt out " +
            "node_modules/.cache __pycache__ .pytest_cache " +
            "playwright-report test-results 2>/dev/null || true";

        var req = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", script },
            WorkingDirectory: workspace);

        await _docker.ExecAsync(containerId, req, ct);
    }

    private static async Task HeartbeatLoopAsync(ActivityExecutionContext ctx, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                ctx.Heartbeat();
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static void ValidateContainerId(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            throw new ApplicationFailureException(
                "ContainerId is required.",
                errorType: "ConfigError",
                nonRetryable: true);
    }

    private static string NormalizeWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            return "/workspace";

        // Coerce host paths (e.g. "C:/Users/x/work") to the container default.
        // Inside the container the workspace is always /workspace; the host
        // path is meaningless here.
        if (workspacePath.Contains(':') || workspacePath.StartsWith('\\'))
            return "/workspace";

        return workspacePath.TrimEnd('/');
    }

    private static IReadOnlyList<string> MergeExcludes(IReadOnlyList<string>? caller)
    {
        if (caller == null || caller.Count == 0) return DefaultExcludes;

        var merged = new List<string>(DefaultExcludes);
        foreach (var ex in caller)
        {
            var trimmed = ex.Trim().Trim('/');
            if (trimmed.Length > 0 && !merged.Contains(trimmed))
                merged.Add(trimmed);
        }
        return merged;
    }

    private static string BuildSnapshotPipeline(
        string workspace, IReadOnlyList<string> excludes, int maxFiles)
    {
        // -path './bin' -prune -o ... -path './obj' -prune -o ... -type f -print0
        var sb = new StringBuilder();
        sb.Append("set -o pipefail; cd ").Append(ShellQuote(workspace)).Append(" && find . ");

        foreach (var ex in excludes)
        {
            sb.Append("-path ").Append(ShellQuote("./" + ex)).Append(" -prune -o ");
        }

        // -type f -print0 => null-separated, robust against newlines in paths.
        // sha256sum reads -- - file list; head limits to maxFiles to bound
        // payload size.
        sb.Append("-type f -print0 ")
          .Append("| head -z -n ").Append(maxFiles)
          .Append(" | xargs -0 sha256sum 2>/dev/null ")
          .Append("| LC_ALL=C sort -k 2");

        return sb.ToString();
    }

    private static string BuildAstHashPipeline(
        string workspace, IReadOnlyList<string>? files)
    {
        // Step 1 — assemble file list. When the caller passed specific files,
        // honor that; otherwise scan all .cs under workspace excluding bin/obj.
        var sb = new StringBuilder();
        sb.Append("set -o pipefail; cd ").Append(ShellQuote(workspace)).Append(" && ");

        if (files != null && files.Count > 0)
        {
            sb.Append("FILES=$(printf '%s\\n'");
            foreach (var f in files)
            {
                sb.Append(' ').Append(ShellQuote(f));
            }
            sb.Append(')');
        }
        else
        {
            sb.Append("FILES=$(find . -name '*.cs' -type f ")
              .Append("-not -path './bin/*' -not -path './obj/*' ")
              .Append("-not -path './.smartimprove/*')");
        }

        // Step 2 — normalize each file:
        //   tr '\n' ' ' to flatten so block comments span line boundaries
        //   sed -E to strip /* ... */ then // ...
        //   tr -d '[:space:]' to drop all whitespace
        // Step 3 — sha256sum the normalized stream, prefix with file name,
        // sort, then aggregate-hash. Emit count line + aggregate line.
        //
        // The awk dance keeps just the leading hash per file so the per-file
        // hash + path pair sorts deterministically.
        sb.Append(@"
COUNT=$(printf '%s\n' ""$FILES"" | grep -c . || true)
if [ ""$COUNT"" -eq 0 ]; then
  echo 0
  exit 0
fi
PER_FILE=$(printf '%s\n' ""$FILES"" | while IFS= read -r f; do
  if [ -f ""$f"" ]; then
    H=$(tr '\n' ' ' < ""$f"" \
        | sed -E 's|/\*[^*]*\*+([^/*][^*]*\*+)*/||g' \
        | sed -E 's|//[^\n]*||g' \
        | tr -d '[:space:]' \
        | sha256sum \
        | awk '{print $1}')
    printf '%s  %s\n' ""$H"" ""$f""
  fi
done | LC_ALL=C sort)
AGG=$(printf '%s' ""$PER_FILE"" | sha256sum | awk '{print $1}')
echo ""$COUNT""
echo ""$AGG""
");

        return sb.ToString();
    }

    /// <summary>
    /// Parse <c>sha256sum</c> output (each line is "&lt;hash&gt;  &lt;path&gt;")
    /// into a path → hash dictionary. Handles BSD sha256sum output too
    /// (SHA256 (path) = hash) by sniffing the leading char.
    /// </summary>
    internal static (Dictionary<string, string> Hashes, bool Truncated)
        ParseShaSumOutput(string output, int maxFiles)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var truncated = false;
        var lines = output.Split('\n');
        foreach (var rawLine in lines)
        {
            if (hashes.Count >= maxFiles)
            {
                // We only mark truncation when at least one more non-empty
                // line followed — otherwise the cap exactly matches the file
                // count and there's nothing missed.
                if (!string.IsNullOrWhiteSpace(rawLine)) truncated = true;
                continue;
            }

            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            // GNU coreutils format: "<64-hex>  <path>"
            // Split on two-space delimiter; sha256sum guarantees exactly two.
            var sep = line.IndexOf("  ", StringComparison.Ordinal);
            if (sep <= 0) continue;

            var hash = line[..sep].Trim();
            var path = line[(sep + 2)..].Trim();
            if (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
            if (hash.Length != 64 || string.IsNullOrEmpty(path)) continue;

            hashes[path] = hash;
        }

        return (hashes, truncated);
    }

    /// <summary>
    /// Compute the difference between two snapshots. Pure function; lives
    /// here so workflows and tests both depend on the same logic.
    /// </summary>
    public static FilesystemDelta ComputeDelta(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var created = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();

        foreach (var kv in after)
        {
            if (!before.TryGetValue(kv.Key, out var beforeHash))
                created.Add(kv.Key);
            else if (!string.Equals(beforeHash, kv.Value, StringComparison.Ordinal))
                modified.Add(kv.Key);
        }

        foreach (var key in before.Keys)
        {
            if (!after.ContainsKey(key))
                deleted.Add(key);
        }

        created.Sort(StringComparer.Ordinal);
        modified.Sort(StringComparer.Ordinal);
        deleted.Sort(StringComparer.Ordinal);

        return new FilesystemDelta(created, modified, deleted);
    }

    /// <summary>
    /// SHA-256 of empty input — pre-computed sentinel for "no files hashed"
    /// so the workflow can compare against it without re-hashing nothing.
    /// </summary>
    internal const string EmptyHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static string ShellQuote(string s)
    {
        // Single-quote-safe shell quoting: ' → '\'' .
        var inner = s.Replace("'", "'\\''");
        return $"'{inner}'";
    }

    private static string Truncate(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "...");
}

