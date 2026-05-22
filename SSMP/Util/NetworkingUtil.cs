using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SSMP.Logging;

namespace SSMP.Util;

/// <summary>
/// Shared IPv4 networking helpers used across transport and matchmaking code.
/// </summary>
internal static class NetworkingUtil {
    #region Public API

    /// <summary>
    /// Normalizes <paramref name="value"/> to a dotted-decimal IPv4 string, logging a warning and
    /// returning <see langword="null"/> when the value is present but invalid.
    /// Returns <see langword="null"/> silently when <paramref name="value"/> is null or whitespace.
    /// </summary>
    /// <param name="value">The raw IP string from configuration.</param>
    /// <param name="owner">Display name of the owning component, used in log messages.</param>
    /// <param name="settingName">Configuration key name, used in log messages.</param>
    public static string? NormalizeConfiguredIpv4(string? value, string owner, string settingName) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (TryNormalizeIpv4(value, out var normalized))
            return normalized;

        Logger.Warn($"{owner}: Ignoring {settingName} value '{value}' because it is not a valid IPv4 address.");
        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is a private IPv4 address:
    /// <c>10.x.x.x/8</c>, <c>172.16.x.x/12</c>, or <c>192.168.x.x/16</c>.
    /// </summary>
    public static bool IsPrivateIpv4(string? value) {
        if (!TryNormalizeIpv4(value, out var normalized) ||
            !IPAddress.TryParse(normalized, out var ip))
            return false;

        var b = ip.GetAddressBytes();
        return b[0] switch {
            10 => true,
            172 => b[1] is >= 16 and <= 31,
            192 => b[1] == 168,
            _ => false
        };
    }

    /// <summary>
    /// Resolves the local bind address for gameplay traffic from <paramref name="bindIpAddress"/>,
    /// falling back to <see cref="IPAddress.Any"/> when the value is absent, invalid, or does not
    /// belong to a local network interface.
    /// </summary>
    public static IPAddress ResolveBindAddress(string? bindIpAddress) {
        if (string.IsNullOrWhiteSpace(bindIpAddress))
            return IPAddress.Any;

        if (IPAddress.TryParse(bindIpAddress, out var address) &&
            address.AddressFamily == AddressFamily.InterNetwork &&
            IsLocalInterfaceAddress(address)) {
            Logger.Info($"ConnectInterface: Binding matchmaking socket to LocalBindIp {address}.");
            return address;
        }

        Logger.Warn(
            $"ConnectInterface: Ignoring LocalBindIp '{bindIpAddress}' because it is not a valid " +
            "local IPv4 address. Falling back to IPAddress.Any."
        );

        return IPAddress.Any;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Attempts to parse <paramref name="value"/> as an IPv4 address, writing the canonical
    /// dotted-decimal form to <paramref name="normalized"/> on success.
    /// </summary>
    private static bool TryNormalizeIpv4(string? value, out string normalized) {
        if (!string.IsNullOrWhiteSpace(value) &&
            IPAddress.TryParse(value, out var ip) &&
            ip.AddressFamily == AddressFamily.InterNetwork) {
            normalized = ip.ToString();
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="address"/> is assigned to a local
    /// network interface (including the loopback adapter).
    /// </summary>
    private static bool IsLocalInterfaceAddress(IPAddress address) {
        if (IPAddress.IsLoopback(address)) {
            return true;
        }

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()) {
            foreach (var unicast in iface.GetIPProperties().UnicastAddresses) {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    unicast.Address.Equals(address))
                    return true;
            }
        }

        return false;
    }

    #endregion
}
