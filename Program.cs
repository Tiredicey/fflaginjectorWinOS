using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace FlagInjector;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName         = Environment.ProcessPath
                                       ?? Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    Verb             = "runas",
                    UseShellExecute  = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                    Arguments        = string.Join(" ",
                        args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode != 1223)
            {
                MessageBox.Show($"Elevation failed: {ex.Message}", "Error");
            }
            return;
        }

        if (!Environment.Is64BitProcess)
        {
            MessageBox.Show("64-bit build required.", "Architecture Mismatch");
            return;
        }

        using var mtx = new Mutex(true,
            $"Local\\FlagInjectorCS_{Environment.UserName}", out bool created);
        if (!created) { MessageBox.Show("Already running."); return; }

        var settings = AppSettings.Load();
        Theme.Set(settings.DarkTheme);

        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; }
        catch { }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SplashForm? splash = null;
        try { splash = new SplashForm(); splash.Show(); Thread.Sleep(0); }
        catch (Exception ex) { Debug.WriteLine($"[Splash] {ex}"); }

        if (!settings.DisclaimerShown)
        {
            settings.DisclaimerShown = true;
            settings.Save();
            using var disclaimer = new DisclaimerOverlay();
            disclaimer.ShowDialog();
        }

        var form = new MainForm(args);
        form.Shown += (_, _) =>
        {
            try { splash?.RequestClose(); } catch { }
        };
        Application.Run(form);
    }
}