using MagicPAI.Core.Config;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Services;

public class GuiPortAllocatorTests
{
    [Fact]
    public void Reserve_ReturnsSamePort_ForSameOwner()
    {
        var allocator = CreateAllocator(6150, 6152);

        var first = allocator.Reserve("session-1");
        var second = allocator.Reserve("session-1");

        Assert.Equal(first, second);
        Assert.Equal(first, allocator.GetReservedPort("session-1"));
    }

    [Fact]
    public void Reserve_ReturnsDifferentPorts_ForDifferentOwners()
    {
        var allocator = CreateAllocator(6153, 6155);

        var first = allocator.Reserve("session-1");
        var second = allocator.Reserve("session-2");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Release_MakesPortAvailableAgain()
    {
        var allocator = CreateAllocator(6156, 6156);

        var first = allocator.Reserve("session-1");
        allocator.Release("session-1");
        var second = allocator.Reserve("session-2");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Reserve_Throws_WhenRangeIsExhausted()
    {
        var allocator = CreateAllocator(6157, 6157);
        allocator.Reserve("session-1");

        var ex = Assert.Throws<InvalidOperationException>(() => allocator.Reserve("session-2"));
        Assert.Contains("No free GUI port", ex.Message);
    }

    private static GuiPortAllocator CreateAllocator(int start, int end) =>
        new(new MagicPaiConfig
        {
            GuiPortRangeStart = start,
            GuiPortRangeEnd = end
        });
}
