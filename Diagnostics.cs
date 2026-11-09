using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Pulse;

public record CheckResult(string Name, string Status, string Detail);
public record DiagnosticReport(string Id, DateTimeOffset Time, List<CheckResult> Checks, BatteryQuality Battery, List<object> Events, string Folder, string? BatteryReport);

public static class Diagnostics
{
    public static async Task<DiagnosticReport> Run(Snapshot s, string root)
    {
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var folder = Path.Combine(root, "diagnosticos", id); Directory.CreateDirectory(folder);
        var checks = new List<CheckResult>(); var events = new List<object>(); var battery = s.BatteryQuality;
        string? batteryHtml = null;
        var xml = Path.Combine(folder, "bateria-windows.xml");
        var result = await PowerCfg("/batteryreport", "/output", xml, "/xml");
        if (result.Exit == 0 && File.Exists(xml))
        {
            try
            {
                var doc = XDocument.Load(xml);
                var b = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Battery");
                double? Num(string field) => double.TryParse(b?.Elements().FirstOrDefault(x => x.Name.LocalName == field)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
                var design = Num("DesignCapacity"); var full = Num("FullChargeCapacity"); var cycle = Num("CycleCount");
                if (design > 0 && full > 0)
                {
                    var health = full / design * 100;
                    battery = new(design / 1000, full / 1000, health, Math.Max(0, 100 - health!.Value), cycle != null ? (int)cycle : s.BatteryQuality.Cycles, s.BatteryQuality.ChargeWatts, health < 60 ? "Desgaste alto" : health < 80 ? "Desgastada" : "Capacidad conservada");
                }
                checks.Add(new("Informe de batería de Windows", "completado", "Reporte nativo generado y guardado; capacidades y ciclos informados por el controlador."));
            }
            catch (Exception ex) { checks.Add(new("Informe de batería de Windows", "parcial", "XML guardado, lectura no disponible: " + ex.Message)); }
            var html = Path.Combine(folder, "bateria-windows.html"); var h = await PowerCfg("/batteryreport", "/output", html);
            if (h.Exit == 0 && File.Exists(html)) batteryHtml = html;
        }
        else checks.Add(new("Informe de batería de Windows", "no disponible", result.Text.Trim()));
        checks.Add(new("Capacidad de batería", battery.HealthPercent == null ? "no disponible" : battery.HealthPercent < 80 ? "atención" : "informado",
            battery.HealthPercent == null ? "El controlador no informa capacidades comparables." : $"Conserva {battery.HealthPercent:F1} % ({battery.FullWh:F2} / {battery.DesignWh:F2} Wh); desgaste estimado {battery.WearPercent:F1} %. Ciclos: {battery.Cycles?.ToString() ?? "no disponibles"}. Es una estimación del controlador, no una prueba de autonomía."));
        checks.Add(new("Memoria física", "informado", $"{s.MemoryUsed / 1073741824:F1} de {s.MemoryTotal / 1073741824:F1} GB en uso. Esta lectura no prueba la integridad física de la RAM."));
        var free = s.DiskTotal - s.DiskUsed; var freePct = s.DiskTotal > 0 ? free / s.DiskTotal * 100 : null;
        checks.Add(new("Espacio en el SSD", freePct == null ? "no disponible" : freePct < 10 ? "atención" : "informado", freePct == null ? "No se pudo consultar." : $"{free / 1073741824:F1} GB libres ({freePct:F1} %)."));
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Model,Status FROM Win32_DiskDrive"); using var items = q.Get();
            foreach (ManagementObject item in items) { checks.Add(new("Estado informado por Windows · SSD", "informado", $"{item["Model"]}: {item["Status"]}. No equivale a una prueba completa de superficie o SMART.")); item.Dispose(); }
        }
        catch (Exception ex) { checks.Add(new("Estado informado por Windows · SSD", "no disponible", ex.Message)); }
        try
        {
            using var q = new ManagementObjectSearcher(@"root\wmi", "SELECT PredictFailure,Reason FROM MSStorageDriver_FailurePredictStatus"); using var items = q.Get(); int count = 0;
            foreach (ManagementObject item in items) { count++; bool predicted = Convert.ToBoolean(item["PredictFailure"]); checks.Add(new("Predicción de fallo SMART", predicted ? "atención" : "informado", predicted ? $"Windows informa predicción de fallo; razón {item["Reason"]}." : "Este proveedor no señala un fallo previsto. No descarta todos los problemas del disco.")); item.Dispose(); }
            if (count == 0) checks.Add(new("Predicción de fallo SMART", "no disponible", "Este disco/controlador no expone datos mediante el proveedor WMI consultado."));
        }
        catch { checks.Add(new("Predicción de fallo SMART", "no disponible", "El proveedor no está disponible para este disco o requiere otros permisos.")); }
        double? Nvme(string name) => s.Sensors.FirstOrDefault(x => x.Id.StartsWith("/nvme/", StringComparison.Ordinal) && x.Name == name)?.Value;
        var life = Nvme("Life"); var used = Nvme("Percentage Used"); var spare = Nvme("Available Spare"); var spareLimit = Nvme("Available Spare Threshold");
        if (life != null || spare != null)
            checks.Add(new("Resistencia y reserva SMART NVMe", life < 10 || (spare != null && spareLimit != null && spare < spareLimit) ? "atención" : "informado", $"Resistencia restante estimada: {life?.ToString("F0") ?? "no disponible"} %. Uso de resistencia: {used?.ToString("F0") ?? "no disponible"} %. Reserva: {spare?.ToString("F0") ?? "no disponible"} %; umbral: {spareLimit?.ToString("F0") ?? "no disponible"} %. Lecturas del controlador; no predicen todos los posibles fallos."));
        var temps = s.Sensors.Where(TemperatureRules.IsActual).ToList();
        checks.Add(new("Sensores térmicos", temps.Count == 0 ? "no disponible" : "informado", temps.Count == 0 ? "No hay lecturas térmicas accesibles. No se puede evaluar la temperatura." : string.Join("; ", temps.Select(t => $"{t.Name}: {t.Value:F1} °C")) + ". Muestra puntual, sin prueba de estrés."));
        try
        {
            var query = "*[System[TimeCreated[timediff(@SystemTime) <= 604800000] and ((Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or Provider[@Name='Microsoft-Windows-WHEA-Logger'] or (Provider[@Name='disk'] and (EventID=7 or EventID=51 or EventID=153)) or (Provider[@Name='stornvme'] and EventID=129))]]";
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query) { ReverseDirection = true });
            for (int i = 0; i < 100; i++) { using var entry = reader.ReadEvent(); if (entry == null) break; events.Add(new { time = entry.TimeCreated, provider = entry.ProviderName, id = entry.Id, level = entry.LevelDisplayName }); }
            checks.Add(new("Eventos de hardware · últimos 7 días", events.Count > 0 ? "revisar" : "consultado", events.Count == 0 ? "No se encontraron eventos de las categorías consultadas: Kernel-Power 41, WHEA, disco 7/51/153 y stornvme 129." : $"Se encontraron {events.Count}{(events.Count == 100 ? " o más" : "")} eventos en las categorías consultadas. Se guardan los 100 más recientes. Un evento no determina por sí solo la causa de un fallo."));
        }
        catch (Exception ex) { checks.Add(new("Eventos de hardware · últimos 7 días", "no disponible", ex.Message)); }
        var plan = await PowerCfg("/getactivescheme");
        checks.Add(new("Plan de energía", plan.Exit == 0 ? "informado" : "no disponible", plan.Text.Trim()));
        checks.Add(new("Alcance del diagnóstico", "informado", "Revisión de lecturas, batería, espacio, estado expuesto del disco y eventos de Windows. No ejecuta benchmarks, descargas completas de batería, pruebas de RAM que reinicien el equipo ni reparaciones de archivos."));
        var report = new DiagnosticReport(id, DateTimeOffset.Now, checks, battery, events, folder, batteryHtml);
        await File.WriteAllTextAsync(Path.Combine(folder, "diagnostico.json"), JsonSerializer.Serialize(report, Program.Json));
        await File.WriteAllTextAsync(Path.Combine(folder, "diagnostico.html"), Html(report), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(root, "last-diagnostic.json"), JsonSerializer.Serialize(report, Program.Json));
        return report;
    }
    static async Task<(int Exit, string Text)> PowerCfg(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); return (-1, "La consulta excedió el tiempo disponible."); }
            return (process.ExitCode, (await stdout) + " " + (await stderr));
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
    static string Html(DiagnosticReport r)
    {
        string E(object? v) => WebUtility.HtmlEncode(Convert.ToString(v, CultureInfo.CurrentCulture)) ?? "";
        var html = new StringBuilder("<!doctype html><html lang='es'><meta charset='utf-8'><title>Pulse · Diagnóstico</title><style>body{font:15px Segoe UI,sans-serif;background:#10161c;color:#e3e9ec;max-width:900px;margin:40px auto;padding:24px}h1{color:#c5ec9c}section{padding:16px 0;border-bottom:1px solid #35414a}p{line-height:1.7;color:#b8c6cf}.status{font-size:12px;color:#efcf87}a{color:#99c6fa}pre{white-space:pre-wrap;font:12px Consolas,monospace}</style><h1>Pulse / Diagnóstico del equipo</h1>");
        html.Append("<p>").Append(E(r.Time.ToString("f"))).Append(" · Revisión sin pruebas de estrés.</p>");
        foreach (var c in r.Checks) html.Append("<section><span class='status'>").Append(E(c.Status)).Append("</span><h2>").Append(E(c.Name)).Append("</h2><p>").Append(E(c.Detail)).Append("</p></section>");
        if (r.BatteryReport != null) html.Append("<p><a href='bateria-windows.html'>Abrir informe original de batería de Windows</a></p>");
        html.Append("<h2>Eventos consultados</h2><pre>").Append(E(JsonSerializer.Serialize(r.Events, new JsonSerializerOptions { WriteIndented = true }))).Append("</pre></html>"); return html.ToString();
    }
}
