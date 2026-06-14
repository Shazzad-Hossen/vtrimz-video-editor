using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Vtrimz.Helpers;

namespace Vtrimz.Views;

public partial class AboutDialog : Window
{
    private static readonly (string Tooltip, string Url, Geometry Icon)[] Links =
    [
        ("Website", "https://shazzad-hossen.vercel.app/", SocialIconPaths.Website),
        ("GitHub", "https://github.com/Shazzad-Hossen", SocialIconPaths.GitHub),
        ("LinkedIn", "https://www.linkedin.com/in/shazzad-srv", SocialIconPaths.LinkedIn),
        ("Facebook", "https://www.facebook.com/sboy.showrav", SocialIconPaths.Facebook),
        ("Instagram", "https://www.instagram.com/shazzad.srv", SocialIconPaths.Instagram),
        ("Email", "mailto:shazzad.srv@gmail.com", SocialIconPaths.Email)
    ];

    public AboutDialog(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        Loaded += (_, _) => LoadContent();
    }

    private void LoadContent()
    {
        var photoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "asset", "dev.png");
        if (File.Exists(photoPath))
            DeveloperImage.Source = new BitmapImage(new Uri(photoPath));

        foreach (var (tooltip, url, icon) in Links)
            LinksPanel.Children.Add(CreateIconButton(tooltip, url, icon));
    }

    private Button CreateIconButton(string tooltip, string url, Geometry icon)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = icon,
            Stretch = Stretch.Uniform,
            Width = 20,
            Height = 20,
            Fill = (Brush)FindResource("TextPrimaryBrush"),
            IsHitTestVisible = false
        };

        var button = new Button
        {
            Content = path,
            ToolTip = tooltip,
            Tag = url,
            Style = (Style)FindResource("SocialIconButton")
        };

        button.Click += (_, _) => OpenUrl(url);
        button.MouseEnter += (_, _) => path.Fill = Brushes.White;
        button.MouseLeave += (_, _) => path.Fill = (Brush)FindResource("TextPrimaryBrush");
        return button;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
