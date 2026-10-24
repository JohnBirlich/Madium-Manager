using System.Net;
using System.Net.Sockets;

namespace MadiumManager.Domain.Servers;

/// <summary>
/// A validated server endpoint. Host names are deliberately resolved by providers,
/// not by the reconnect command builder.
/// </summary>
public sealed record ServerEndpoint
{
    public ServerEndpoint(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);

        Address = address;
        Port = port;
    }

    public IPAddress Address { get; }

    public int Port { get; }

    public override string ToString() => Address.AddressFamily == AddressFamily.InterNetworkV6
        ? $"[{Address}]:{Port}"
        : $"{Address}:{Port}";
}
