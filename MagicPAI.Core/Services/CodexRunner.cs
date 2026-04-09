using System.Text;
using System.Text.Json;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class CodexRunner : ICliAgentRunner
{
    private const string OutputStartMarker = "__MAGICPAI_CODEX_LAST_MESSAGE_START__";
    private const string OutputEndMarker = "__MAGICPAI_CODEX_LAST_MESSAGE_END__";

    public string AgentName => "codex";
    public string DefaultModel => "gpt-5.4";
    public string[] AvailableModels =>
    [
        "gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex", "codex-mini-latest", "o3", "o4-mini", "gpt-4.1",
        "gpt5", "gpt5-mini", "codex", "codex-mini"
    ];
    public bool SupportsNativeSchema => true;

    public string BuildCommand(AgentRequest request)
    {
        var prompt = BuildPrompt(request);
        var model = ResolveModel(request.Model ?? DefaultModel);
        var workDir = request.WorkDir ?? "/workspace";
        var outputFile = $"/tmp/codex-last-message-{Guid.NewGuid():N}.txt";
        var schemaFile = $"/tmp/codex-schema-{Guid.NewGuid():N}.json";
        var isResume = !string.IsNullOrWhiteSpace(request.SessionId);

        var command = new StringBuilder();
        command.Append($"cd '{Escape(workDir)}' && ");

        if (!isResume && !string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            command.Append($"python3 -c 'import base64, pathlib, sys; pathlib.Path(sys.argv[1]).write_bytes(base64.b64decode(sys.argv[2]))' '{schemaFile}' '{Convert.ToBase64String(Encoding.UTF8.GetBytes(request.OutputSchema))}' && ");
        }

        command.Append("codex exec");
        if (isResume)
            command.Append(" resume");
        command.Append(" --skip-git-repo-check");
        if (isResume)
            command.Append(" --dangerously-bypass-approvals-and-sandbox");
        else
            command.Append(" --sandbox danger-full-access -c 'ask_for_approval=\"never\"' --color never");
        command.Append(" --json");
        command.Append($" -m {model}");

        if (!isResume && !string.IsNullOrWhiteSpace(request.OutputSchema))
            command.Append($" --output-schema '{schemaFile}'");

        command.Append($" -o '{outputFile}'");
        if (isResume)
            command.Append($" '{Escape(request.SessionId!)}'");
        command.Append($" '{Escape(prompt)}'");
        command.Append($" ; status=$?");
        command.Append($" ; printf '\\n{OutputStartMarker}\\n'");
        command.Append($" ; cat '{outputFile}' 2>/dev/null || true");
        command.Append($" ; printf '\\n{OutputEndMarker}\\n'");
        command.Append(" ; exit $status");

        return command.ToString();
    }

    public CliAgentExecutionPlan BuildExecutionPlan(AgentRequest request)
    {
        var model = ResolveModel(request.Model ?? DefaultModel);
        var workDir = request.WorkDir ?? "/workspace";
        var outputFile = $"/tmp/codex-last-message-{Guid.NewGuid():N}.txt";
        var schemaFile = $"/tmp/codex-schema-{Guid.NewGuid():N}.json";
        var isResume = !string.IsNullOrWhiteSpace(request.SessionId);
        var prompt = BuildPrompt(request);

        var arguments = new List<string> { "exec" };
        if (isResume)
            arguments.Add("resume");

        arguments.Add("--skip-git-repo-check");
        if (isResume)
        {
            arguments.Add("--dangerously-bypass-approvals-and-sandbox");
        }
        else
        {
            arguments.Add("--sandbox");
            arguments.Add("danger-full-access");
            arguments.Add("-c");
            arguments.Add("ask_for_approval=\"never\"");
            arguments.Add("--color");
            arguments.Add("never");
        }
        arguments.Add("--json");
        arguments.Add("-m");
        arguments.Add(model);

        if (!isResume && !string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            arguments.Add("--output-schema");
            arguments.Add(schemaFile);
        }

        arguments.Add("-o");
        arguments.Add(outputFile);

        if (isResume)
            arguments.Add(request.SessionId!);

        arguments.Add(prompt);

        var setupRequests = new List<ContainerExecRequest>();
        if (!isResume && !string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            setupRequests.Add(new ContainerExecRequest(
                FileName: "python3",
                Arguments:
                [
                    "-c",
                    "import base64, pathlib, sys; pathlib.Path(sys.argv[1]).write_bytes(base64.b64decode(sys.argv[2]))",
                    schemaFile,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(request.OutputSchema))
                ],
                WorkingDirectory: workDir));
        }

        return new CliAgentExecutionPlan(
            MainRequest: new ContainerExecRequest(
                FileName: "codex",
                Arguments: arguments,
                WorkingDirectory: workDir),
            SetupRequests: setupRequests);
    }

    public CliAgentResponse ParseResponse(string rawOutput)
    {
        string? sessionId = null;
        string? output = ExtractLastMessage(rawOutput);
        string? errorMessage = null;
        var success = true;
        var inputTokens = 0;
        var outputTokens = 0;

        foreach (var line in rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            JsonElement json;
            try
            {
                json = JsonDocument.Parse(line).RootElement.Clone();
            }
            catch
            {
                continue;
            }

            if (json.ValueKind != JsonValueKind.Object)
                continue;

            if (!json.TryGetProperty("type", out var typeProp))
                continue;

            switch (typeProp.GetString())
            {
                case "thread.started":
                    if (json.TryGetProperty("thread_id", out var threadId))
                        sessionId = threadId.GetString();
                    break;
                case "item.completed":
                    if (json.TryGetProperty("item", out var item) &&
                        item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("type", out var itemType) &&
                        itemType.GetString() == "agent_message" &&
                        item.TryGetProperty("text", out var text))
                    {
                        output = text.GetString() ?? output;
                    }
                    break;
                case "turn.completed":
                    if (json.TryGetProperty("usage", out var usage) &&
                        usage.ValueKind == JsonValueKind.Object)
                    {
                        inputTokens = TryGetInt32(usage, "input_tokens");
                        outputTokens = TryGetInt32(usage, "output_tokens");
                    }
                    break;
                case "error":
                    success = false;
                    errorMessage ??= json.TryGetProperty("message", out var message)
                        ? message.GetString()
                        : "Codex reported an error.";
                    break;
                case "turn.failed":
                    success = false;
                    if (json.TryGetProperty("error", out var error) &&
                        error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("message", out var failedMessage))
                    {
                        errorMessage ??= failedMessage.GetString();
                    }
                    break;
            }
        }

        output ??= rawOutput.Trim();
        if (!success && !string.IsNullOrWhiteSpace(errorMessage))
            output = errorMessage;

        return new CliAgentResponse(
            Success: success,
            Output: output,
            CostUsd: 0,
            FilesModified: [],
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            SessionId: sessionId,
            StructuredOutputJson: TryExtractJson(output));
    }

    private static string ResolveModel(string alias) => alias switch
    {
        "gpt5" => "gpt-5.4",
        "gpt5-mini" => "gpt-5.4-mini",
        "codex" => "gpt-5.3-codex",
        "codex-mini" => "codex-mini-latest",
        _ => alias
    };

    private static string Escape(string value) => value.Replace("'", "'\\''");

    private static string BuildPrompt(AgentRequest request)
    {
        var prompt = request.Prompt ?? "";
        if (!string.IsNullOrWhiteSpace(request.SessionId) && !string.IsNullOrWhiteSpace(request.OutputSchema))
            return $"Respond with ONLY valid JSON matching this schema (no markdown, no explanation):\n{request.OutputSchema}\n\nTask: {prompt}";

        return prompt;
    }

    private static string? ExtractLastMessage(string rawOutput)
    {
        var start = rawOutput.IndexOf(OutputStartMarker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += OutputStartMarker.Length;
        var end = rawOutput.IndexOf(OutputEndMarker, start, StringComparison.Ordinal);
        if (end < 0)
            return null;

        return rawOutput[start..end].Trim();
    }

    private static int TryGetInt32(JsonElement json, string propertyName)
    {
        return json.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static string? TryExtractJson(string output)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0)
            return null;
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch
        {
            return null;
        }
    }
}
