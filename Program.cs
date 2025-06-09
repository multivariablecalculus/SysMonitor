using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Threading;
using System.Collections.Generic;

[SupportedOSPlatform("windows")]
class Program
{
    static PerformanceCounter cpuCounter = new("Processor", "% Processor Time", "_Total");
    static PerformanceCounter? gpuUsageCounter;
    static List<float> gpuHistory = new();
    const int GPU_GRAPH_TOP_ROW = 20;
    static PerformanceCounter? netSentCounter;
    static PerformanceCounter? netReceivedCounter;
    static List<float> cpuHistory = new();
    static List<float> ramHistory = new();
    const int GRAPH_WIDTH = 25;
    const int GRAPH_HEIGHT = 4;

    static DateTime startTime = DateTime.Now;

    static void Main()
    {
        string? netInterface = GetActiveNetworkInterfaceName();
        if (netInterface != null)
        {
            netSentCounter = new("Network Interface", "Bytes Sent/sec", netInterface);
            netReceivedCounter = new("Network Interface", "Bytes Received/sec", netInterface);
        }

        string? gpuInstance = GetGPU3DEngineInstance();
        if (gpuInstance != null)
        {
            gpuUsageCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", gpuInstance);
        }

        Console.Clear();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        while (true)
        {
            Console.SetCursorPosition(0, 0);
            DisplaySystemStats();
            Thread.Sleep(1000);
        }
    }

    static void DisplaySystemStats()
    {
        float cpuUsage = cpuCounter.NextValue();
        Thread.Sleep(200);
        cpuUsage = cpuCounter.NextValue();

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

        Console.WriteLine("\x1b[1;37m=== CLI SYSTEM MONITOR ===\x1b[0m\n");
        Console.WriteLine($"\x1b[1;33mCPU Usage:        {cpuUsage,5:F1}% {cpuBar}\x1b[0m");
        Console.WriteLine($"\x1b[1;34mGPU Usage:        {gpuUsage,5:F1}%\x1b[0m");
        Console.WriteLine($"\x1b[1;36mRAM Usage:        {ramUsage,5:F1}% {ramBar}\x1b[0m");
        Console.WriteLine($"\x1b[1;35mDisk C:\\ Usage:   {diskUsage,5:F1}% {diskBar}\x1b[0m");
        Console.WriteLine($"\x1b[1;32mNetwork Sent:     {GetNetworkSent(),6:F1} KB/s\x1b[0m");
        Console.WriteLine($"\x1b[1;32mNetwork Received: {GetNetworkReceived(),6:F1} KB/s\x1b[0m");

        int right = 45;
        Console.SetCursorPosition(right, 2);
        Console.WriteLine("\x1b[1;37m╔════════════════════════════╗\x1b[0m");
        Console.SetCursorPosition(right, 3);
        Console.WriteLine($"\x1b[1;37m║  Host: {Environment.MachineName,-18}║\x1b[0m");
        Console.SetCursorPosition(right, 4);
        Console.WriteLine($"\x1b[1;37m║  OS:   {Environment.OSVersion.VersionString,-16}║\x1b[0m");
        Console.SetCursorPosition(right, 5);
        Console.WriteLine($"\x1b[1;37m║  Uptime: {uptime,-16}║\x1b[0m");
        Console.SetCursorPosition(right, 6);
        Console.WriteLine("\x1b[1;37m╚════════════════════════════╝\x1b[0m");

        DrawGraph(cpuHistory, "CPU Trend", 8, "\x1b[1;33m");
        DrawGraph(ramHistory, "RAM Trend", 14, "\x1b[1;36m");
        DrawGraph(gpuHistory, "GPU Trend", GPU_GRAPH_TOP_ROW, "\x1b[1;34m");
    }

    static string GetBar(float percent)
    {
        int width = 10;
        int filled = (int)(width * percent / 100);
        return "[" + new string('█', filled) + new string('-', width - filled) + "]";
    }

    static float GetRAMUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                ulong total = (ulong)obj["TotalVisibleMemorySize"] * 1024;
                ulong free = (ulong)obj["FreePhysicalMemory"] * 1024;
                return 100.0f * (total - free) / total;
            }
        }
        catch { }
        return 0f;
    }

    static float GetDiskUsage()
    {
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == @"C:\");
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
        return 0f;
    }

    static float GetNetworkSent() => netSentCounter?.NextValue() / 1024 ?? 0f;
    static float GetNetworkReceived() => netReceivedCounter?.NextValue() / 1024 ?? 0f;

    static string GetCPUTemperature()
    {
        try
        {
            using ManagementObjectSearcher searcher = new(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] != null)
                {
                    double kelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    double celsius = (kelvin / 10) - 273.15;
                    return celsius.ToString("F1");
                }
            }
        }
        catch { }

        return "N/A";
    }

    static void DrawGraph(List<float> history, string title, int topRow, string color)
    {
        int right = 45;
        float maxVal = history.Max() > 0 ? history.Max() : 1f;

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

    static string? GetActiveNetworkInterfaceName()
    {
        var category = new PerformanceCounterCategory("Network Interface");
        var instances = category.GetInstanceNames();

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.OperationalStatus == OperationalStatus.Up &&
                        i.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        string Normalize(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

        foreach (var ni in interfaces)
        {
            string normalizedDescription = Normalize(ni.Description);

            var match = instances.FirstOrDefault(inst =>
            {
                string normalizedInstance = Normalize(inst);
                return normalizedInstance.Contains(normalizedDescription) || normalizedDescription.Contains(normalizedInstance);
            });

            if (match != null)
                return match;
        }


        return instances.FirstOrDefault(i => !i.ToLowerInvariant().Contains("loopback"));
    }
    
    static string? GetGPU3DEngineInstance()
    {
        var category = new PerformanceCounterCategory("GPU Engine");
        var instances = category.GetInstanceNames();

        return instances.FirstOrDefault(inst => inst.ToLower().Contains("engtype_3d"));
    }


}
