// MagicPAI.Server/Services/DockerEnforcementValidator.cs
// Startup-time configuration guard per temporal.md §9.2. If the server is
// asked to run without Docker (or Kubernetes) worker containers, we refuse
// to start. All AI/CLI activities run inside per-session containers.
using MagicPAI.Core.Config;

namespace MagicPAI.Server.Services;

public interface IStartupValidator
{
    void Validate();
}

public class DockerEnforcementValidator : IStartupValidator
{
    private readonly MagicPaiConfig _config;
    private readonly ILogger<DockerEnforcementValidator> _log;

    public DockerEnforcementValidator(
        MagicPaiConfig config,
        ILogger<DockerEnforcementValidator> log)
    {
        _config = config;
        _log = log;
    }

    public void Validate()
    {
        var backend = _config.ExecutionBackend ?? "";
        var backendOk =
            backend.Equals("docker", StringComparison.OrdinalIgnoreCase) ||
            backend.Equals("kubernetes", StringComparison.OrdinalIgnoreCase);

        if (!backendOk)
        {
            throw new InvalidOperationException(
                "MagicPAI requires a containerized execution backend. " +
                "Set MagicPAI:ExecutionBackend=docker or kubernetes in appsettings.json " +
                "or MagicPAI__ExecutionBackend=docker env var.");
        }

        if (!_config.UseWorkerContainers)
        {
            throw new InvalidOperationException(
                "MagicPAI:UseWorkerContainers evaluated to false. " +
                "Enable Docker (UseDocker=true) or Kubernetes (ExecutionBackend=kubernetes). " +
                "Local mode is unsupported.");
        }

        _log.LogInformation(
            "Docker enforcement validated. Backend={Backend}, UseWorkerContainers={Use}",
            _config.ExecutionBackend, _config.UseWorkerContainers);
    }
}
