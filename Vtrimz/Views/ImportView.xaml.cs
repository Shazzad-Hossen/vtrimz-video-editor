using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Vtrimz.Views;

public partial class ImportView : UserControl
{
    public event EventHandler<string>? VideoSelected;

    private static readonly string VideoFilter =
        "All Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpeg;*.mpg;*.3gp;*.ts;*.mts;*.m2ts;*.ogv;*.vob|" +
        "MP4 (*.mp4)|*.mp4|MKV (*.mkv)|*.mkv|AVI (*.avi)|*.avi|MOV (*.mov)|*.mov|WMV (*.wmv)|*.wmv|WebM (*.webm)|*.webm|" +
        "All Files (*.*)|*.*";

    public ImportView()
    {
        InitializeComponent();
    }

    private void SelectVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Video",
            Filter = VideoFilter,
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        SelectedFileText.Text = Path.GetFileName(dialog.FileName);
        VideoSelected?.Invoke(this, dialog.FileName);
    }
}
