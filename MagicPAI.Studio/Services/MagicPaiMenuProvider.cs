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
                GroupName = "MagicPAI"
            },
            new()
            {
                Href = "magic/sessions",
                Text = "Sessions",
                Icon = "fa-solid fa-play-circle",
                Order = -9,
                GroupName = "MagicPAI"
            },
            new()
            {
                Href = "magic/costs",
                Text = "Cost Analytics",
                Icon = "fa-solid fa-dollar-sign",
                Order = -8,
                GroupName = "MagicPAI"
            },
            new()
            {
                Href = "magic/settings",
                Text = "Settings",
                Icon = "fa-solid fa-gear",
                Order = -7,
                GroupName = "MagicPAI"
            }
        };

        return new ValueTask<IEnumerable<MenuItem>>(items);
    }
}
