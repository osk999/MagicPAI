using System.Text.RegularExpressions;

namespace MagicPAI.Core.Services.Auth;

/// <summary>
/// Detects authentication and quota errors in Claude CLI output.
/// Patterns ported from MagicPrompt.Core/Services/Auth/AuthErrorDetector.cs.
/// </summary>
public static class AuthErrorDetector
{
    private static readonly Regex AuthErrorRegex = new(
        string.Join("|", new[]
        {
            @"API\s*Error:\s*401",
            @"authentication_error",
            @"OAuth\s+token\s+has\s+expired",
            @"token\s+expired",
            @"invalid_token",
            @"unauthorized",
            @"expired_token",
            @"invalid_grant",
            @"refresh_token.*expired",
            @"Your\s+session\s+has\s+expired",
            @"re-authenticate",
            @"Please\s+run.*login",
            @"Your\s+account\s+does\s+not\s+have\s+access\s+to\s+Claude",
            @"OAuth\s+token\s+revoked",
            @"You.ve\s+hit\s+your\s+limit",
            @"hit\s+your\s+limit.*resets",
        }),
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsAuthError(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        return AuthErrorRegex.IsMatch(output);
    }
}
