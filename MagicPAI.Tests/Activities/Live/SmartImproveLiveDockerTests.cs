using System.Diagnostics;
using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.SmartImprove;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Testing;
using Xunit.Abstractions;

namespace MagicPAI.Tests.Activities.Live;

/// <summary>
/// Live E2E verification for the deterministic (non-LLM) SmartImprove
/// activities: <see cref="SmartImproveActivities.SnapshotFilesystemAsync"/>,
/// <see cref="SmartImproveActivities.ComputeAstHashAsync"/>,
/// <see cref="SmartImproveActivities.GetGitStateAsync"/>, and
/// <see cref="SmartImproveActivities.VerifyHarnessAsync"/>.
///
/// These tests are activity-level (no Temporal workflow + no LLM) so they
/// run in seconds and incur no token cost. They validate the actual bash
/// pipelines against a real <c>magicpai-env</c> container with a scratch
/// workspace populated per-test.
///
/// Tagged <c>[Trait("Category", "SmartImproveE2E")]</c>; tests are skipped
/// when Docker isn't available. See newplan.md §7.4 (Phase 7 live tests).
/// </summary>
[Trait("Category", "SmartImproveE2E")]
public class SmartImproveLiveDockerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly DockerContainerManager _docker;
    private readonly SmartImproveActivities _sut;
    private string? _containerId;
    private string? _workspaceDir;

    public SmartImproveLiveDockerTests(ITestOutputHelper output)
    {
        _output = output;
        _docker = new DockerContainerManager();
        _sut = new SmartImproveActivities(
            docker: _docker,
            log: NullLogger<SmartImproveActivities>.Instance);
    }

    public async Task InitializeAsync()
    {
        if (!IsDockerAvailable())
        {
            _output.WriteLine("Docker not reachable — live tests will skip.");
            return;
        }

        _workspaceDir = Path.Combine(Path.GetTempPath(),
            "magicpai-smartimprove-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspaceDir);

        // Seed the workspace with a tiny C# library so AST + snapshot tests
        // have real content to hash. Each test populates additional files
        // as needed under its own subdirectory.
        await File.WriteAllTextAsync(
            Path.Combine(_workspaceDir, "Hello.cs"),
            "namespace Sample;\npublic static class Hello { public static string Greet() => \"hi\"; }\n");

        var spawned = await _docker.SpawnAsync(new ContainerConfig
        {
            Image = "magicpai-env:latest",
            WorkspacePath = _workspaceDir,
            ContainerWorkDir = "/workspace",
            MemoryLimitMb = 1024,
            CpuCount = 1,
            EnableGui = false,
        }, CancellationToken.None);

        _containerId = spawned.ContainerId;
        _output.WriteLine($"Spawned container {_containerId} on {_workspaceDir}");
    }

    public async Task DisposeAsync()
    {
        if (_containerId is not null)
        {
            try { await _docker.DestroyAsync(_containerId, CancellationToken.None); }
            catch (Exception ex) { _output.WriteLine($"Destroy failed: {ex.Message}"); }
        }
        if (_workspaceDir is not null && Directory.Exists(_workspaceDir))
        {
            try { Directory.Delete(_workspaceDir, recursive: true); }
            catch { /* best-effort */ }
        }
        _docker.Dispose();
    }

    // ── Snapshot ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task SnapshotFilesystemAsync_Live_ReturnsHashesForRealFiles()
    {
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new SnapshotFilesystemInput(
            ContainerId: _containerId!,
            WorkspacePath: "/workspace");

        var env = new ActivityEnvironment();
        var sw = Stopwatch.StartNew();
        var output = await env.RunAsync(() => _sut.SnapshotFilesystemAsync(input));
        sw.Stop();

        _output.WriteLine($"Snapshot: {output.FileCount} files in {sw.Elapsed.TotalSeconds:F2}s");

        output.FileCount.Should().BeGreaterThan(0,
            "the seeded Hello.cs must show up in the snapshot");
        output.FileHashes.Keys.Should().Contain(k => k.EndsWith("Hello.cs"));
    }

    [SkippableFact]
    public async Task SnapshotFilesystemAsync_Live_HashIsContentOnly_NotMtime()
    {
        // Two snapshots taken with a touch in between (mtime changes, content
        // doesn't) must produce the SAME hash for the touched file. This
        // proves the snapshot is content-based per newplan.md §4 — a model
        // can't game it by touching a file without writing.
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new SnapshotFilesystemInput(_containerId!, "/workspace");
        var env = new ActivityEnvironment();

        var snap1 = await env.RunAsync(() => _sut.SnapshotFilesystemAsync(input));

        // Touch the file inside the container (mtime change only).
        var touch = await _docker.ExecAsync(
            _containerId!, "touch /workspace/Hello.cs", "/workspace", CancellationToken.None);
        touch.ExitCode.Should().Be(0);

        var snap2 = await env.RunAsync(() => _sut.SnapshotFilesystemAsync(input));

        // Same path, same content → hash must be identical.
        var key = snap1.FileHashes.Keys.First(k => k.EndsWith("Hello.cs"));
        snap1.FileHashes[key].Should().Be(snap2.FileHashes[key],
            "content-only hash must be invariant under touch (mtime-only changes)");
    }

    [SkippableFact]
    public async Task SnapshotFilesystemAsync_Live_DetectsContentChange()
    {
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new SnapshotFilesystemInput(_containerId!, "/workspace");
        var env = new ActivityEnvironment();

        var before = await env.RunAsync(() => _sut.SnapshotFilesystemAsync(input));

        // Real content change.
        var write = await _docker.ExecAsync(
            _containerId!,
            "echo '// added comment' >> /workspace/Hello.cs",
            "/workspace",
            CancellationToken.None);
        write.ExitCode.Should().Be(0);

        var after = await env.RunAsync(() => _sut.SnapshotFilesystemAsync(input));

        var delta = SmartImproveActivities.ComputeDelta(before.FileHashes, after.FileHashes);
        delta.Modified.Should().NotBeEmpty(
            "the appended comment must produce a content-hash change");
        delta.Modified.Should().Contain(p => p.EndsWith("Hello.cs"));
    }

    // ── AST hash ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ComputeAstHashAsync_Live_DefeatsWhitespaceChurn()
    {
        // The AST signal exists specifically to defeat the failure mode
        // where a model adds whitespace/comments to fool git no-progress.
        // Two iterations whose .cs files differ only in whitespace+comments
        // MUST produce the same AST hash.
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new ComputeAstHashInput(_containerId!, "/workspace");
        var env = new ActivityEnvironment();

        var before = await env.RunAsync(() => _sut.ComputeAstHashAsync(input));

        // Append whitespace + a comment — semantically identical.
        await _docker.ExecAsync(
            _containerId!,
            "printf '\\n\\n   // a comment that should be stripped\\n' >> /workspace/Hello.cs",
            "/workspace",
            CancellationToken.None);

        var after = await env.RunAsync(() => _sut.ComputeAstHashAsync(input));

        before.AstHash.Should().Be(after.AstHash,
            "whitespace + comment-only changes must NOT change the lexical hash — " +
            "this is the anti-reward-hacking guard from newplan.md §4");
    }

    [SkippableFact]
    public async Task ComputeAstHashAsync_Live_DetectsRealCodeChange()
    {
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new ComputeAstHashInput(_containerId!, "/workspace");
        var env = new ActivityEnvironment();

        var before = await env.RunAsync(() => _sut.ComputeAstHashAsync(input));

        // Real code change: rename method.
        await _docker.ExecAsync(
            _containerId!,
            "sed -i 's/Greet/Hello/g' /workspace/Hello.cs",
            "/workspace",
            CancellationToken.None);

        var after = await env.RunAsync(() => _sut.ComputeAstHashAsync(input));

        before.AstHash.Should().NotBe(after.AstHash,
            "renaming a method is a real code change — must produce a new hash");
    }

    // ── Git state ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetGitStateAsync_Live_NotAGitRepo_FallbackBehavior()
    {
        // Workspace seeded by InitializeAsync has no .git directory →
        // NotAGitRepo=true. Workflow consumers fall back to the
        // filesystem-delta signal.
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new GetGitStateInput(_containerId!, "/workspace");
        var env = new ActivityEnvironment();

        var output = await env.RunAsync(() => _sut.GetGitStateAsync(input));

        output.NotAGitRepo.Should().BeTrue();
        output.HeadSha.Should().BeEmpty();
        output.IsClean.Should().BeTrue();
        output.DirtyCount.Should().Be(0);
    }

    [SkippableFact]
    public async Task GetGitStateAsync_Live_RealRepo_CapturesHeadAndDirty()
    {
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        // Initialize a real git repo + commit ALL files in the workspace.
        // Notes:
        //  • `core.autocrlf=false` prevents git from normalizing CRLF
        //    endings on commit, which would otherwise report the file
        //    as dirty immediately after commit on Windows hosts.
        //  • `git add -A` (instead of just Hello.cs) is required because
        //    the container entrypoint injects auxiliary files like
        //    `.mcp.json` (MCP server configs); leaving them untracked
        //    would make `git status --porcelain` non-empty and the test
        //    would observe IsClean=false post-commit.
        await ExecOrThrowAsync(
            "cd /workspace && git init && " +
            "git config core.autocrlf false && git config core.safecrlf false && " +
            "git config user.email t@test.local && git config user.name 'test' && " +
            "git add -A && git commit -m 'initial' >/dev/null");

        var input = new GetGitStateInput(_containerId!, "/workspace");
        var env = new ActivityEnvironment();

        // Diagnostic: capture git status -s output before asserting so a
        // reviewer can see which files git considers untracked/modified
        // when the assertion fails (CRLF normalization, container-injected
        // files, etc.).
        var statusDiag = await _docker.ExecAsync(
            _containerId!, "cd /workspace && git status -s | head -30",
            "/workspace", CancellationToken.None);
        _output.WriteLine($"git status -s after init+commit:\n{statusDiag.Output}");

        var clean = await env.RunAsync(() => _sut.GetGitStateAsync(input));
        clean.NotAGitRepo.Should().BeFalse();
        clean.HeadSha.Should().NotBeEmpty().And.HaveLength(40);
        clean.IsClean.Should().BeTrue(
            $"git reported dirty entries:\n{statusDiag.Output}");
        clean.DirtyCount.Should().Be(0);

        // Make a change.
        await ExecOrThrowAsync("echo '// dirty' >> /workspace/Hello.cs");

        var dirty = await env.RunAsync(() => _sut.GetGitStateAsync(input));
        dirty.HeadSha.Should().Be(clean.HeadSha, "HEAD did not move");
        dirty.IsClean.Should().BeFalse();
        dirty.DirtyCount.Should().BeGreaterThan(0);
    }

    // ── Verify harness ────────────────────────────────────────────────

    [SkippableFact]
    public async Task VerifyHarnessAsync_Live_RunsHarnessAndParsesPassFail()
    {
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        // Plant a synthetic harness that emits two lines: one pass + one fail.
        const string harness = """
            #!/usr/bin/env bash
            set -uo pipefail
            echo '{"id":"build","status":"pass","exitCode":0,"evidence":""}'
            echo '{"id":"test","status":"fail","exitCode":1,"evidence":"3 failed"}'
            """;
        await ExecOrThrowAsync("mkdir -p /workspace/.smartimprove");
        var writeReq = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", "cat > /workspace/.smartimprove/harness.sh" },
            WorkingDirectory: "/workspace",
            StdinInput: harness);
        var w = await _docker.ExecAsync(_containerId!, writeReq, CancellationToken.None);
        w.ExitCode.Should().Be(0);
        await ExecOrThrowAsync("chmod +x /workspace/.smartimprove/harness.sh");

        const string rubric = """
            { "items": [
                { "id":"build", "priority":"P0" },
                { "id":"test",  "priority":"P0" }
            ] }
            """;
        var input = new VerifyHarnessInput(
            SessionId: "live-harness",
            ContainerId: _containerId!,
            WorkspacePath: "/workspace",
            HarnessScriptPath: ".smartimprove/harness.sh",
            RubricJson: rubric,
            CleanRebuild: false,
            Seed: 42,
            TimeoutSeconds: 30);

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => _sut.VerifyHarnessAsync(input));

        output.Failures.Should().HaveCount(1);
        output.Failures[0].RubricItemId.Should().Be("test");
        output.RealP0Count.Should().Be(1);
        output.FailureSetHash.Should().NotBe(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "non-empty failure set must produce non-empty hash");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task ExecOrThrowAsync(string command)
    {
        var result = await _docker.ExecAsync(
            _containerId!, command, "/workspace", CancellationToken.None);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Setup command failed (exit {result.ExitCode}): {command}\n{result.Error}");
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                ArgumentList = { "version", "--format", "{{.Server.Version}}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            return p.WaitForExit(3000) && p.ExitCode == 0;
        }
        catch { return false; }
    }
}
