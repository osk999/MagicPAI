using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Services;

public class SharedBlackboardTests
{
    private readonly SharedBlackboard _blackboard = new();

    [Fact]
    public void ClaimFile_Succeeds_When_Unclaimed()
    {
        Assert.True(_blackboard.ClaimFile("src/main.cs", "task-1"));
    }

    [Fact]
    public void ClaimFile_Fails_When_Already_Claimed()
    {
        _blackboard.ClaimFile("src/main.cs", "task-1");
        Assert.False(_blackboard.ClaimFile("src/main.cs", "task-2"));
    }

    [Fact]
    public void ClaimFile_Same_Task_Cannot_Reclaim()
    {
        _blackboard.ClaimFile("src/main.cs", "task-1");
        // TryAdd returns false even for the same key/value
        Assert.False(_blackboard.ClaimFile("src/main.cs", "task-1"));
    }

    [Fact]
    public void ReleaseFile_Succeeds_For_Owner()
    {
        _blackboard.ClaimFile("src/main.cs", "task-1");
        Assert.True(_blackboard.ReleaseFile("src/main.cs", "task-1"));
    }

    [Fact]
    public void ReleaseFile_Fails_For_Non_Owner()
    {
        _blackboard.ClaimFile("src/main.cs", "task-1");
        Assert.False(_blackboard.ReleaseFile("src/main.cs", "task-2"));
    }

    [Fact]
    public void ReleaseFile_Fails_When_Not_Claimed()
    {
        Assert.False(_blackboard.ReleaseFile("src/main.cs", "task-1"));
    }

    [Fact]
    public void GetFileOwner_Returns_Owner()
    {
        _blackboard.ClaimFile("src/main.cs", "task-1");
        Assert.Equal("task-1", _blackboard.GetFileOwner("src/main.cs"));
    }

    [Fact]
    public void GetFileOwner_Returns_Null_When_Unclaimed()
    {
        Assert.Null(_blackboard.GetFileOwner("src/main.cs"));
    }

    [Fact]
    public void File_Can_Be_Reclaimed_After_Release()
    {
        _blackboard.ClaimFile("src/main.cs", "task-1");
        _blackboard.ReleaseFile("src/main.cs", "task-1");
        Assert.True(_blackboard.ClaimFile("src/main.cs", "task-2"));
        Assert.Equal("task-2", _blackboard.GetFileOwner("src/main.cs"));
    }

    [Fact]
    public void SetTaskOutput_And_GetTaskOutput()
    {
        _blackboard.SetTaskOutput("task-1", "output data");
        Assert.Equal("output data", _blackboard.GetTaskOutput("task-1"));
    }

    [Fact]
    public void GetTaskOutput_Returns_Null_When_Not_Set()
    {
        Assert.Null(_blackboard.GetTaskOutput("nonexistent"));
    }

    [Fact]
    public void SetTaskOutput_Overwrites_Previous()
    {
        _blackboard.SetTaskOutput("task-1", "first");
        _blackboard.SetTaskOutput("task-1", "second");
        Assert.Equal("second", _blackboard.GetTaskOutput("task-1"));
    }

    [Fact]
    public void Clear_Removes_All_Claims_And_Outputs()
    {
        _blackboard.ClaimFile("src/a.cs", "task-1");
        _blackboard.ClaimFile("src/b.cs", "task-2");
        _blackboard.SetTaskOutput("task-1", "data");

        _blackboard.Clear();

        Assert.Null(_blackboard.GetFileOwner("src/a.cs"));
        Assert.Null(_blackboard.GetFileOwner("src/b.cs"));
        Assert.Null(_blackboard.GetTaskOutput("task-1"));
    }

    [Fact]
    public void Concurrent_Claims_Only_One_Wins()
    {
        const int threadCount = 50;
        var results = new bool[threadCount];

        Parallel.For(0, threadCount, i =>
        {
            results[i] = _blackboard.ClaimFile("contested.cs", $"task-{i}");
        });

        // Exactly one thread should win
        Assert.Equal(1, results.Count(r => r));

        // The owner should be one of the tasks
        var owner = _blackboard.GetFileOwner("contested.cs");
        Assert.NotNull(owner);
        Assert.StartsWith("task-", owner);
    }

    [Fact]
    public void Multiple_Files_Independent_Claims()
    {
        Assert.True(_blackboard.ClaimFile("src/a.cs", "task-1"));
        Assert.True(_blackboard.ClaimFile("src/b.cs", "task-1"));
        Assert.True(_blackboard.ClaimFile("src/c.cs", "task-2"));

        Assert.Equal("task-1", _blackboard.GetFileOwner("src/a.cs"));
        Assert.Equal("task-1", _blackboard.GetFileOwner("src/b.cs"));
        Assert.Equal("task-2", _blackboard.GetFileOwner("src/c.cs"));
    }
}
