using System.Globalization;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MagicPAI.Tests.Services;

public class LocalContainerManagerTests
{
    [Fact]
    public async Task ExecStreamingAsync_Times_Out_When_No_Output_Is_Produced()
    {
        var manager = new LocalContainerManager(NullLogger<LocalContainerManager>.Instance);

        var result = await manager.ExecStreamingAsync(
            "local-test",
            CreateSilentCommand(millisecondsBeforeOutput: 1500),
            _ => { },
            TimeSpan.FromMilliseconds(400),
            CancellationToken.None);

        Assert.Equal(124, result.ExitCode);
        Assert.Contains("inactivity timeout", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecStreamingAsync_Allows_Periodic_Output_Within_Idle_Window()
    {
        var manager = new LocalContainerManager(NullLogger<LocalContainerManager>.Instance);
        var chunks = new List<string>();

        var result = await manager.ExecStreamingAsync(
            "local-test",
            CreatePeriodicOutputCommand(),
            chunk => chunks.Add(chunk),
            TimeSpan.FromMilliseconds(800),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(chunks);
        Assert.Contains("4", result.Output);
    }

    private static string CreateSilentCommand(int millisecondsBeforeOutput) =>
        OperatingSystem.IsWindows()
            ? $"powershell -NoProfile -Command \"Start-Sleep -Milliseconds {millisecondsBeforeOutput}; Write-Output done\""
            : $"sleep {FormatSeconds(millisecondsBeforeOutput)}; echo done";

    private static string CreatePeriodicOutputCommand() =>
        OperatingSystem.IsWindows()
            ? "powershell -NoProfile -Command \"1..4 | ForEach-Object { Write-Output $_; Start-Sleep -Milliseconds 200 }\""
            : "for i in 1 2 3 4; do echo $i; sleep 0.2; done";

    private static string FormatSeconds(int milliseconds) =>
        (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
}
