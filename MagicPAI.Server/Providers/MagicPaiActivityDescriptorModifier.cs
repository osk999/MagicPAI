using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace MagicPAI.Server.Providers;

/// <summary>
/// Customizes how MagicPAI activities appear in the Elsa Studio designer
/// by setting icons and colors based on activity category.
/// </summary>
public class MagicPaiActivityDescriptorModifier : IActivityDescriptorModifier
{
    private static readonly Dictionary<string, (string Color, string Icon)> CategoryStyles = new()
    {
        ["AI"] = ("#2196F3", "brain"),
        ["Docker"] = ("#00BCD4", "cube"),
        ["Verification"] = ("#4CAF50", "shield-check"),
        ["Git"] = ("#FF9800", "source-branch"),
        ["Infrastructure"] = ("#9E9E9E", "cog"),
    };

    private const string DefaultColor = "#7E57C2";
    private const string DefaultIcon = "puzzle";

    public void Modify(ActivityDescriptor descriptor)
    {
        if (!IsMagicPaiActivity(descriptor))
            return;

        var (color, icon) = ResolveCategoryStyle(descriptor.Category ?? "");

        descriptor.CustomProperties["Color"] = color;
        descriptor.CustomProperties["Icon"] = icon;
    }

    private static bool IsMagicPaiActivity(ActivityDescriptor descriptor)
    {
        return descriptor.Namespace?.StartsWith("MagicPAI", StringComparison.OrdinalIgnoreCase) == true
            || descriptor.Category?.StartsWith("MagicPAI", StringComparison.OrdinalIgnoreCase) == true
            || descriptor.TypeName?.StartsWith("MagicPAI", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static (string Color, string Icon) ResolveCategoryStyle(string category)
    {
        foreach (var (key, style) in CategoryStyles)
        {
            if (category.Contains(key, StringComparison.OrdinalIgnoreCase))
                return style;
        }

        return (DefaultColor, DefaultIcon);
    }
}
