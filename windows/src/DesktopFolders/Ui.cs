using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopFolders.Core;
using Windows.UI.ViewManagement;

namespace DesktopFolders;

internal sealed class Words(bool arabic)
{
    public string Get(string en, string ar) => arabic ? ar : en;
    public FlowDirection Direction => arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}

internal static class Ui
{
    internal static UISettings WindowsColors { get; } = new();
    private static AppAppearance appearance;
    private static bool Dark => appearance == AppAppearance.Dark || (appearance == AppAppearance.Windows && WindowsDark);
    private static bool WindowsDark => IsLight(WindowsColor(UIColorType.Foreground));
    public static Brush Ink => Brush(Dark ? "#F4F4F5" : "#1B1B1F");
    public static Brush Muted => Brush(Dark ? "#C3C3C9" : "#52525A");
    public static Brush Surface => Brush(Dark ? "#202020" : "#F3F3F3");
    public static Brush Card => Brush(Dark ? "#2B2B2B" : "#FFFFFF");
    public static Color AccentColor => WindowsColor(UIColorType.Accent);
    public static Brush Accent => new SolidColorBrush(WindowsColor(Dark ? UIColorType.AccentLight2 : UIColorType.AccentDark2));
    public static Color WindowsPanelColor => (Brush(WindowsDark ? "#202020" : "#F3F3F3")).Color;
    public static Color WindowsPanelInk => (Brush(WindowsDark ? "#F4F4F5" : "#1B1B1F")).Color;
    public static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    public static void Configure(AppAppearance selected)
    {
        appearance = Enum.IsDefined(selected) ? selected : AppAppearance.Windows;
#pragma warning disable WPF0001
        Application.Current.ThemeMode = appearance switch
        {
            AppAppearance.Dark => ThemeMode.Dark,
            AppAppearance.Light => ThemeMode.Light,
            _ => ThemeMode.System
        };
#pragma warning restore WPF0001
    }

    private static Color WindowsColor(UIColorType type)
    {
        var color = WindowsColors.GetColorValue(type);
        return Color.FromRgb(color.R, color.G, color.B);
    }

    private static bool IsLight(Color color) => (5 * color.G + 2 * color.R + color.B) > 8 * 128;

    public static void SetWindow(Window window, Words words, string title)
    {
        window.Title = title;
        window.FontFamily = new("Segoe UI");
        window.FontSize = 14;
        window.Foreground = Ink;
        window.Background = Surface;
        window.FlowDirection = words.Direction;
        window.Icon = WindowsServices.FolderIcon;
    }

    public static TextBlock Icon(string glyph, double size = 18) => new()
    {
        Text = glyph, FontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = size, FlowDirection = FlowDirection.LeftToRight
    };

    public static Button Button(string glyph, string label, Action action, bool primary = false)
    {
        var accent = AccentColor;
        var button = new Button
        {
            Content = Icon(glyph), Padding = new(9, 6, 9, 6), Margin = new(0, 0, 6, 6),
            MinWidth = 38, MinHeight = 36, ToolTip = label,
            Foreground = primary ? (IsLight(accent) ? Brushes.Black : Brushes.White) : Accent,
            Background = primary ? new SolidColorBrush(accent) : Card,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    public static Button Ok(Action action)
    {
        var button = Button(Glyph.Check, "OK", action, true);
        button.Content = "OK";
        button.MinWidth = 68;
        button.IsDefault = true;
        return button;
    }

    public static Button CornerClose(string label, Action action, Brush foreground)
    {
        var button = Button(Glyph.Close, label, action);
        button.Background = Brushes.Transparent;
        button.Foreground = foreground;
        button.BorderThickness = new(0);
        button.MinWidth = button.MinHeight = 29;
        button.Padding = new(5);
        button.Margin = new(0);
        var chrome = new FrameworkElementFactory(typeof(ContentPresenter));
        chrome.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        chrome.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var surface = new FrameworkElementFactory(typeof(Border));
        // A single alpha level keeps the whole hit area clickable on a layered window.
        surface.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
        surface.AppendChild(chrome);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        return button;
    }

    public static TextBlock Text(string value, double size = 14) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap
    };

    public static TextBox Field(string value) => new() { Text = value, Padding = new(7), MinWidth = 95 };

    public static string? Prompt(Window? owner, Words words, string title, string value = "")
    {
        var dialog = new Window { Owner = owner, SizeToContent = SizeToContent.Height,
            Width = 430, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = owner is null };
        SetWindow(dialog, words, title);
        var body = new StackPanel { Margin = new(18) };
        var field = Field(value);
        body.Children.Add(Text(title));
        field.Margin = new(0, 8, 0, 12);
        body.Children.Add(field);
        var buttons = new WrapPanel();
        var ok = Ok(() => { if (!string.IsNullOrWhiteSpace(field.Text)) dialog.DialogResult = true; });
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        dialog.Content = body;
        dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        return dialog.ShowDialog() == true ? field.Text.Trim() : null;
    }
}

internal static class Glyph
{
    public const string Add = "\uE710";
    public const string Demo = "\uE8B7";
    public const string Settings = "\uE713";
    public const string Info = "\uE946";
    public const string Open = "\uE8E5";
    public const string Edit = "\uE70F";
    public const string Copy = "\uE8C8";
    public const string Delete = "\uE74D";
    public const string Up = "\uE74A";
    public const string Down = "\uE74B";
    public const string Repair = "\uE72C";
    public const string Check = "\uE73E";
    public const string Close = "\uE711";
    public const string Color = "\uE790";
    public const string Apps = "\uE71D";
    public const string Image = "\uE91B";
    public const string Link = "\uE71B";
    public const string File = "\uE8E5";
}
