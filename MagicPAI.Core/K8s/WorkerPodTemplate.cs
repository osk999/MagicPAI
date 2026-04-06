using k8s.Models;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.K8s;

/// <summary>
/// Generates a Kubernetes V1Pod spec from ContainerConfig for worker pods.
/// </summary>
public static class WorkerPodTemplate
{
    /// <summary>
    /// Create a V1Pod specification for a MagicPAI worker.
    /// </summary>
    /// <param name="config">Container configuration.</param>
    /// <param name="instanceId">Unique workflow instance identifier (typically a GUID).</param>
    /// <param name="namespace">Kubernetes namespace to deploy into.</param>
    public static V1Pod Create(ContainerConfig config, string instanceId, string @namespace = "magicpai")
    {
        var shortId = instanceId.Length >= 8 ? instanceId[..8] : instanceId;
        var podName = $"magicpai-worker-{shortId}";

        var envVars = config.Env
            .Select(kv => new V1EnvVar(kv.Key, kv.Value))
            .ToList();

        var resources = new V1ResourceRequirements
        {
            Requests = new Dictionary<string, ResourceQuantity>
            {
                ["memory"] = new ResourceQuantity($"{config.MemoryLimitMb}Mi"),
                ["cpu"] = new ResourceQuantity($"{config.CpuCount}")
            },
            Limits = new Dictionary<string, ResourceQuantity>
            {
                ["memory"] = new ResourceQuantity($"{config.MemoryLimitMb}Mi"),
                ["cpu"] = new ResourceQuantity($"{config.CpuCount}")
            }
        };

        var volumeMounts = new List<V1VolumeMount>
        {
            new()
            {
                Name = "workspace",
                MountPath = config.ContainerWorkDir
            }
        };

        var containerPorts = new List<V1ContainerPort>();
        if (config.EnableGui)
        {
            containerPorts.Add(new V1ContainerPort
            {
                ContainerPort = 7900,
                Name = "novnc",
                Protocol = "TCP"
            });
        }

        var container = new V1Container
        {
            Name = "worker",
            Image = config.Image,
            Command = ["sleep", "infinity"],
            Resources = resources,
            Env = envVars,
            VolumeMounts = volumeMounts,
            Ports = containerPorts.Count > 0 ? containerPorts : null,
            WorkingDir = config.ContainerWorkDir
        };

        // Use emptyDir for ephemeral workspaces; production may override with PVC
        var volumes = new List<V1Volume>
        {
            new()
            {
                Name = "workspace",
                EmptyDir = new V1EmptyDirVolumeSource()
            }
        };

        var pod = new V1Pod
        {
            ApiVersion = "v1",
            Kind = "Pod",
            Metadata = new V1ObjectMeta
            {
                Name = podName,
                NamespaceProperty = @namespace,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "magicpai-worker",
                    ["workflow-instance"] = instanceId
                },
                Annotations = new Dictionary<string, string>
                {
                    ["magicpai/created-at"] = DateTime.UtcNow.ToString("o"),
                    ["magicpai/timeout-minutes"] = config.Timeout.TotalMinutes.ToString("F0")
                }
            },
            Spec = new V1PodSpec
            {
                Containers = [container],
                Volumes = volumes,
                RestartPolicy = "Never"
            }
        };

        return pod;
    }
}
