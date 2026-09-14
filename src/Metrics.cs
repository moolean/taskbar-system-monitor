using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TaskbarSystemMonitor
{
    internal sealed class Snapshot
    {
        internal double? Cpu;
        internal double Memory;
        internal ulong UsedBytes, TotalBytes;
        internal double? RxKbps, TxKbps;
        internal string Battery = "—", NetworkName = "未连接";
        internal DateTime Time = DateTime.Now;
        internal string LocalIp = "—";
        internal BriefingData Briefing = new BriefingData();
        internal string CpuText { get { return Cpu.HasValue ? Cpu.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "—"; } }
        internal string MemoryText { get { return Memory.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"; } }
        internal string MemoryDetail { get { return string.Format("{0:0.0} / {1:0.0} GiB", UsedBytes / 1073741824.0, TotalBytes / 1073741824.0); } }
        internal string Value(string id)
        {
            switch (id)
            {
                case "Cpu": return CpuText;
                case "Memory": return MemoryText;
                case "MemoryUsed": return MemoryDetail;
                case "Download": return Speed(RxKbps);
                case "Upload": return Speed(TxKbps);
                case "Battery": return Battery;
                case "Clock": return Time.ToString("MM/dd ddd HH:mm");
                case "Codex": return Briefing.Codex.Short;
                case "Calendar": return Briefing.Calendar.Short;
                case "Ip": return string.IsNullOrEmpty(Briefing.Location.Short) ? LocalIp : Briefing.Location.Short;
                case "Tracks": return Briefing.WorkSummary;
                default: return "—";
            }
        }
        internal static string Speed(double? kib)
        {
            if (!kib.HasValue) return "—";
            double value = Math.Max(0, kib.Value);
            return value >= 1048576 ? (value / 1048576).ToString("0.0") + " GiB/s" : value >= 1024 ? (value / 1024).ToString("0.0") + " MiB/s" : value.ToString("0") + " KiB/s";
        }
    }
    internal sealed class Sampler
    {
        private ulong idle, kernel, user;
        private bool baseline;
        internal Snapshot Sample()
        {
            FileTime i, k, u;
            if (!GetSystemTimes(out i, out k, out u)) throw new Win32Exception();
            double? cpu = baseline ? Calculate(idle, kernel, user, i.Value, k.Value, u.Value) : null;
            idle = i.Value; kernel = k.Value; user = u.Value; baseline = true;
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (!GlobalMemoryStatusEx(ref memory)) throw new Win32Exception();
            if (memory.TotalPhysical == 0 || memory.AvailablePhysical > memory.TotalPhysical)
                throw new InvalidOperationException("物理内存计数无效");
            ulong used = memory.TotalPhysical - memory.AvailablePhysical;
            return new Snapshot { Cpu = cpu, Memory = used * 100.0 / memory.TotalPhysical, UsedBytes = used, TotalBytes = memory.TotalPhysical };
        }
        // GetSystemTimes kernel includes idle. Counter resets and zero intervals are unavailable, not 0%.
        internal static double? Calculate(ulong oldI, ulong oldK, ulong oldU, ulong i, ulong k, ulong u)
        {
            if (i < oldI || k < oldK || u < oldU) return null;
            double total = (double)(k - oldK) + (u - oldU);
            double idleDelta = i - oldI;
            if (total <= 0 || idleDelta > total) return null;
            return Math.Max(0, Math.Min(100, (total - idleDelta) * 100.0 / total));
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            internal uint Low, High;
            internal ulong Value { get { return ((ulong)High << 32) | Low; } }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, Extended;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    }
    internal sealed class History
    {
        internal readonly List<double?> Cpu = new List<double?>(), Memory = new List<double?>();
        internal void Add(Snapshot snapshot)
        {
            Cpu.Add(snapshot == null ? null : snapshot.Cpu);
            Memory.Add(snapshot == null ? null : (double?)snapshot.Memory);
            if (Cpu.Count > 120) { Cpu.RemoveAt(0); Memory.RemoveAt(0); }
        }
    }
}
