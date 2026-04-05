using MagicPAI.Activities.Git;

namespace MagicPAI.Tests.Services;

public class BranchSanitizationTests
{
    [Fact]
    public void SafeBranchName_PassesThrough()
    {
        var result = CreateWorktreeActivity.SanitizeBranchName("feature/my-branch");
        Assert.Equal("feature/my-branch", result);
    }

    [Fact]
    public void BranchWithShellChars_AreSanitized()
    {
        var result = CreateWorktreeActivity.SanitizeBranchName("branch; rm -rf /");
        Assert.Equal("branchrm-rf/", result);
        Assert.DoesNotContain(";", result);
        Assert.DoesNotContain(" ", result);
    }

    [Fact]
    public void BranchWithBackticks_AreSanitized()
    {
        var result = CreateWorktreeActivity.SanitizeBranchName("branch`whoami`");
        Assert.DoesNotContain("`", result);
    }

    [Fact]
    public void BranchWithDollarSign_IsSanitized()
    {
        var result = CreateWorktreeActivity.SanitizeBranchName("branch$(evil)");
        Assert.DoesNotContain("$", result);
        Assert.DoesNotContain("(", result);
    }

    [Fact]
    public void EmptyBranch_ReturnsEmpty()
    {
        var result = CreateWorktreeActivity.SanitizeBranchName("");
        Assert.Equal("", result);
    }

    [Fact]
    public void BranchWithDotsAndUnderscores_Allowed()
    {
        var result = CreateWorktreeActivity.SanitizeBranchName("release_v2.1.0");
        Assert.Equal("release_v2.1.0", result);
    }
}
