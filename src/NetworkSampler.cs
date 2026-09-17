using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace TaskbarSystemMonitor
{
    internal sealed class NetworkSampler
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double previousTime, refreshedAt = -20;
        private long previousRx, previousTx;
        private string previousId;
        private NetworkInterface[] adapters = new NetworkInterface[0];

        internal void Sample(Snapshot snapshot, string selectedId)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now - refreshedAt >= 10)
            {
                adapters = NetworkInterface.GetAllNetworkInterfaces(); refreshedAt = now;
            }
            NetworkInterface chosen = string.IsNullOrEmpty(selectedId)
                ? adapters.Where(x => x.OperationalStatus == OperationalStatus.Up && HasGateway(x)).OrderByDescending(x => x.Speed).FirstOrDefault()
                : adapters.FirstOrDefault(x => x.Id == selectedId && x.OperationalStatus == OperationalStatus.Up);
            if (chosen == null) { previousId = null; snapshot.NetworkName = "Offline"; return; }
            IPInterfaceStatistics stats = chosen.GetIPStatistics();
            if (previousId == chosen.Id)
            {
                snapshot.RxKbps = Rate(previousRx, stats.BytesReceived, now - previousTime);
                snapshot.TxKbps = Rate(previousTx, stats.BytesSent, now - previousTime);
            }
            previousRx = stats.BytesReceived; previousTx = stats.BytesSent; previousTime = now; previousId = chosen.Id;
            snapshot.NetworkName = chosen.Name;
            snapshot.LocalIp = string.Join(" / ", chosen.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(x => x.Address.ToString()).ToArray());
        }
        internal static double? Rate(long before, long after, double seconds)
        {
            if (after < before || seconds <= 0 || seconds > 30) return null;
            return (after - before) / 1024.0 / seconds;
        }
        private static bool HasGateway(NetworkInterface adapter)
        {
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return false;
            try { return adapter.GetIPProperties().GatewayAddresses.Count > 0; } catch { return false; }
        }
        internal static string Battery()
        {
            PowerStatus status = SystemInformation.PowerStatus;
            int flags = (int)status.BatteryChargeStatus;
            if (flags == 255) return "—";
            if ((flags & 128) != 0) return "N/A";
            float value = status.BatteryLifePercent;
            if (value < 0 || value > 1) return "—";
            return ((flags & 8) != 0 ? "AC+ " : status.PowerLineStatus == PowerLineStatus.Online ? "AC " : "") + (value * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
    }
}
