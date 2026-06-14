using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Vtrimz.Views;

namespace Vtrimz;

public partial class MainWindow : Window
{
    private readonly LibVLC _libVlc;
    private EditorView? _editorView;

    public MainWindow()
    {
        InitializeComponent();

        _libVlc = new LibVLC(
            "--intf=dummy",
            "--no-video-title-show",
            "--avcodec-hw=any",
            "--file-caching=300",
            "--live-caching=300");

        ShowSplash();
    }

    private void ShowSplash()
    {
        ContentHost.Content = new SplashView();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ShowImport();
        };
        timer.Start();
    }

    private void ShowImport()
    {
        var import = new ImportView();
        import.VideoSelected += OnVideoSelected;
        ContentHost.Content = import;
    }

    private void OnVideoSelected(object? sender, string path)
    {
        _editorView?.Dispose();
        _editorView = new EditorView(_libVlc);
        _editorView.LoadVideo(path);
        ContentHost.Content = _editorView;
    }

    protected override void OnClosed(EventArgs e)
    {
        _editorView?.Dispose();
        _libVlc.Dispose();
        base.OnClosed(e);
    }
}
