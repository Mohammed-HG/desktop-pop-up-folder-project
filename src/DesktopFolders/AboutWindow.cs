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
        var name = Ui.Text(AppName, 26);
        name.FontWeight = FontWeights.SemiBold;
        body.Children.Add(name);
        body.Children.Add(Ui.Text($"{L("Version", "الإصدار")} {DisplayVersion}"));
        var summary = Ui.Text(L(
            "Open your apps and files from a floating folder on the Windows desktop.",
            "افتح تطبيقاتك وملفاتك من مجلد منبثق على سطح مكتب Windows."));
        summary.Margin = new(0, 12, 0, 12);
        body.Children.Add(summary);
        body.Children.Add(Ui.Text("© 2026 Mohammed-HG"));
        AddLink(L("Mohammed-HG on GitHub", "حساب Mohammed-HG على GitHub"), ProfileUrl);
        AddLink(L("Program source code on GitHub", "الشيفرة المصدرية على GitHub"), SourceUrl);
        var close = Ui.Button(L("Close", "إغلاق"), Close);
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
            var line = new TextBlock(link) { Margin = new(0, 8, 0, 0) };
            body.Children.Add(line);
        }
    }
}
