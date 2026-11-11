using System;
using System.Windows;
using System.Windows.Controls;
using DesktopFolders.Core;

namespace DesktopFolders;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(Manager owner, App app)
    {
        Owner = owner;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Build(app);
    }

    private void Build(App app)
    {
        var words = app.Words;
        string L(string en, string ar) => words.Get(en, ar);
        Ui.SetWindow(this, words, L("Settings", "الإعدادات"));
        var body = new StackPanel { Margin = new(24) };
        var title = Ui.Text(Title, 27);
        title.FontWeight = FontWeights.SemiBold;
        body.Children.Add(title);
        var controls = new StackPanel();
        body.Children.Add(new Border { Background = Ui.Card, CornerRadius = new(12), Padding = new(16),
            Margin = new(0, 15, 0, 12), Child = controls });

        var theme = new ComboBox
        {
            ItemsSource = new[] { L("Follow Windows", "مظهر ويندوز الحالي"), L("Light", "فاتح"), L("Dark", "داكن") },
            SelectedIndex = Enum.IsDefined(app.Data.Appearance) ? (int)app.Data.Appearance : 0
        };
        Add(L("Appearance", "المظهر"), theme);
        var language = new ComboBox { ItemsSource = new[] { "English", "العربية" }, SelectedIndex = app.Data.Arabic ? 1 : 0 };
        Add(L("Language", "اللغة"), language);
        var startup = new CheckBox { Content = L("Start with Windows", "البدء مع تشغيل ويندوز"), IsChecked = app.Data.StartWithWindows,
            Margin = new(0, 8, 0, 8) };
        controls.Children.Add(startup);
        var desktop = new CheckBox { Content = L("Desktop right-click command", "أمر القائمة السياقية لسطح المكتب"),
            IsChecked = WindowsServices.HasDesktopMenu() };
        controls.Children.Add(desktop);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Ui.Ok(Close));
        body.Children.Add(actions);
        Content = body;
        theme.SelectionChanged += (_, _) => Apply(refresh: true);
        language.SelectionChanged += (_, _) => Apply(refresh: true);
        startup.Click += (_, _) => Apply(refresh: false);
        desktop.Click += (_, _) => Apply(refresh: false);

        void Apply(bool refresh)
        {
            if (theme.SelectedIndex < 0 || language.SelectedIndex < 0) return;
            var applied = app.ApplySettings((AppAppearance)theme.SelectedIndex, language.SelectedIndex == 1,
                startup.IsChecked == true, desktop.IsChecked == true);
            if (refresh || !applied) Dispatcher.BeginInvoke(() => { if (IsVisible) Build(app); });
        }

        void Add(string label, FrameworkElement control)
        {
            controls.Children.Add(Ui.Text(label));
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            control.Margin = new(0, 4, 0, 10);
            controls.Children.Add(control);
        }
    }
}
