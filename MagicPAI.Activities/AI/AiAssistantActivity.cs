using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents",
    "Execute a prompt via a generic AI assistant (Claude, Codex, Gemini) in a Docker container")]
[FlowNode("Done", "Failed")]
public class AiAssistantActivity : RunCliAgentActivity
{
}
