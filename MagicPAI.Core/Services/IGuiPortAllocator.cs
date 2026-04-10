namespace MagicPAI.Core.Services;

public interface IGuiPortAllocator
{
    int Reserve(string ownerId);
    void Release(string ownerId);
    int? GetReservedPort(string ownerId);
}
