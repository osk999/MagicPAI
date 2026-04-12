using Elsa.Studio.Contracts;
using Elsa.Studio.Models;
using Microsoft.AspNetCore.Components.Routing;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Provides custom MagicPAI menu items to the Elsa Studio sidebar.
/// </summary>
public class MagicPaiMenuProvider : IMenuProvider
{
    public ValueTask<IEnumerable<MenuItem>> GetMenuItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<MenuItem>
        {
            new()
            {
                Href = "magic/dashboard",
                Text = "Dashboard",
                Icon = "fa-solid fa-gauge-high",
                Order = -10,
                GroupName = "magicpai"
            },
            new()
            {
                Href = "magic/costs",
                Text = "Cost Analytics",
                Icon = "fa-solid fa-dollar-sign",
                Order = -8,
                GroupName = "magicpai"
            },
            new()
            {
                Href = "magic/settings",
                Text = "Settings",
                Icon = "fa-solid fa-gear",
                Order = -7,
                GroupName = "magicpai"
            },
            new()
            {
                Href = "workflows/definitions",
                Text = "Workflows",
                Icon = "fa-solid fa-diagram-project",
                Order = -5,
                GroupName = "elsa-studio"
            },
            new()
            {
                Href = "workflows/instances",
                Text = "Instances",
                Icon = "fa-solid fa-list-check",
                Order = -4,
                GroupName = "elsa-studio"
            },
            new()
            {
                Href = "magic/elsa-studio",
                Text = "API Explorer",
                Icon = "fa-solid fa-terminal",
                Order = -3,
                GroupName = "elsa-studio"
            }
        };

        return new ValueTask<IEnumerable<MenuItem>>(items);
    }
}
