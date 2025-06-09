using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

class Program
{
    static List<float> cpuHistory = new();
    static List<float> ramHistory = new();
    static List<float> gpuHistory = new();
    const int GRAPH_WIDTH = 25;
    const int GRAPH_HEIGHT = 4;
    static DateTime startTime = DateTime.Now;

    // --- Windows counters
    static PerformanceCounter? cpuCounter;
    static PerformanceCounter? gpuUsageCounter;
    static PerformanceCounter? netSentCounter;
    static PerformanceCounter? netReceivedCounter;
    static string? winNetInterface;
    static string? winGpuInstance;

    // Linux/Unix state for CPU/network
    static ulong[]? lastCpuTimes;
    static DateTime lastCpuSample = DateTime.MinValue;
    static long lastNetSent = -1, lastNetRecv = -1;
    static DateTime lastNetSample = DateTime.MinValue;

    static void Main()
    {
        Console.Clear();
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            winNetInterface = GetActiveNetworkInterfaceName();
            if (winNetInterface != null)
            {
                Console.WriteLine($"Using network interface for counters: {winNetInterface}");
                netSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", winNetInterface);
                netReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", winNetInterface);
            }
            else
            {
                Console.WriteLine("No suitable network interface found for counters!");
            }
            winGpuInstance = GetGPU3DEngineInstance();
            if (winGpuInstance != null)
            {
                gpuUsageCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", winGpuInstance);
            }
#pragma warning restore CA1416  
            Thread.Sleep(1000);
        }

        // Prime CPU sample for non-Windows
        if (!OperatingSystem.IsWindows())
            GetCPUUsage();

        while (true)
        {
            Console.SetCursorPosition(0, 0);
            DisplaySystemStats();
            Thread.Sleep(1000);
        }
    }

    static void DisplaySystemStats()
    {
        float cpuUsage = GetCPUUsage();
        float ramUsage = GetRAMUsage();
        float diskUsage = GetDiskUsage();
        float gpuUsage = GetGPUUsage();
        gpuHistory.Add(gpuUsage);
        if (gpuHistory.Count > GRAPH_WIDTH) gpuHistory.RemoveAt(0);

        string uptime = (DateTime.Now - startTime).ToString(@"hh\:mm\:ss");

        cpuHistory.Add(cpuUsage);
        ramHistory.Add(ramUsage);
        if (cpuHistory.Count > GRAPH_WIDTH) cpuHistory.RemoveAt(0);
        if (ramHistory.Count > GRAPH_WIDTH) ramHistory.RemoveAt(0);

        string cpuBar = GetBar(cpuUsage);
        string ramBar = GetBar(ramUsage);
        string diskBar = GetBar(diskUsage);

        Console.WriteLine("\x1b[1;37m=== CLI SYSTEM MONITOR (Cross-platform) ===\x1b[0m\n");
        Console.WriteLine($"\x1b[1;33mCPU Usage:        {cpuUsage,5:F1}% {cpuBar}\x1b[0m");
        Console.WriteLine($"\x1b[1;34mGPU Usage:        {gpuUsage,5:F1}%\x1b[0m");
        Console.WriteLine($"\x1b[1;36mRAM Usage:        {ramUsage,5:F1}% {ramBar}\x1b[0m");
        Console.WriteLine($"\x1b[1;35mDisk Usage:       {diskUsage,5:F1}% {diskBar}\x1b[0m");
        Console.WriteLine($"\x1b[1;32mNetwork Sent:     {GetNetworkSent(),6:F1} KB/s\x1b[0m");
        Console.WriteLine($"\x1b[1;32mNetwork Received: {GetNetworkReceived(),6:F1} KB/s\x1b[0m");

        int right = 45;
        Console.SetCursorPosition(right, 2);
        Console.WriteLine("\x1b[1;37m╔════════════════════════════╗\x1b[0m");
        Console.SetCursorPosition(right, 3);
        Console.WriteLine($"\x1b[1;37m║  Host: {Environment.MachineName,-18}║\x1b[0m");
        Console.SetCursorPosition(right, 4);
        Console.WriteLine($"\x1b[1;37m║  OS:   {RuntimeInformation.OSDescription,-16}║\x1b[0m");
        Console.SetCursorPosition(right, 5);
        Console.WriteLine($"\x1b[1;37m║  Uptime: {uptime,-16}║\x1b[0m");
        Console.SetCursorPosition(right, 6);
        Console.WriteLine("\x1b[1;37m╚════════════════════════════╝\x1b[0m");

        DrawGraph(cpuHistory, "CPU Trend", 8, "\x1b[1;33m");
        DrawGraph(ramHistory, "RAM Trend", 14, "\x1b[1;36m");
        DrawGraph(gpuHistory, "GPU Trend", 20, "\x1b[1;34m");
    }

    static string GetBar(float percent)
    {
        int width = 10;
        int filled = (int)(width * percent / 100);
        return "[" + new string('█', filled) + new string('-', width - filled) + "]";
    }

    static float GetCPUUsage()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            if (cpuCounter == null) return 0f;
            cpuCounter.NextValue();
            Thread.Sleep(200);
            return cpuCounter.NextValue();
#pragma warning restore CA1416  
        }
        // non-Windows
        try
        {
            var stat = File.ReadLines("/proc/stat").FirstOrDefault();
            if (stat == null) return 0f;
            var vals = stat.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ulong.Parse).ToArray();

            if (lastCpuTimes == null)
            {
                lastCpuTimes = vals;
                lastCpuSample = DateTime.Now;
                Thread.Sleep(200);
                return GetCPUUsage();
            }
            else
            {
                ulong idle1 = lastCpuTimes[3] + (lastCpuTimes.Length > 4 ? lastCpuTimes[4] : 0);
                ulong idle2 = vals[3] + (vals.Length > 4 ? vals[4] : 0);
                double total1 = lastCpuTimes.Select(x => (double)x).Sum();
                double total2 = vals.Select(x => (double)x).Sum();
                float idleDelta = (float)(idle2 - idle1);
                float totalDelta = (float)(total2 - total1);
                lastCpuTimes = vals;
                lastCpuSample = DateTime.Now;
                if (totalDelta <= 0) return 0f;
                return 100f * (1.0f - idleDelta / totalDelta);
            }
        }
        catch { return 0f; }
    }

    static float GetRAMUsage()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            try
            {
                var wmi = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (System.Management.ManagementObject obj in wmi.Get())
                {
                    ulong total = (ulong)obj["TotalVisibleMemorySize"] * 1024;
                    ulong free = (ulong)obj["FreePhysicalMemory"] * 1024;
                    return 100.0f * (total - free) / total;
                }
            }
            catch { }
#pragma warning restore CA1416  
            return 0f;
        }
        else
        {
            try
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                ulong total = ulong.Parse(lines.First(l => l.StartsWith("MemTotal")).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                ulong available = ulong.Parse(lines.First(l => l.StartsWith("MemAvailable")).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                return 100.0f * (total - available) / total;
            }
            catch { return 0f; }
        }
    }

    static float GetDiskUsage()
    {
        try
        {
            DriveInfo? drive = null;
            if (OperatingSystem.IsWindows())
                drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == @"C:\");
            else
                drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == "/");
            if (drive != null)
            {
                double used = drive.TotalSize - drive.TotalFreeSpace;
                return (float)(100.0 * used / drive.TotalSize);
            }
        }
        catch { }
        return 0f;
    }

    static float GetGPUUsage()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            try
            {
                if (gpuUsageCounter != null)
                {
                    gpuUsageCounter.NextValue();
                    Thread.Sleep(100);
                    return gpuUsageCounter.NextValue();
                }
            }
            catch { }
#pragma warning restore CA1416  
            return 0f;
        }

        return 0f;
    }

    static float GetNetworkSent()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            try { return netSentCounter?.NextValue() / 1024 ?? 0f; } catch { return 0f; }
#pragma warning restore CA1416  
        }
        else
        {
            return GetNetDelta("tx_bytes") / 1024f;
        }
    }

    static float GetNetworkReceived()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            try { return netReceivedCounter?.NextValue() / 1024 ?? 0f; } catch { return 0f; }
#pragma warning restore CA1416  
        }
        else
        {
            return GetNetDelta("rx_bytes") / 1024f;
        }
    }

    static float GetNetDelta(string field)
    {
        string iface = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            ?.Name ?? "eth0";
        string path = $"/sys/class/net/{iface}/statistics/{field}";
        long curr = 0;
        try { curr = long.Parse(File.ReadAllText(path)); } catch { }
        float rate = 0f;
        DateTime now = DateTime.Now;
        if (lastNetSample != DateTime.MinValue)
        {
            double dt = (now - lastNetSample).TotalSeconds;
            if (field == "tx_bytes" && lastNetSent >= 0)
                rate = (curr - lastNetSent) / (float)Math.Max(dt, 1.0);
            else if (field == "rx_bytes" && lastNetRecv >= 0)
                rate = (curr - lastNetRecv) / (float)Math.Max(dt, 1.0);
        }
        if (field == "tx_bytes") lastNetSent = curr;
        else if (field == "rx_bytes") lastNetRecv = curr;
        lastNetSample = now;
        return rate;
    }

    static void DrawGraph(List<float> history, string title, int topRow, string color)
    {
        int right = 45;
        float maxVal = history.Count > 0 && history.Max() > 0 ? history.Max() : 1f;

        Console.SetCursorPosition(right, topRow);
        Console.WriteLine($"{color}╔═══════ {title} ════════╗\x1b[0m");
        for (int row = 1; row <= GRAPH_HEIGHT; row++)
        {
            Console.SetCursorPosition(right, topRow + row);
            Console.Write($"{color}║ \x1b[0m");
            for (int i = 0; i < history.Count; i++)
            {
                float val = history[i];
                float heightPerRow = maxVal / GRAPH_HEIGHT;
                Console.Write(val >= heightPerRow * (GRAPH_HEIGHT - row + 1) ? $"{color}█\x1b[0m" : "░");
            }
            Console.Write(new string(' ', GRAPH_WIDTH - history.Count));
            Console.Write($"{color} ║\x1b[0m");
        }
        Console.SetCursorPosition(right, topRow + GRAPH_HEIGHT + 1);
        Console.WriteLine($"{color}╚══════════════════════════╝\x1b[0m");
    }

    // Windows only helpers
    static string? GetActiveNetworkInterfaceName()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            var category = new PerformanceCounterCategory("Network Interface");
            var instances = category.GetInstanceNames();

            Console.WriteLine("Available network interfaces for PerformanceCounter:");
            foreach (var name in instances)
                Console.WriteLine($" - {name}");

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up &&
                            i.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var ni in interfaces)
            {
                var match = instances.FirstOrDefault(inst => inst == ni.Name);
                if (match != null)
                    return match;
            }

            return instances.FirstOrDefault(i => !i.ToLowerInvariant().Contains("loopback"));
#pragma warning restore CA1416  
        }
        return null;
    }

    static string? GetGPU3DEngineInstance()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416  
            var category = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames();
#pragma warning restore CA1416  
            return instances.FirstOrDefault(inst => inst.ToLower().Contains("engtype_3d"));
        }
        return null;
    }
}