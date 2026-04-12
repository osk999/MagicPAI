using Elsa.Studio.Contracts;
using Elsa.Studio.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Registers custom menu groups for the MagicPAI sidebar navigation.
/// Elsa Shell only renders menu items whose GroupName matches a registered group.
/// </summary>
public class MagicPaiMenuGroupProvider : IMenuGroupProvider
{
    public ValueTask<IEnumerable<MenuItemGroup>> GetMenuGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = new List<MenuItemGroup>
        {
            new("magicpai", "MagicPAI", -10f),
            new("elsa-studio", "Elsa Studio", -5f)
        };

        return new(groups);
    }
}
