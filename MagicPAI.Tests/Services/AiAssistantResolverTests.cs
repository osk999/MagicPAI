using MagicPAI.Core.Config;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Services;

public class AiAssistantResolverTests
{
    [Theory]
    [InlineData(null, "claude")]
    [InlineData("", "claude")]
    [InlineData("1", "claude")]
    [InlineData("2", "codex")]
    [InlineData("3", "gemini")]
    [InlineData("openai", "codex")]
    public void NormalizeAssistant_Maps_Aliases(string? input, string expected)
    {
        Assert.Equal(expected, AiAssistantResolver.NormalizeAssistant(input, "claude"));
    }

    [Theory]
    [InlineData(1, "opus")]
    [InlineData(2, "sonnet")]
    [InlineData(3, "haiku")]
    public void ResolveModelForPower_Uses_Claude_Default_Map(int modelPower, string expected)
    {
        var config = new MagicPaiConfig();
        var runner = new ClaudeRunner();

        Assert.Equal(expected, AiAssistantResolver.ResolveModelForPower(runner, config, modelPower));
    }

    [Fact]
    public void Resolve_Rejects_Invalid_Explicit_Model()
    {
        var config = new MagicPaiConfig();
        var runner = new CodexRunner();

        Assert.Throws<InvalidOperationException>(() =>
            AiAssistantResolver.Resolve(runner, config, "codex", "sonnet", null));
    }

    [Fact]
    public void Resolve_Uses_ModelPower_When_Model_Is_Auto()
    {
        var config = new MagicPaiConfig();
        var runner = new CodexRunner();

        var resolved = AiAssistantResolver.Resolve(runner, config, "codex", "auto", 2);

        Assert.Equal("gpt-5.3-codex", resolved.Model);
        Assert.Equal(2, resolved.ModelPower);
    }
}
