using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pulse;

static class StorageTests
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
            var now = new DateTimeOffset(DateTime.Today.AddHours(12));
            var sensors = new List<Reading> { new("/battery/0/energy/0", "Battery", "Designed Capacity", "Energy", 50000), new("/battery/0/energy/1", "Battery", "Fully-Charged Capacity", "Energy", 30000) };
            var quality = BatteryQuality.FromSensors(sensors, 38);
            Check(quality.HealthPercent == 60 && quality.WearPercent == 40, "Battery ratio");
            Check(BatteryQuality.FromSensors(new List<Reading>(), null).HealthPercent == null, "Missing capacity must stay null");
            Check(!TemperatureRules.IsActual(new("/nvme/0/temperature/11", "SSD", "Critical Temperature", "Temperature", 83)), "NVMe threshold is not actual temperature");
            var s = new Snapshot(now, 20, 8e9, 16e9, 4, 1024, 2048, 100e9, 512e9, 12, 80, true, null, false, "CPU", "GPU", "SSD", sensors, null, false, quality);
            var store = new HistoryStore(directory);
            Check(store.Save(s with { Time = now.AddDays(-400) }, true), "Old insert");
            Check(store.Save(s with { Time = now.AddDays(-40) }, true), "Old telemetry insert");
            Check(store.Save(s, true), "Current insert");
            Check(!store.Save(s with { Time = now.AddSeconds(2) }), "Sampling rate limit");
            Check(store.Save(s with { Time = now.AddSeconds(16) }), "Next sample");
            var reopened = new HistoryStore(directory);
            var data = JsonSerializer.SerializeToElement(reopened.Query("24h"), Program.Json);
            Check(data.GetProperty("count").GetInt64() == 2, "Reopen and retention persistence");
            Check(data.GetProperty("batteryDaily").GetArrayLength() == 2, "Battery daily retention and same-day upsert");
            var export = reopened.ExportCsv(Path.Combine(directory, "exports"));
            Check(File.ReadAllLines(export).Length == 3, "CSV row count");
            var point = data.GetProperty("points")[0];
            Check(point.GetProperty("cpuTemp").ValueKind == JsonValueKind.Null, "Absent temperature must stay null");
            File.WriteAllText(Path.Combine(directory, "test-result.txt"), "PASS: battery ratio, missing data, sampling, persistence, 30/365-day retention, daily upsert, CSV and null temperatures.");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "test-result.txt"), "FAIL: " + ex); Environment.ExitCode = 1; }
        finally { SqliteConnection.ClearAllPools(); }
    }
}
