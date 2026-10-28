using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Crown.Infrastructure.Backups;

public sealed class NetworkInfoReader
{
    private readonly IConfiguration _configuration;

    public NetworkInfoReader(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public NetworkInfoSnapshot Read()
    {
        var hostname = Environment.MachineName;
        var publicHostname = _configuration["System:PublicHostname"]
            ?? _configuration["APP_HOSTNAME"]
            ?? "crown.local";

        var advertised = (_configuration["System:AdvertiseAddresses"] ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var interfaceAddresses = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up
                || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var uni in ni.GetIPProperties().UnicastAddresses)
            {
                if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    interfaceAddresses.Add(uni.Address.ToString());
                }
            }
        }

        interfaceAddresses = interfaceAddresses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        try
        {
            var resolved = Dns.GetHostAddresses(publicHostname)
                .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.ToString())
                .Where(x => !advertised.Contains(x, StringComparer.OrdinalIgnoreCase))
                .ToList();
            advertised.AddRange(resolved);
        }
        catch
        {
            // Hostname may be unknown inside the container.
        }

        return new NetworkInfoSnapshot
        {
            MachineName = hostname,
            PublicHostname = publicHostname,
            AdvertiseAddresses = advertised.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            InterfaceAddresses = interfaceAddresses
        };
    }
}

public sealed class NetworkInfoSnapshot
{
    public string MachineName { get; init; } = string.Empty;

    public string PublicHostname { get; init; } = string.Empty;

    public List<string> AdvertiseAddresses { get; init; } = [];

    public List<string> InterfaceAddresses { get; init; } = [];
}
