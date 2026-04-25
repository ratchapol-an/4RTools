using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RagnarokAutomation.Core;

public static class MonitoringClassifier
{
    public static (RuntimeState state, RootCause rootCause, string evidence) Classify(ProcessSnapshot snapshot, bool internetAvailable)
    {
        if (!internetAvailable)
        {
            return (RuntimeState.Disconnected, RootCause.InternetDown, "internet probe failed");
        }

        if (!snapshot.IsProcessAlive)
        {
            return (RuntimeState.Disconnected, RootCause.ClientCrashed, "process exited");
        }

        if (snapshot.EstablishedConnections == 0 && snapshot.ClosingConnections > 0)
        {
            return (RuntimeState.Disconnected, RootCause.ServerDown, $"closing={snapshot.ClosingConnections}");
        }

        if (snapshot.EstablishedConnections == 0)
        {
            return (RuntimeState.Disconnected, RootCause.ServerDown, "no active remote tcp sessions");
        }

        return (RuntimeState.InGame, RootCause.Unknown, $"established={snapshot.EstablishedConnections}");
    }

    public static async Task<bool> ProbeInternetAsync()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return false;
        }

        if (await TryPingAsync("1.1.1.1", 1200).ConfigureAwait(false))
        {
            return true;
        }

        if (await TryPingAsync("8.8.8.8", 1200).ConfigureAwait(false))
        {
            return true;
        }

        // Fallback: TCP connect check in case ICMP is blocked.
        try
        {
            using TcpClient tcp = new();
            using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(1500));
            await tcp.ConnectAsync("1.1.1.1", 443, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryPingAsync(string host, int timeoutMs)
    {
        try
        {
            using Ping ping = new();
            PingReply reply = await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
