using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DesktopFolders;

internal sealed class AboutWindow : Window
{
    public const string AppName = "Desktop Pop-up folder";
    public const string ProfileUrl = "https://github.com/Mohammed-HG";
    public const string SourceUrl = ProfileUrl + "/Win-desktop-modern-folder";
    public static string DisplayVersion =>
        typeof(AboutWindow).Assembly.GetName().Version?.ToString(2) ?? "";

    public AboutWindow(Words words)
    {
        string L(string en, string ar) => words.Get(en, ar);
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.SetWindow(this, words, L("About", "حول التطبيق"));
        var body = new StackPanel { Margin = new(24) };
        var identity = new StackPanel { Orientation = Orientation.Horizontal };
        var appIcon = new Image { Source = WindowsServices.FolderIcon, Width = 40, Height = 40 };
        identity.Children.Add(new Border { Child = appIcon, Background = Ui.Card, Padding = new(8),
            CornerRadius = new(14), Margin = new(0, 0, 12, 0) });
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(AppName, 24);
        name.FontWeight = FontWeights.SemiBold;
        details.Children.Add(name);
        var version = Ui.Text($"{L("Version", "الإصدار")} {DisplayVersion}", 13);
        version.Foreground = Ui.Accent;
        details.Children.Add(version);
        identity.Children.Add(details);
        body.Children.Add(identity);
        var summary = Ui.Text(L(
            "Open your apps and files from a floating folder on the Windows desktop.",
            "افتح تطبيقاتك وملفاتك من مجلد منبثق على سطح مكتب Windows."));
        summary.Margin = new(0, 12, 0, 12);
        body.Children.Add(summary);
        var copyright = Ui.Text("© 2026 Mohammed-HG", 12);
        copyright.Foreground = Ui.Muted;
        body.Children.Add(copyright);
        AddLink(L("Mohammed-HG on GitHub", "حساب Mohammed-HG على GitHub"), ProfileUrl);
        AddLink(L("Program source code on GitHub", "الشيفرة المصدرية على GitHub"), SourceUrl);
        var close = Ui.Button(Glyph.Close, L("Close", "إغلاق"), Close);
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new(0, 12, 0, 0);
        body.Children.Add(close);
        Content = body;

        void AddLink(string text, string url)
        {
            var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
            link.RequestNavigate += (_, e) =>
            {
                e.Handled = true;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
                catch (Exception error)
                {
                    WindowsServices.Log("open-about-link", error);
                    MessageBox.Show(this, L("Could not open your web browser.", "تعذر فتح متصفح الويب."));
                }
            };
            link.Foreground = Ui.Accent;
            var line = new DockPanel();
            var glyph = Ui.Icon(Glyph.Link, 16);
            glyph.Foreground = Ui.Accent;
            glyph.Margin = new(0, 0, 10, 0);
            line.Children.Add(glyph);
            line.Children.Add(new TextBlock(link));
            body.Children.Add(new Border { Child = line, Background = Ui.Card, Padding = new(12),
                CornerRadius = new(9), Margin = new(0, 8, 0, 0) });
        }
    }
}
