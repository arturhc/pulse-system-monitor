namespace Pulse;

public record BatteryQuality(double? DesignWh, double? FullWh, double? HealthPercent, double? WearPercent,
    int? Cycles, double? ChargeWatts, string Assessment)
{
    public static BatteryQuality FromSensors(IEnumerable<Reading> sensors, int? cycles)
    {
        double? Value(string name) => sensors.FirstOrDefault(s => s.Id.StartsWith("/battery/", StringComparison.Ordinal) && s.Name == name)?.Value;
        var design = Value("Designed Capacity");
        var full = Value("Fully-Charged Capacity");
        double? health = design > 0 && full > 0 ? full / design * 100 : null;
        return new(design / 1000, full / 1000, health, health.HasValue ? Math.Max(0, 100 - health.Value) : null,
            cycles, Value("Charge Rate") ?? -Value("Discharge Rate"),
            health == null ? "Sin datos" : health < 60 ? "Desgaste alto" : health < 80 ? "Desgastada" : "Capacidad conservada");
    }
}
