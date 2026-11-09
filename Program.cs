using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Pulse;

static class Program
{
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseWidget");
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    internal static string AppVersion => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.2.0";
    [STAThread]
    static void Main(string[] args)
    {
        Directory.CreateDirectory(DataDir);
        if (args.Length == 2 && args[0] == "--test-storage") { StorageTests.Run(args[1]); return; }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => File.AppendAllText(Path.Combine(DataDir, "errors.log"), $"{DateTime.Now:o} {e.Exception}\n");
        if (args.Contains("--quit"))
        {
            foreach (var p in Process.GetProcessesByName("Pulse"))
                try { if (p.Id != Environment.ProcessId && p.MainModule?.FileName == Environment.ProcessPath) PostMessage(p.MainWindowHandle, 0x8001, 0, 0); } catch { }
            return;
        }
        if (args.Contains("--install") || Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("Instalar Pulse", StringComparison.OrdinalIgnoreCase))
        {
            try { Install(); }
            catch (Exception ex) { MessageBox.Show("No se pudo instalar Pulse: " + ex.Message, "Pulse"); }
            return;
        }
        if (args.Contains("--diagnose"))
        {
            try
            {
                using var telemetry = new Telemetry();
                telemetry.Collect(); Thread.Sleep(2000);
                File.WriteAllText(Path.Combine(DataDir, "diagnostics.json"), JsonSerializer.Serialize(telemetry.Collect(), Json));
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(DataDir, "diagnostics-error.txt"), ex.ToString()); }
            return;
        }
        if (args.Contains("--run-diagnostics"))
        {
            try
            {
                using var telemetry = new Telemetry(); telemetry.Collect(); Thread.Sleep(2000);
                var sample = telemetry.Collect();
                new HistoryStore(DataDir).Save(sample, true);
                Diagnostics.Run(sample, DataDir).GetAwaiter().GetResult();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(DataDir, "diagnostic-run-error.txt"), ex.ToString()); }
            return;
        }
        if (args.Contains("--export-history"))
        {
            new HistoryStore(DataDir).ExportCsv(Path.Combine(DataDir, "exportaciones")); return;
        }
        using var mutex = new Mutex(true, "Local\\PulseWidget-Desktop", out var first);
        if (!first)
        {
            bool handedOver = false;
            if (args.Contains("--elevated"))
                try { handedOver = mutex.WaitOne(TimeSpan.FromSeconds(15)); } catch (AbandonedMutexException) { handedOver = true; }
            if (!handedOver) { MessageBox.Show("Pulse ya está abierto. Búscalo junto al reloj de Windows.", "Pulse"); return; }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new Widget());
    }
    [DllImport("user32.dll")] static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);
    static void Install()
    {
        var source = Path.GetFullPath(AppContext.BaseDirectory);
        var target = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PulseWidget"));
        var installedExe = Path.Combine(target, "Pulse.exe");
        foreach (var running in Process.GetProcessesByName("Pulse"))
        {
            if (running.Id == Environment.ProcessId) continue;
            string? runningPath;
            try { runningPath = running.MainModule?.FileName; }
            catch { throw new IOException("Hay una instancia de Pulse con permisos de administrador. Ejecuta el instalador como administrador para actualizarla."); }
            if (string.Equals(runningPath, installedExe, StringComparison.OrdinalIgnoreCase))
            {
                PostMessage(running.MainWindowHandle, 0x8001, 0, 0);
                if (!running.WaitForExit(15000)) throw new IOException("Cierra Pulse antes de actualizarlo.");
            }
        }
        if (!source.TrimEnd(Path.DirectorySeparatorChar).Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative.Equals("Instalar Pulse.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var destination = Path.GetFullPath(Path.Combine(target, relative));
                if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ruta de instalación inválida");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }
        }
        var app = Path.Combine(target, "Pulse.exe");
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs") })
        {
            Directory.CreateDirectory(folder);
            dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, "Pulse.lnk"));
            shortcut.TargetPath = app; shortcut.WorkingDirectory = target; shortcut.Description = "Pulse · Monitor de CPU, RAM, SSD, GPU, batería y sensores"; shortcut.IconLocation = app + ",0"; shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        Marshal.FinalReleaseComObject(shell);
        File.WriteAllText(Path.Combine(DataDir, "installation.txt"), target);
        Process.Start(new ProcessStartInfo(app) { UseShellExecute = true, WorkingDirectory = target, WindowStyle = ProcessWindowStyle.Normal });
    }
}

sealed class Preferences
{
    public bool Pinned { get; set; } = false;
    public bool Expanded { get; set; }
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
}

sealed class Widget : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(15, 19, 24) };
    readonly NotifyIcon tray;
    readonly CancellationTokenSource stop = new();
    readonly Preferences prefs;
    Telemetry? telemetry;
    Task? worker;
    HistoryStore? historyStore;
    Snapshot? latestSample;
    DiagnosticReport? lastDiagnostic;
    bool diagnosticRunning;
    bool initialDiagnosticRequested;
    bool ready, quitting;
    static string PrefFile => Path.Combine(Program.DataDir, "preferences.json");
    readonly string exe = Environment.ProcessPath!;

    public Widget()
    {
        try { prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PrefFile)) ?? new(); } catch { prefs = new(); }
        Text = "Pulse · Monitor del sistema";
        BackColor = Color.FromArgb(15, 19, 24);
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new(390, 510);
        ClientSize = prefs.Expanded ? new(810, 760) : new(430, 700);
        Icon = Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;
        TopMost = prefs.Pinned;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = prefs.X >= 0 ? new(Math.Clamp(prefs.X, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(prefs.Y, area.Top, Math.Max(area.Top, area.Bottom - Height))) : new(area.Right - Width - 22, area.Top + 32);
        Controls.Add(web);
        var grip = new Panel { Size = new(13, 13), BackColor = Color.FromArgb(48, 60, 68), Cursor = Cursors.SizeNWSE, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Location = new(ClientSize.Width - 14, ClientSize.Height - 14) };
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 17, 0); } };
        Controls.Add(grip); grip.BringToFront();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostrar Pulse", null, (_, _) => ShowWidget());
        menu.Items.Add("Salir", null, (_, _) => Quit());
        tray = new NotifyIcon { Icon = Icon, Text = "Pulse · Monitor del sistema", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowWidget();
        Shown += async (_, _) => { SetWidgetSize(); ClampPosition(); await Initialize(); };
        FormClosing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); Save(); } };
    }
    async Task Initialize()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDir, "WebView"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("pulse.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://pulse.local/", StringComparison.Ordinal)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += (_, e) =>
            {
                if (!e.Source.StartsWith("https://pulse.local/", StringComparison.Ordinal)) return;
                try { Command(e.TryGetWebMessageAsString()); } catch (Exception ex) { Log(ex); }
            };
            core.Navigate("https://pulse.local/index.html");
            worker = Task.Run(async () =>
            {
                try
                {
                    telemetry = new Telemetry();
                    try { historyStore = new HistoryStore(Program.DataDir); } catch (Exception ex) { Log(ex); }
                    while (!stop.IsCancellationRequested)
                    {
                        var sample = telemetry.Collect();
                        Volatile.Write(ref latestSample, sample);
                        if (!initialDiagnosticRequested && IsHandleCreated)
                        {
                            initialDiagnosticRequested = true;
                            BeginInvoke(() =>
                            {
                                var marker = Path.Combine(Program.DataDir, "diagnostic-version.txt");
                                if (!File.Exists(marker) || File.ReadAllText(marker) != Program.AppVersion) RunDiagnostics();
                            });
                        }
                        try { historyStore?.Save(sample); }
                        catch (Exception ex) { Log(ex); if (IsHandleCreated) BeginInvoke(() => Post(new { type = "storageError", message = "No se pudo guardar el historial. Las lecturas en vivo continúan." })); }
                        var json = JsonSerializer.Serialize(new { type = "sample", data = sample }, Program.Json);
                        if (!IsDisposed && IsHandleCreated) BeginInvoke(() => { if (ready) core.PostWebMessageAsJson(json); });
                        // Overwrite a single local snapshot only every 30 s; no growing log.
                        if (DateTime.Now.Second % 30 < 2) File.WriteAllText(Path.Combine(Program.DataDir, "latest.json"), JsonSerializer.Serialize(sample, Program.Json));
                        await Task.Delay(2000, stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log(ex); if (IsHandleCreated) BeginInvoke(() => core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "error", message = "No se pudieron consultar las métricas. Reinicia Pulse." }))); }
                finally { telemetry?.Dispose(); }
            });
        }
        catch (Exception ex) { Log(ex); MessageBox.Show("No se pudo abrir Pulse: " + ex.Message, "Pulse"); Quit(); }
    }
    void Command(string command)
    {
        switch (command)
        {
            case "ready": ready = true; SendPrefs(); LoadLastDiagnostic(); break;
            case "drag": ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); break;
            case "pin": TopMost = !TopMost; prefs.Pinned = TopMost; Save(); SendPrefs(); break;
            case "expand": prefs.Expanded = !prefs.Expanded; SetWidgetSize(); ClampPosition(); Save(); SendPrefs(); break;
            case "hide": Hide(); Save(); break;
            case "quit": Quit(); break;
            case "startup": SetStartup(!StartupEnabled()); SendPrefs(); break;
            case "admin": ActivateSensors(); break;
            case "diagnostics": RunDiagnostics(); break;
            case "export": ExportHistory(); break;
            case "open-reports": OpenFolder(Path.Combine(Program.DataDir, "diagnosticos")); break;
            case "open-data": OpenFolder(Program.DataDir); break;
            case "report":
                if (lastDiagnostic != null) Process.Start(new ProcessStartInfo(Path.Combine(lastDiagnostic.Folder, "diagnostico.html")) { UseShellExecute = true });
                break;
        }
        if (command is "history:1h" or "history:24h" or "history:7d" or "history:30d") LoadHistory(command[8..]);
        if (command is "view:history" or "view:diagnostics")
        {
            if (!prefs.Expanded) { prefs.Expanded = true; SetWidgetSize(); ClampPosition(); Save(); SendPrefs(); }
            Post(new { type = "view", view = command[5..] });
            if (command == "view:history") LoadHistory("24h");
        }
    }
    void Post(object value) { if (ready && !IsDisposed && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, Program.Json)); }
    void LoadLastDiagnostic()
    {
        try { lastDiagnostic = JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(Path.Combine(Program.DataDir, "last-diagnostic.json")), Program.Json); if (lastDiagnostic != null) Post(new { type = "diagnostic", data = lastDiagnostic }); } catch { }
    }
    async void LoadHistory(string range)
    {
        try { var store = historyStore; if (store == null) { Post(new { type = "historyPending" }); return; } var data = await Task.Run(() => store.Query(range)); Post(new { type = "history", data }); }
        catch (Exception ex) { Log(ex); Post(new { type = "storageError", message = "No se pudo consultar el historial." }); }
    }
    async void ExportHistory()
    {
        try
        {
            var store = historyStore; if (store == null) return;
            var path = await Task.Run(() => store.ExportCsv(Path.Combine(Program.DataDir, "exportaciones")));
            Post(new { type = "exported", file = path }); OpenFolder(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex) { Log(ex); Post(new { type = "storageError", message = "No se pudo exportar el historial." }); }
    }
    async void RunDiagnostics()
    {
        if (diagnosticRunning) return;
        var sample = Volatile.Read(ref latestSample);
        if (sample == null) { Post(new { type = "storageError", message = "Espera a que aparezca la primera lectura." }); return; }
        diagnosticRunning = true; Post(new { type = "diagnosticRunning" });
        try { lastDiagnostic = await Task.Run(() => Diagnostics.Run(sample, Program.DataDir)); File.WriteAllText(Path.Combine(Program.DataDir, "diagnostic-version.txt"), Program.AppVersion); Post(new { type = "diagnostic", data = lastDiagnostic }); }
        catch (Exception ex) { Log(ex); Post(new { type = "diagnosticError", message = "No se pudo completar el diagnóstico: " + ex.Message }); }
        finally { diagnosticRunning = false; }
    }
    static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { path } }); }
    bool activatingSensors;
    async void ActivateSensors()
    {
        if (activatingSensors) return;
        activatingSensors = true;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            if (key == null)
            {
                var installer = Path.Combine(AppContext.BaseDirectory, "vendor", "PawnIO_setup.exe");
                if (!File.Exists(installer))
                {
                    var downloads = Path.Combine(Program.DataDir, "downloads"); Directory.CreateDirectory(downloads);
                    installer = Path.Combine(downloads, "PawnIO-2.2.0.exe");
                    if (!File.Exists(installer))
                    {
                        Post(new { type = "notice", message = "Descargando el controlador oficial PawnIO. Después Windows solicitará confirmación." });
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                        var bytes = await client.GetByteArrayAsync("https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe");
                        var downloadedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                        if (downloadedHash != "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032") throw new IOException("La descarga no coincide con la versión verificada de PawnIO.");
                        await File.WriteAllBytesAsync(installer, bytes);
                    }
                }
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(installer)));
                if (hash != "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032") throw new IOException("El instalador del controlador no coincide con la versión verificada.");
                using var setup = Process.Start(new ProcessStartInfo(installer, "-install -silent") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                if (setup == null) return;
                await setup.WaitForExitAsync();
                if (setup.ExitCode != 0) throw new IOException("Windows no pudo instalar el controlador de sensores. Código: " + setup.ExitCode);
            }
            var p = Process.Start(new ProcessStartInfo(exe, "--elevated") { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory });
            if (p != null) Quit();
        }
        catch (System.ComponentModel.Win32Exception) { /* Declined UAC: preserve the running standard widget. */ }
        catch (Exception ex) { Log(ex); MessageBox.Show(ex.Message, "Pulse · Sensores"); }
        finally { activatingSensors = false; }
    }
    bool StartupEnabled() { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("PulseWidget") is string; }
    void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("PulseWidget", "\"" + exe + "\""); else key.DeleteValue("PulseWidget", false);
    }
    void SendPrefs() => web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "prefs", pinned = TopMost, expanded = prefs.Expanded, startup = StartupEnabled() }, Program.Json));
    void SetWidgetSize()
    {
        var scale = DeviceDpi / 96.0;
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new(Math.Min((int)(390 * scale), area.Width - 12), Math.Min((int)(510 * scale), area.Height - 12));
        ClientSize = new(Math.Min((int)((prefs.Expanded ? 810 : 430) * scale), area.Width - 16), Math.Min((int)((prefs.Expanded ? 760 : 700) * scale), area.Height - 16));
    }
    void ClampPosition() { var a = Screen.FromControl(this).WorkingArea; Location = new(Math.Clamp(Left, a.Left, Math.Max(a.Left, a.Right - Width)), Math.Clamp(Top, a.Top, Math.Max(a.Top, a.Bottom - Height))); }
    void ShowWidget() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    void Save() { prefs.X = Left; prefs.Y = Top; File.WriteAllText(PrefFile, JsonSerializer.Serialize(prefs)); }
    void Quit() { Save(); quitting = true; stop.Cancel(); try { worker?.Wait(TimeSpan.FromSeconds(4)); } catch { } tray.Visible = false; Close(); }
    protected override void Dispose(bool disposing) { if (disposing) { stop.Cancel(); tray.Dispose(); web.Dispose(); } base.Dispose(disposing); }
    static void Log(Exception ex) { try { File.AppendAllText(Path.Combine(Program.DataDir, "errors.log"), $"{DateTime.Now:o} {ex}\n"); } catch { } }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x8001) { Quit(); return; }
        base.WndProc(ref m);
        if (m.Msg == 0x84 && (int)m.Result == 1)
        {
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            if (p.X >= Width - 12 && p.Y >= Height - 12) m.Result = 17;
        }
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern nint SendMessage(nint handle, int msg, nint wParam, nint lParam);
}
