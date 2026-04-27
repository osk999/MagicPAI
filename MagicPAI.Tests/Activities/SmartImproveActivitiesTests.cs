using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.SmartImprove;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="SmartImproveActivities"/>: the filesystem
/// snapshot + lexical AST hash utilities that drive the
/// SmartIterativeLoopWorkflow's silence countdown and multi-signal
/// no-progress detector. See newplan.md §7.1 (test plan), §8 (per-component
/// verification).
/// </summary>
[Trait("Category", "Unit")]
public class SmartImproveActivitiesTests
{
    // ── Pure-function tests ──────────────────────────────────────────────

    [Fact]
    public void ParseShaSumOutput_ParsesGnuFormat()
    {
        const string input =
            "abc123  src/Foo.cs\n" +
            "def456  src/Bar.cs\n";

        var (hashes, truncated) = SmartImproveActivities.ParseShaSumOutput(
            input.Replace("abc123", new string('a', 64)).Replace("def456", new string('d', 64)),
            maxFiles: 100);

        hashes.Should().HaveCount(2);
        hashes["src/Foo.cs"].Should().Be(new string('a', 64));
        hashes["src/Bar.cs"].Should().Be(new string('d', 64));
        truncated.Should().BeFalse();
    }

    [Fact]
    public void ParseShaSumOutput_StripsLeadingDotSlash()
    {
        var hash = new string('1', 64);
        var input = $"{hash}  ./src/Foo.cs\n";

        var (hashes, _) = SmartImproveActivities.ParseShaSumOutput(input, 100);

        hashes.Should().ContainKey("src/Foo.cs");
        hashes.Should().NotContainKey("./src/Foo.cs");
    }

    [Fact]
    public void ParseShaSumOutput_SkipsLinesWithoutDoubleSpaceSeparator()
    {
        var hash = new string('a', 64);
        var input =
            $"{hash}  good.cs\n" +
            "garbage line without separator\n" +
            $"{hash} only-single-space.cs\n";

        var (hashes, _) = SmartImproveActivities.ParseShaSumOutput(input, 100);

        hashes.Should().HaveCount(1);
        hashes.Should().ContainKey("good.cs");
    }

    [Fact]
    public void ParseShaSumOutput_RejectsHashesNot64Hex()
    {
        const string input =
            "tooshort  a.cs\n" +
            "01234567890123456789012345678901234567890123456789012345678901234  a.cs\n"; // 65 chars

        var (hashes, _) = SmartImproveActivities.ParseShaSumOutput(input, 100);

        hashes.Should().BeEmpty();
    }

    [Fact]
    public void ParseShaSumOutput_TruncatesAtMaxFilesAndFlagsTruncated()
    {
        var hash = new string('a', 64);
        var lines = string.Concat(Enumerable.Range(0, 5).Select(i => $"{hash}  f{i}.cs\n"));

        var (hashes, truncated) = SmartImproveActivities.ParseShaSumOutput(lines, maxFiles: 3);

        hashes.Should().HaveCount(3);
        truncated.Should().BeTrue();
    }

    [Fact]
    public void ParseShaSumOutput_NoTruncationWhenExactlyAtCap()
    {
        var hash = new string('a', 64);
        var lines = string.Concat(Enumerable.Range(0, 3).Select(i => $"{hash}  f{i}.cs\n"));

        var (hashes, truncated) = SmartImproveActivities.ParseShaSumOutput(lines, maxFiles: 3);

        hashes.Should().HaveCount(3);
        truncated.Should().BeFalse();
    }

    // ── ComputeDelta pure-function tests ─────────────────────────────────

    [Fact]
    public void ComputeDelta_EmptyOnIdenticalSnapshots()
    {
        var same = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
        };
        var delta = SmartImproveActivities.ComputeDelta(same, same);
        delta.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeDelta_DetectsCreatedFiles()
    {
        var before = new Dictionary<string, string> { ["a.cs"] = "h1" };
        var after = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
        };

        var delta = SmartImproveActivities.ComputeDelta(before, after);

        delta.Created.Should().BeEquivalentTo(new[] { "b.cs" });
        delta.Modified.Should().BeEmpty();
        delta.Deleted.Should().BeEmpty();
        delta.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ComputeDelta_DetectsModifiedFiles()
    {
        var before = new Dictionary<string, string> { ["a.cs"] = "old" };
        var after = new Dictionary<string, string> { ["a.cs"] = "new" };

        var delta = SmartImproveActivities.ComputeDelta(before, after);

        delta.Modified.Should().BeEquivalentTo(new[] { "a.cs" });
        delta.Created.Should().BeEmpty();
        delta.Deleted.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_DetectsDeletedFiles()
    {
        var before = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["gone.cs"] = "h2",
        };
        var after = new Dictionary<string, string> { ["a.cs"] = "h1" };

        var delta = SmartImproveActivities.ComputeDelta(before, after);

        delta.Deleted.Should().BeEquivalentTo(new[] { "gone.cs" });
        delta.Created.Should().BeEmpty();
        delta.Modified.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_HandlesAllThreeChangesAtOnce()
    {
        var before = new Dictionary<string, string>
        {
            ["unchanged.cs"] = "u",
            ["modified.cs"] = "old",
            ["deleted.cs"] = "d",
        };
        var after = new Dictionary<string, string>
        {
            ["unchanged.cs"] = "u",
            ["modified.cs"] = "new",
            ["created.cs"] = "c",
        };

        var delta = SmartImproveActivities.ComputeDelta(before, after);

        delta.Created.Should().BeEquivalentTo(new[] { "created.cs" });
        delta.Modified.Should().BeEquivalentTo(new[] { "modified.cs" });
        delta.Deleted.Should().BeEquivalentTo(new[] { "deleted.cs" });
        delta.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ComputeDelta_OutputIsSortedDeterministically()
    {
        var before = new Dictionary<string, string>();
        var after = new Dictionary<string, string>
        {
            ["z.cs"] = "1",
            ["a.cs"] = "2",
            ["m.cs"] = "3",
        };

        var delta = SmartImproveActivities.ComputeDelta(before, after);

        delta.Created.Should().Equal(new[] { "a.cs", "m.cs", "z.cs" });
    }

    // ── FilesystemDelta property tests ───────────────────────────────────

    [Fact]
    public void FilesystemDelta_TouchedTestFiles_DetectsTestsFolder()
    {
        var delta = new FilesystemDelta(
            Created: new[] { "src/foo.cs" },
            Modified: new[] { "tests/bar_test.cs" },
            Deleted: Array.Empty<string>());

        delta.TouchedTestFiles.Should().BeTrue();
    }

    [Fact]
    public void FilesystemDelta_TouchedTestFiles_DetectsSpecExtension()
    {
        var delta = new FilesystemDelta(
            Created: new[] { "components/login.spec.ts" },
            Modified: Array.Empty<string>(),
            Deleted: Array.Empty<string>());

        delta.TouchedTestFiles.Should().BeTrue();
    }

    [Fact]
    public void FilesystemDelta_TouchedTestFiles_DetectsTestsCsConvention()
    {
        var delta = new FilesystemDelta(
            Created: Array.Empty<string>(),
            Modified: new[] { "MagicPAI.Tests/Workflows/SimpleAgentWorkflowTests.cs" },
            Deleted: Array.Empty<string>());

        delta.TouchedTestFiles.Should().BeTrue();
    }

    [Fact]
    public void FilesystemDelta_TouchedTestFiles_FalseForPureSrc()
    {
        var delta = new FilesystemDelta(
            Created: new[] { "src/Foo.cs" },
            Modified: new[] { "src/Bar.cs" },
            Deleted: new[] { "src/Baz.cs" });

        delta.TouchedTestFiles.Should().BeFalse();
    }

    [Fact]
    public void FilesystemDelta_IsEmpty_OnlyTrueWhenAllListsEmpty()
    {
        var empty = new FilesystemDelta(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
        var oneCreated = new FilesystemDelta(new[] { "a" }, Array.Empty<string>(), Array.Empty<string>());

        empty.IsEmpty.Should().BeTrue();
        oneCreated.IsEmpty.Should().BeFalse();
    }

    // ── SnapshotFilesystemAsync — happy path with mocked container manager ─

    [Fact]
    public async Task SnapshotFilesystemAsync_ParsesPipelineOutputAndReturnsHashes()
    {
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var dockerOutput = $"{hashA}  src/A.cs\n{hashB}  src/B.cs\n";

        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, dockerOutput, null!));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new SnapshotFilesystemInput(
            ContainerId: "container-1",
            WorkspacePath: "/workspace");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.SnapshotFilesystemAsync(input));

        output.FileCount.Should().Be(2);
        output.FileHashes.Should().ContainKeys("src/A.cs", "src/B.cs");
        output.FileHashes["src/A.cs"].Should().Be(hashA);
        output.TruncatedByMaxFiles.Should().BeFalse();
        output.CapturedAtUnixSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SnapshotFilesystemAsync_RejectsEmptyContainerId()
    {
        var docker = new Mock<IContainerManager>();
        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new SnapshotFilesystemInput(
            ContainerId: "",
            WorkspacePath: "/workspace");

        var env = new ActivityEnvironment();
        var act = () => env.RunAsync(() => sut.SnapshotFilesystemAsync(input));

        await act.Should().ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
            .Where(ex => ex.ErrorType == "ConfigError");
    }

    [Fact]
    public async Task SnapshotFilesystemAsync_PassesPartialResultsWhenPipelineExitsNonZero()
    {
        // sha256sum exits non-zero when at least one file disappears mid-walk
        // (race) but the rest of the output is still valid. Activity should
        // surface what it has rather than failing.
        var hashA = new string('c', 64);
        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, $"{hashA}  partial.cs\n", "sha256sum: vanished.cs: No such file"));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new SnapshotFilesystemInput("c", "/workspace");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.SnapshotFilesystemAsync(input));

        output.FileCount.Should().Be(1);
        output.FileHashes.Should().ContainKey("partial.cs");
    }

    [Fact]
    public async Task SnapshotFilesystemAsync_RoutesPipelineThroughBashLoginShell()
    {
        ContainerExecRequest? captured = null;
        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecRequest, CancellationToken>(
                (_, req, _) => captured = req)
            .ReturnsAsync(new ExecResult(0, "", null!));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new SnapshotFilesystemInput("c", "/workspace");
        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.SnapshotFilesystemAsync(input));

        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("bash");
        captured.Arguments.Should().HaveCount(2);
        captured.Arguments[0].Should().Be("-lc");
        // Pipeline must include a sha256sum and the workspace path (quoted).
        captured.Arguments[1].Should().Contain("sha256sum");
        captured.Arguments[1].Should().Contain("/workspace");
        // Default exclusions must appear so the pipeline skips bin/obj/etc.
        captured.Arguments[1].Should().Contain("./bin");
        captured.Arguments[1].Should().Contain("./obj");
        captured.Arguments[1].Should().Contain("./node_modules");
    }

    // ── ComputeAstHashAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ComputeAstHashAsync_NoFilesReturnsEmptySentinel()
    {
        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "0\n", null!));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new ComputeAstHashInput(ContainerId: "c", WorkspacePath: "/workspace");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ComputeAstHashAsync(input));

        output.NoCSharpFiles.Should().BeTrue();
        output.FilesHashed.Should().Be(0);
        // The "no files" sentinel is the well-known SHA-256 of empty input.
        output.AstHash.Should().Be(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task ComputeAstHashAsync_ParsesCountAndAggregateHash()
    {
        var hash = new string('f', 64);
        var dockerOutput = $"7\n{hash}\n";

        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, dockerOutput, null!));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new ComputeAstHashInput("c", "/workspace");
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ComputeAstHashAsync(input));

        output.FilesHashed.Should().Be(7);
        output.AstHash.Should().Be(hash);
        output.NoCSharpFiles.Should().BeFalse();
    }

    [Fact]
    public async Task ComputeAstHashAsync_FallsBackToEmptyHashWhenAggregateLineMissing()
    {
        // Edge case: pipeline emits only the count line. Workflow should
        // still get a defined hash so equality comparisons across iterations
        // remain stable.
        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "5\n", null!));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new ComputeAstHashInput("c", "/workspace");
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ComputeAstHashAsync(input));

        output.FilesHashed.Should().Be(5);
        output.AstHash.Should().Be(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task ComputeAstHashAsync_RejectsEmptyContainerId()
    {
        var docker = new Mock<IContainerManager>();
        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new ComputeAstHashInput("", "/workspace");

        var env = new ActivityEnvironment();
        var act = () => env.RunAsync(() => sut.ComputeAstHashAsync(input));

        await act.Should().ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
            .Where(ex => ex.ErrorType == "ConfigError");
    }

    // ── VerifyHarnessAsync — pure parser tests ──────────────────────────

    [Fact]
    public void ParseHarnessOutput_ParsesPassFailLines()
    {
        const string rubric = """
            { "items": [
              { "id":"build", "priority":"P0" },
              { "id":"test",  "priority":"P0" },
              { "id":"lint",  "priority":"P2" }
            ] }
            """;

        const string output =
            "{\"id\":\"build\",\"status\":\"pass\",\"exitCode\":0,\"evidence\":\"\"}\n" +
            "{\"id\":\"test\",\"status\":\"fail\",\"exitCode\":1,\"evidence\":\"3 failed\"}\n" +
            "{\"id\":\"lint\",\"status\":\"fail\",\"exitCode\":1,\"evidence\":\"warnings\"}\n";

        var result = SmartImproveActivities.ParseHarnessOutput(output, rubric);

        result.Failures.Should().HaveCount(2);
        result.Failures.Should().Contain(f => f.RubricItemId == "test" && f.Priority == "P0");
        result.Failures.Should().Contain(f => f.RubricItemId == "lint" && f.Priority == "P2");
        result.RealP0Count.Should().Be(1); // test failed
        result.RealP1Count.Should().Be(0);
        result.RealP2Count.Should().Be(1); // lint failed
        result.FailureSetHash.Should().NotBeNullOrEmpty();
        result.FailureSetHash.Should().NotBe(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void ParseHarnessOutput_AllPassYieldsEmptySentinelHash()
    {
        const string rubric = "{\"items\":[{\"id\":\"a\",\"priority\":\"P0\"}]}";
        const string output = "{\"id\":\"a\",\"status\":\"pass\",\"exitCode\":0,\"evidence\":\"\"}\n";

        var result = SmartImproveActivities.ParseHarnessOutput(output, rubric);

        result.Failures.Should().BeEmpty();
        result.RealP0Count.Should().Be(0);
        // The empty-set sentinel ensures two clean runs hash-compare equal.
        result.FailureSetHash.Should().Be(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void ParseHarnessOutput_DefaultsUnknownPriorityToP1()
    {
        // Rubric lookup fails for "phantom" — fall back to P1, don't drop it.
        const string rubric = "{\"items\":[{\"id\":\"build\",\"priority\":\"P0\"}]}";
        const string output = "{\"id\":\"phantom\",\"status\":\"fail\",\"exitCode\":1,\"evidence\":\"x\"}\n";

        var result = SmartImproveActivities.ParseHarnessOutput(output, rubric);

        result.Failures.Should().HaveCount(1);
        result.Failures[0].Priority.Should().Be("P1");
    }

    [Fact]
    public void ParseHarnessOutput_IgnoresNonJsonNoiseLines()
    {
        const string rubric = "{\"items\":[{\"id\":\"build\",\"priority\":\"P0\"}]}";
        const string output =
            "+ npm install\n" +
            "added 320 packages in 4s\n" +
            "{\"id\":\"build\",\"status\":\"fail\",\"exitCode\":1,\"evidence\":\"oops\"}\n" +
            "stderr noise\n";

        var result = SmartImproveActivities.ParseHarnessOutput(output, rubric);

        result.Failures.Should().HaveCount(1);
        result.Failures[0].RubricItemId.Should().Be("build");
    }

    [Fact]
    public void ParseHarnessOutput_HandlesMissingItemsArrayInRubric()
    {
        // Missing or malformed rubric → priority defaults to P1 for every fail.
        const string output =
            "{\"id\":\"x\",\"status\":\"fail\",\"exitCode\":1,\"evidence\":\"\"}\n";

        var resultEmpty = SmartImproveActivities.ParseHarnessOutput(output, "{}");
        var resultJunk = SmartImproveActivities.ParseHarnessOutput(output, "<<not json>>");

        resultEmpty.Failures.Should().HaveCount(1);
        resultEmpty.Failures[0].Priority.Should().Be("P1");
        resultJunk.Failures.Should().HaveCount(1);
        resultJunk.Failures[0].Priority.Should().Be("P1");
    }

    [Fact]
    public void HashFailureIdSet_StableAcrossOrdering()
    {
        var a = SmartImproveActivities.HashFailureIdSet(new[] { "build", "test", "lint" });
        var b = SmartImproveActivities.HashFailureIdSet(new[] { "lint", "build", "test" });
        a.Should().Be(b);
    }

    [Fact]
    public void HashFailureIdSet_ChangesWhenSetDiffers()
    {
        var a = SmartImproveActivities.HashFailureIdSet(new[] { "build", "test" });
        var b = SmartImproveActivities.HashFailureIdSet(new[] { "build", "test", "lint" });
        a.Should().NotBe(b);
    }

    [Fact]
    public async Task VerifyHarnessAsync_RejectsEmptyContainerId()
    {
        var docker = new Mock<IContainerManager>();
        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new VerifyHarnessInput(
            SessionId: "s",
            ContainerId: "",
            WorkspacePath: "/workspace",
            HarnessScriptPath: ".smartimprove/harness.sh",
            RubricJson: "{}");

        var env = new ActivityEnvironment();
        var act = () => env.RunAsync(() => sut.VerifyHarnessAsync(input));

        await act.Should().ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
            .Where(ex => ex.ErrorType == "ConfigError");
    }

    [Fact]
    public async Task VerifyHarnessAsync_RejectsEmptyHarnessPath()
    {
        var docker = new Mock<IContainerManager>();
        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new VerifyHarnessInput(
            "s", "ctr-1", "/workspace", "", "{}");

        var env = new ActivityEnvironment();
        var act = () => env.RunAsync(() => sut.VerifyHarnessAsync(input));

        await act.Should().ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
            .Where(ex => ex.ErrorType == "ConfigError");
    }

    [Fact]
    public async Task VerifyHarnessAsync_RunsCleanRebuildBeforeHarnessOnSecondRun()
    {
        var calls = new List<string>();
        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecRequest, CancellationToken>(
                (_, req, _) => calls.Add(req.Arguments[1]))
            .ReturnsAsync(new ExecResult(0, "", ""));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new VerifyHarnessInput(
            SessionId: "s",
            ContainerId: "ctr-1",
            WorkspacePath: "/workspace",
            HarnessScriptPath: ".smartimprove/harness.sh",
            RubricJson: "{\"items\":[]}",
            CleanRebuild: true);

        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.VerifyHarnessAsync(input));

        // First call should be the cleanup, second the harness invocation.
        calls.Should().HaveCountGreaterOrEqualTo(2);
        calls[0].Should().Contain("rm -rf bin obj");
        calls[1].Should().Contain("bash '.smartimprove/harness.sh'");
        calls[1].Should().Contain("SMARTIMPROVE_SEED=");
    }

    [Fact]
    public async Task ComputeAstHashAsync_HonorsExplicitFileList()
    {
        ContainerExecRequest? captured = null;
        var docker = new Mock<IContainerManager>();
        docker.Setup(d => d.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecRequest, CancellationToken>(
                (_, req, _) => captured = req)
            .ReturnsAsync(new ExecResult(0, "2\n" + new string('a', 64) + "\n", null!));

        var sut = new SmartImproveActivities(docker.Object, NullLogger<SmartImproveActivities>.Instance);
        var input = new ComputeAstHashInput(
            ContainerId: "c",
            WorkspacePath: "/workspace",
            Files: new[] { "src/A.cs", "src/B.cs" });

        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.ComputeAstHashAsync(input));

        captured.Should().NotBeNull();
        captured!.Arguments[1].Should().Contain("src/A.cs");
        captured.Arguments[1].Should().Contain("src/B.cs");
        // When file list is explicit, the script should NOT do a bare find.
        captured.Arguments[1].Should().NotContain("find . -name");
    }
}
