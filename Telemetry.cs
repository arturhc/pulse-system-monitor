using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace Pulse;

public record Reading(string Id, string Hardware, string Name, string Kind, double? Value);
public record MachineIdentity(string Manufacturer, string Model, int? Cores, int Threads);
public record Snapshot(DateTimeOffset Time, double? Cpu, double MemoryUsed, double MemoryTotal,
    double? DiskActive, double? DiskRead, double? DiskWrite, double? DiskUsed, double? DiskTotal,
    double? Gpu, double? Battery, bool Plugged, int? BatterySeconds, bool Admin,
    string CpuName, string GpuName, string DiskName, List<Reading> Sensors, string? SensorError, bool PawnIoInstalled, BatteryQuality BatteryQuality, MachineIdentity? Machine = null);

public sealed class Telemetry : IDisposable
{
    readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true, IsBatteryEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
    readonly List<Reading> sensors = new();
    readonly PerformanceCounter? read, write, idle;
    readonly Dictionary<string, PerformanceCounter> gpuCounters = new();
    readonly bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    readonly DriveInfo disk = new(Path.GetPathRoot(Environment.SystemDirectory)!);
    ulong previousIdle, previousKernel, previousUser;
    int ticks;
    string cpuName = "CPU", gpuName = "GPU", diskName = "SSD";
    string? sensorError;
    bool hardwareReady;
    readonly bool pawnIoInstalled;
    readonly int? batteryCycles;
    MachineIdentity machine = new("", "Equipo Windows", null, Environment.ProcessorCount);

    public Telemetry()
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO")) pawnIoInstalled = key != null;
        try
        {
            using var query = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive WHERE Index = 0");
            using var drives = query.Get();
            foreach (ManagementObject drive in drives) { diskName = drive["Model"]?.ToString()?.Trim() ?? "SSD"; drive.Dispose(); }
        }
        catch { }
        try
        {
            using var systemQuery = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
            using var systems = systemQuery.Get();
            foreach (ManagementObject item in systems)
            {
                machine = machine with { Manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? "", Model = item["Model"]?.ToString()?.Trim() ?? "Equipo Windows" };
                item.Dispose(); break;
            }
            using var cpuQuery = new ManagementObjectSearcher("SELECT NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor");
            using var cpus = cpuQuery.Get(); int cores = 0, threads = 0;
            foreach (ManagementObject item in cpus) { cores += Convert.ToInt32(item["NumberOfCores"]); threads += Convert.ToInt32(item["NumberOfLogicalProcessors"]); item.Dispose(); }
            machine = machine with { Cores = cores > 0 ? cores : null, Threads = threads > 0 ? threads : Environment.ProcessorCount };
        }
        catch { }
        try
        {
            using var query = new ManagementObjectSearcher(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount");
            using var batteries = query.Get();
            foreach (ManagementObject item in batteries) { batteryCycles = Convert.ToInt32(item["CycleCount"]); item.Dispose(); break; }
        }
        catch { }
        GetSystemTimes(out previousIdle, out previousKernel, out previousUser);
        try { read = Counter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"); write = Counter("PhysicalDisk", "Disk Write Bytes/sec", "_Total"); idle = Counter("PhysicalDisk", "% Idle Time", "_Total"); } catch { }
        try { computer.Open(); hardwareReady = true; } catch (Exception ex) { sensorError = ex.Message; }
    }
    static PerformanceCounter Counter(string category, string name, string instance)
    { var c = new PerformanceCounter(category, name, instance, true); c.NextValue(); return c; }
    static double? Sample(PerformanceCounter? c)
    { try { var v = c?.NextValue(); return v.HasValue && float.IsFinite(v.Value) ? v.Value : null; } catch { return null; } }

    public Snapshot Collect()
    {
        double? cpu = null;
        if (GetSystemTimes(out var i, out var k, out var u))
        {
            var total = (k - previousKernel) + (u - previousUser);
            if (total > 0) cpu = Math.Clamp(100.0 * (total - Math.Min(total, i - previousIdle)) / total, 0, 100);
            (previousIdle, previousKernel, previousUser) = (i, k, u);
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        GetSystemPowerStatus(out var power);

        if (hardwareReady)
        {
            foreach (var hw in computer.Hardware)
            {
                bool storage = hw.HardwareType == HardwareType.Storage;
                if (storage && ticks % 15 != 0) continue;
                try
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();
                    if (hw.HardwareType == HardwareType.Cpu) cpuName = hw.Name;
                    if (hw.HardwareType.ToString().StartsWith("Gpu")) gpuName = hw.Name;
                    sensors.RemoveAll(s => s.Id.StartsWith(hw.Identifier + "/", StringComparison.Ordinal));
                    AddSensors(hw);
                }
                catch (Exception ex)
                {
                    sensors.RemoveAll(s => s.Id.StartsWith(hw.Identifier + "/", StringComparison.Ordinal));
                    sensorError = ex.Message;
                }
            }
        }

        if (ticks % 5 == 0) RefreshGpuCounters();
        double? gpu = null;
        var values = gpuCounters.Select(c => (Name: c.Key, Value: Sample(c.Value))).Where(v => v.Value.HasValue).ToList();
        if (values.Count > 0)
        {
            // Windows defines GPU utilization as the busiest engine, not the sum of all engines.
            var engines = values.GroupBy(v => v.Name.Contains("_luid_") ? v.Name[(v.Name.IndexOf("_luid_", StringComparison.Ordinal) + 1)..] : v.Name);
            gpu = Math.Clamp(engines.Max(g => g.Sum(v => v.Value!.Value)), 0, 100);
        }
        gpu ??= sensors.Where(s => s.Kind == "Load" && s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)).Select(s => s.Value).FirstOrDefault();
        double? diskUsed = null, diskTotal = null;
        try { diskTotal = disk.TotalSize; diskUsed = disk.TotalSize - disk.TotalFreeSpace; } catch { }
        var idleValue = Sample(idle);
        ticks++;
        return new Snapshot(DateTimeOffset.Now, cpu, memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical,
            idleValue.HasValue ? Math.Clamp(100 - idleValue.Value, 0, 100) : null, Sample(read), Sample(write), diskUsed, diskTotal,
            gpu, power.BatteryLifePercent <= 100 && power.BatteryFlag != 128 ? power.BatteryLifePercent : null, power.AcLineStatus == 1,
            power.BatteryLifeTime >= 0 ? power.BatteryLifeTime : null, admin, cpuName, gpuName, diskName, new(sensors), sensorError, pawnIoInstalled, BatteryQuality.FromSensors(sensors, batteryCycles), machine);
    }

    void AddSensors(IHardware hw)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType is SensorType.Temperature or SensorType.Load or SensorType.Clock or SensorType.Power or SensorType.Level or SensorType.Fan or SensorType.Energy or SensorType.Data or SensorType.SmallData)
                sensors.Add(new(s.Identifier.ToString(), hw.Name, s.Name, s.SensorType.ToString(), s.Value is float v && float.IsFinite(v) ? v : null));
        foreach (var sub in hw.SubHardware) AddSensors(sub);
    }
    void RefreshGpuCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var names = category.GetInstanceNames().ToHashSet();
            foreach (var old in gpuCounters.Keys.Where(k => !names.Contains(k)).ToList()) { gpuCounters[old].Dispose(); gpuCounters.Remove(old); }
            foreach (var name in names)
                if (!gpuCounters.ContainsKey(name))
                    try { gpuCounters[name] = Counter("GPU Engine", "Utilization Percentage", name); } catch { }
        }
        catch { }
    }
    public void Dispose()
    {
        computer.Close(); read?.Dispose(); write?.Dispose(); idle?.Dispose();
        foreach (var c in gpuCounters.Values) c.Dispose();
    }
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus
    { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
}
