using System.Windows;
using BeastScreen.Core.Playback;
using BeastScreen.Desktop.Services;

namespace BeastScreen.Desktop;

public partial class App : Application
{
    private SingleInstance? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--check-data"))
        {
            try
            {
                var path = Path.Combine(ViewModels.PlayerViewModel.DataDirectory, "library.db");
                if (File.Exists(path))
                {
                    using var c = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString());
                    c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version";
                    if (Convert.ToInt32(cmd.ExecuteScalar()) > Core.Library.LibraryDatabase.SchemaVersion) { Shutdown(30); return; }
                }
                Shutdown(0);
            }
            catch { Shutdown(31); }
            return;
        }
        if (e.Args.Contains("--health-check"))
        {
            try { using var engine = new MpvEngine(); engine.Initialize(0, new Dictionary<string, string> { ["vo"] = "null", ["ao"] = "null", ["hwdec"] = "no" }); Shutdown(0); }
            catch { Shutdown(20); }
            return;
        }
        var smoke = e.Args.Contains("--smoke-test");
        if (!smoke)
        {
            instance = new();
            if (!instance.IsPrimary)
            {
                try { await instance.ForwardAsync(e.Args); Shutdown(0); }
                catch { MessageBox.Show("BeastScreen is already running but did not respond. Close it and try again.", "BeastScreen"); Shutdown(2); }
                return;
            }
        }
        try
        {
            var window = new MainWindow(smoke); MainWindow = window; window.Show();
            if (smoke) await window.RunSmokeAsync(e.Args);
            else
            {
                _ = instance!.ListenAsync(paths => Dispatcher.InvokeAsync(() => { window.Activate(); window.OpenSources(paths); }));
                window.OpenSources(e.Args);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(BeastScreen.Core.Security.Redaction.Clean(ex.Message), "BeastScreen could not start"); Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
