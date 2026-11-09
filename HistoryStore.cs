using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Pulse;

public sealed class HistoryStore
{
    static HistoryStore() => SQLitePCL.Batteries_V2.Init();
    readonly string connectionString;
    long lastSaved = long.MinValue;
    long lastPruned;
    public HistoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "history.sqlite"), Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5 }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
          PRAGMA journal_mode=WAL;
          CREATE TABLE IF NOT EXISTS samples (
            ts INTEGER PRIMARY KEY, cpu REAL, ram REAL, gpu REAL, disk REAL,
            read_bps REAL, write_bps REAL, battery REAL, plugged INTEGER,
            health REAL, cpu_temp REAL, gpu_temp REAL, ssd_temp REAL);
          CREATE TABLE IF NOT EXISTS battery_daily (
            day TEXT PRIMARY KEY, ts INTEGER, health REAL, design_wh REAL, full_wh REAL, cycles INTEGER);
          """;
        cmd.ExecuteNonQuery();
        using var version = db.CreateCommand(); version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema < 2)
        {
            using var migrate = db.CreateCommand();
            // v1 stored a maximum which could include NVMe warning thresholds.
            // A true past measurement cannot be reconstructed; retain a gap.
            migrate.CommandText = "UPDATE samples SET ssd_temp=NULL; PRAGMA user_version=2;"; migrate.ExecuteNonQuery();
        }
    }
    SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    static object Db(object? value) => value ?? DBNull.Value;
    static double? Temp(Snapshot s, string fragment)
    {
        var v = s.Sensors.Where(r => TemperatureRules.IsActual(r) && (fragment == "/intelcpu/" ? (r.Id.Contains("/intelcpu/") || r.Id.Contains("/amdcpu/")) : r.Id.Contains(fragment))).Select(r => r.Value!.Value).ToList();
        return v.Count == 0 ? null : v.Max();
    }
    public bool Save(Snapshot s, bool force = false)
    {
        var ts = s.Time.ToUnixTimeMilliseconds();
        if (!force && lastSaved != long.MinValue && ts >= lastSaved && ts - lastSaved < 15000) return false;
        using var db = Open();
        using var tx = db.BeginTransaction();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO samples VALUES ($ts,$cpu,$ram,$gpu,$disk,$read,$write,$battery,$plugged,$health,$ct,$gt,$st)";
            foreach (var (name, value) in new (string, object?)[] {
                ("$ts",ts),("$cpu",s.Cpu),("$ram",s.MemoryTotal>0?s.MemoryUsed/s.MemoryTotal*100:null),("$gpu",s.Gpu),("$disk",s.DiskActive),
                ("$read",s.DiskRead),("$write",s.DiskWrite),("$battery",s.Battery),("$plugged",s.Plugged?1:0),("$health",s.BatteryQuality.HealthPercent),
                ("$ct",Temp(s,"/intelcpu/")),("$gt",Temp(s,"/gpu")),("$st",Temp(s,"/nvme/")) }) cmd.Parameters.AddWithValue(name, Db(value));
            cmd.ExecuteNonQuery();
        }
        if (s.BatteryQuality.HealthPercent != null)
        {
            using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO battery_daily VALUES ($day,$ts,$health,$design,$full,$cycles)";
            cmd.Parameters.AddWithValue("$day", s.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$ts", ts); cmd.Parameters.AddWithValue("$health", Db(s.BatteryQuality.HealthPercent));
            cmd.Parameters.AddWithValue("$design", Db(s.BatteryQuality.DesignWh)); cmd.Parameters.AddWithValue("$full", Db(s.BatteryQuality.FullWh)); cmd.Parameters.AddWithValue("$cycles", Db(s.BatteryQuality.Cycles)); cmd.ExecuteNonQuery();
        }
        if (lastPruned == 0 || ts - lastPruned > 3600000)
        {
            using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM samples WHERE ts < $cut; DELETE FROM battery_daily WHERE ts < $old;";
            cmd.Parameters.AddWithValue("$cut", s.Time.AddDays(-30).ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$old", s.Time.AddDays(-365).ToUnixTimeMilliseconds()); cmd.ExecuteNonQuery(); lastPruned = ts;
        }
        tx.Commit(); lastSaved = ts; return true;
    }
    static double? Nullable(SqliteDataReader r, int col) => r.IsDBNull(col) ? null : r.GetDouble(col);
    public object Query(string range)
    {
        int hours = range switch { "1h" => 1, "7d" => 168, "30d" => 720, _ => 24 };
        long bucket = hours <= 1 ? 15000 : hours <= 24 ? 120000 : hours <= 168 ? 1200000 : 3600000;
        var since = DateTimeOffset.Now.AddHours(-hours).ToUnixTimeMilliseconds();
        using var db = Open();
        var points = new List<object>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT (ts / $bucket)*$bucket, AVG(cpu),AVG(ram),AVG(gpu),AVG(disk),AVG(battery),AVG(health),MAX(cpu_temp),MAX(gpu_temp),MAX(ssd_temp) FROM samples WHERE ts >= $since GROUP BY ts / $bucket ORDER BY ts / $bucket";
            cmd.Parameters.AddWithValue("$bucket", bucket); cmd.Parameters.AddWithValue("$since", since);
            using var r = cmd.ExecuteReader();
            while (r.Read()) points.Add(new { ts = r.GetInt64(0), cpu = Nullable(r, 1), ram = Nullable(r, 2), gpu = Nullable(r, 3), disk = Nullable(r, 4), battery = Nullable(r, 5), health = Nullable(r, 6), cpuTemp = Nullable(r, 7), gpuTemp = Nullable(r, 8), ssdTemp = Nullable(r, 9) });
        }
        long count; long? first;
        using (var cmd = db.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*),MIN(ts) FROM samples WHERE ts >= $since"; cmd.Parameters.AddWithValue("$since", since); using var r = cmd.ExecuteReader(); r.Read(); count = r.GetInt64(0); first = r.IsDBNull(1) ? null : r.GetInt64(1); }
        var battery = new List<object>();
        using (var cmd = db.CreateCommand()) { cmd.CommandText = "SELECT day,health,design_wh,full_wh,cycles FROM battery_daily ORDER BY day"; using var r = cmd.ExecuteReader(); while (r.Read()) battery.Add(new { day = r.GetString(0), health = Nullable(r, 1), designWh = Nullable(r, 2), fullWh = Nullable(r, 3), cycles = r.IsDBNull(4) ? (int?)null : r.GetInt32(4) }); }
        return new { range, bucketMs = bucket, count, first, points, batteryDaily = battery, retentionDays = 30, batteryRetentionDays = 365, intervalSeconds = 15 };
    }
    public string ExportCsv(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"historial-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv");
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM samples ORDER BY ts";
        using var r = cmd.ExecuteReader(); using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("fecha_iso,cpu_porcentaje,ram_porcentaje,gpu_porcentaje,disco_activo_porcentaje,lectura_bytes_s,escritura_bytes_s,bateria_porcentaje,conectada,salud_bateria_porcentaje,cpu_celsius,gpu_celsius,ssd_celsius");
        while (r.Read())
        {
            var values = new List<string> { DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)).ToLocalTime().ToString("O", CultureInfo.InvariantCulture) };
            for (int i = 1; i < r.FieldCount; i++) values.Add(r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)!);
            writer.WriteLine(string.Join(',', values));
        }
        return path;
    }
}
