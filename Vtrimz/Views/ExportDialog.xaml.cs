using System.Windows;

namespace Vtrimz.Views;

public partial class ExportDialog : Window
{
    public ExportDialog(Window owner)
    {
        Owner = owner;
        InitializeComponent();
    }

    public void UpdateStatus(string message) =>
        StatusText.Text = message;
}
