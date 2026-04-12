using MagicPAI.Core.Config;

namespace MagicPAI.Core.Services;

public sealed record ResolvedAssistantOptions(
    string Assistant,
    string? Model,
    int? ModelPower);

public static class AiAssistantResolver
{
    private static readonly Dictionary<string, string> AssistantAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = "claude",
            ["claude"] = "claude",
            ["anthropic"] = "claude",
            ["2"] = "codex",
            ["codex"] = "codex",
            ["openai"] = "codex",
            ["3"] = "gemini",
            ["gemini"] = "gemini",
            ["google"] = "gemini"
        };

    private static readonly Dictionary<string, Dictionary<int, string>> DefaultModelPowerMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude"] = new()
            {
                [1] = "opus",
                [2] = "sonnet",
                [3] = "haiku"
            },
            ["codex"] = new()
            {
                [1] = "gpt-5.4",
                [2] = "gpt-5.3-codex",
                [3] = "gpt-5.4-mini"
            },
            ["gemini"] = new()
            {
                [1] = "gemini-3.1-pro-preview",
                [2] = "gemini-3-flash",
                [3] = "gemini-3.1-flash-lite-preview"
            }
        };

    public static string NormalizeAssistant(string? assistant, string? defaultAssistant = null)
    {
        var candidate = string.IsNullOrWhiteSpace(assistant) ? defaultAssistant : assistant;
        if (string.IsNullOrWhiteSpace(candidate))
            return "claude";

        candidate = candidate.Trim();
        return AssistantAliases.TryGetValue(candidate, out var canonical)
            ? canonical
            : candidate.ToLowerInvariant();
    }

    public static int? NormalizeModelPower(int? modelPower)
    {
        if (!modelPower.HasValue || modelPower.Value <= 0)
            return null;
        if (modelPower is < 1 or > 3)
            throw new InvalidOperationException("ModelPower must be 1 (strong), 2 (balanced), or 3 (fast).");
        return modelPower.Value;
    }

    public static int GetRecommendedModelPower(int complexity) => complexity switch
    {
        <= 3 => 2,  // sonnet-class minimum — no haiku-tier models
        <= 7 => 2,
        _ => 1
    };

    public static ResolvedAssistantOptions Resolve(
        ICliAgentRunner runner,
        MagicPaiConfig config,
        string? assistant,
        string? explicitModel,
        int? modelPower)
    {
        var normalizedAssistant = NormalizeAssistant(assistant, config.DefaultAgent);
        var normalizedPower = NormalizeModelPower(modelPower);
        var model = ResolveModel(runner, config, explicitModel, normalizedPower);
        return new ResolvedAssistantOptions(normalizedAssistant, model, normalizedPower);
    }

    public static string ResolveModelForPower(
        ICliAgentRunner runner,
        MagicPaiConfig config,
        int modelPower)
    {
        var normalizedPower = NormalizeModelPower(modelPower)
            ?? throw new InvalidOperationException("ModelPower is required.");
        return ResolveModel(runner, config, null, normalizedPower)
            ?? runner.DefaultModel;
    }

    public static IReadOnlyDictionary<int, string> GetModelPowerMap(
        MagicPaiConfig config,
        string assistant)
    {
        var normalizedAssistant = NormalizeAssistant(assistant, config.DefaultAgent);
        if (!DefaultModelPowerMap.TryGetValue(normalizedAssistant, out var defaults))
            throw new InvalidOperationException($"No model-power map exists for assistant '{normalizedAssistant}'.");

        var map = new Dictionary<int, string>(defaults);
        if (!config.AssistantModelPowerMap.TryGetValue(normalizedAssistant, out var overrides))
            return map;

        foreach (var (powerKey, model) in overrides)
        {
            if (int.TryParse(powerKey, out var power) && power is >= 1 and <= 3 && !string.IsNullOrWhiteSpace(model))
                map[power] = model.Trim();
        }

        return map;
    }

    public static IReadOnlyList<string> ValidateConfiguredPowerMaps(
        Dictionary<string, Dictionary<string, string>> configuredMaps)
    {
        var errors = new List<string>();

        foreach (var (assistantKey, powerMap) in configuredMaps)
        {
            var normalizedAssistant = NormalizeAssistant(assistantKey);
            if (!DefaultModelPowerMap.ContainsKey(normalizedAssistant))
            {
                errors.Add($"AssistantModelPowerMap contains unsupported assistant '{assistantKey}'.");
                continue;
            }

            foreach (var (powerKey, model) in powerMap)
            {
                if (!int.TryParse(powerKey, out var power) || power is < 1 or > 3)
                    errors.Add($"AssistantModelPowerMap[{assistantKey}] key '{powerKey}' must be 1, 2, or 3.");
                if (string.IsNullOrWhiteSpace(model))
                    errors.Add($"AssistantModelPowerMap[{assistantKey}][{powerKey}] must not be empty.");
            }
        }

        return errors;
    }

    private static string? ResolveModel(
        ICliAgentRunner runner,
        MagicPaiConfig config,
        string? explicitModel,
        int? modelPower)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel) &&
            !string.Equals(explicitModel, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var requestedModel = explicitModel.Trim();
            if (runner.AvailableModels.Contains(requestedModel, StringComparer.OrdinalIgnoreCase))
                return requestedModel;

            throw new InvalidOperationException(
                $"Assistant '{runner.AgentName}' does not support model '{requestedModel}'.");
        }

        if (!modelPower.HasValue)
            return null;

        var powerMap = GetModelPowerMap(config, runner.AgentName);
        if (!powerMap.TryGetValue(modelPower.Value, out var mappedModel))
            throw new InvalidOperationException(
                $"Assistant '{runner.AgentName}' is missing a model mapping for ModelPower={modelPower.Value}.");

        return mappedModel;
    }
}
