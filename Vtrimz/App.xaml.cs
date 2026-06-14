using System.IO;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Vtrimz;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"An error occurred:\n\n{args.Exception.Message}",
                "VTRIMZ Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            InitializeLibVlc();
            base.OnStartup(e);

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start VTRIMZ:\n\n{ex.Message}\n\nMake sure the entire app folder is copied (including libvlc).",
                "VTRIMZ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void InitializeLibVlc()
    {
        var arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        var vlcPath = Path.Combine(AppContext.BaseDirectory, "libvlc", arch);

        if (Directory.Exists(vlcPath) && File.Exists(Path.Combine(vlcPath, "libvlc.dll")))
            Core.Initialize(vlcPath);
        else
            Core.Initialize();
    }
}
