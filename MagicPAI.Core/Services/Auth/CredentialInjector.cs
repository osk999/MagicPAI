using System.Diagnostics;

namespace MagicPAI.Core.Services.Auth;

/// <summary>
/// Injects fresh credential JSON into a running Docker container via docker cp.
/// Ported from MagicPrompt's DockerTabEnvironment.RefreshCredentialsAsync.
/// </summary>
public static class CredentialInjector
{
    /// <summary>
    /// Write updated credentials into the container's /home/worker/.claude/.credentials.json.
    /// </summary>
    public static async Task InjectAsync(string containerId, string credentialsJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerId) || string.IsNullOrWhiteSpace(credentialsJson))
            return;

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, credentialsJson, ct);

            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsWindows())
            {
                psi.Environment["MSYS_NO_PATHCONV"] = "1";
                psi.Environment["MSYS2_ARG_CONV_EXCL"] = "*";
            }

            psi.ArgumentList.Add("cp");
            psi.ArgumentList.Add(tempFile);
            psi.ArgumentList.Add($"{containerId}:/home/worker/.claude/.credentials.json");

            using var process = Process.Start(psi);
            if (process is null) return;
            await process.WaitForExitAsync(ct);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }
}
