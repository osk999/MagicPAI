namespace MagicPAI.Core.Models;

public record PromptEnhancementResult(
    string EnhancedPrompt,
    bool WasEnhanced,
    string Rationale);
