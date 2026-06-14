using System.IO;
using System.Windows;
using System.Windows.Threading;
using Vtrimz.Helpers;
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
                $"Failed to start VTRIMZ:\n\n{ex.Message}",
                "VTRIMZ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void InitializeLibVlc()
    {
        var vlcPath = AppPaths.FindLibVlcDirectory();
        if (vlcPath != null)
            Core.Initialize(vlcPath);
        else
            Core.Initialize();
    }
}
