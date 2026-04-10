using System.Net;
using System.Net.Sockets;
using MagicPAI.Core.Config;

namespace MagicPAI.Core.Services;

public class GuiPortAllocator : IGuiPortAllocator
{
    private readonly MagicPaiConfig _config;
    private readonly object _sync = new();
    private readonly Dictionary<string, int> _ownerToPort = new(StringComparer.Ordinal);
    private readonly HashSet<int> _reservedPorts = [];

    public GuiPortAllocator(MagicPaiConfig config)
    {
        _config = config;
    }

    public int Reserve(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Owner ID is required.", nameof(ownerId));

        lock (_sync)
        {
            if (_ownerToPort.TryGetValue(ownerId, out var existingPort))
                return existingPort;

            for (var port = _config.GuiPortRangeStart; port <= _config.GuiPortRangeEnd; port++)
            {
                if (_reservedPorts.Contains(port))
                    continue;

                if (!IsPortAvailable(port))
                    continue;

                _reservedPorts.Add(port);
                _ownerToPort[ownerId] = port;
                return port;
            }
        }

        throw new InvalidOperationException(
            $"No free GUI port is available in the configured range {_config.GuiPortRangeStart}-{_config.GuiPortRangeEnd}.");
    }

    public void Release(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return;

        lock (_sync)
        {
            if (!_ownerToPort.Remove(ownerId, out var port))
                return;

            _reservedPorts.Remove(port);
        }
    }

    public int? GetReservedPort(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return null;

        lock (_sync)
            return _ownerToPort.TryGetValue(ownerId, out var port) ? port : null;
    }

    private static bool IsPortAvailable(int port)
    {
        TcpListener? listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Loopback, port)
            {
                ExclusiveAddressUse = true
            };
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
