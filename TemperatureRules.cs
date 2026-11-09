namespace Pulse;

public static class TemperatureRules
{
    public static bool IsActual(Reading r) => r.Kind == "Temperature" && r.Value != null &&
        !new[] { "Distance to TjMax", "Warning Temperature", "Critical Temperature", "Threshold" }.Any(term => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
}
