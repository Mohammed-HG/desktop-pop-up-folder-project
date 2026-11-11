using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopFolders;

internal sealed class Words(bool arabic)
{
    public string Get(string en, string ar) => arabic ? ar : en;
    public FlowDirection Direction => arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}

internal static class Ui
{
    public static readonly Brush Ink = Brush("#18243A");
    public static readonly Brush Muted = Brush("#52627A");
    public static readonly Brush Surface = Brush("#F1F5FC");
    public static readonly Brush Accent = Brush("#25499C");
    public static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

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

    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = label, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 6, 6),
            MinHeight = 34, Foreground = primary ? Brushes.White : Ink,
            Background = primary ? Accent : Brushes.White, BorderBrush = Brush("#CCD6E7"),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        return button;
    }

    public static TextBlock Text(string value, double size = 14) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap
    };

    public static TextBox Field(string value) => new() { Text = value, Padding = new(7), MinWidth = 95 };

    public static string? Prompt(Window owner, Words words, string title, string value = "")
    {
        var dialog = new Window { Owner = owner, SizeToContent = SizeToContent.Height,
            Width = 430, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false };
        SetWindow(dialog, words, title);
        var body = new StackPanel { Margin = new(18) };
        var field = Field(value);
        body.Children.Add(Text(title));
        field.Margin = new(0, 8, 0, 12);
        body.Children.Add(field);
        var buttons = new WrapPanel();
        var ok = Button(words.Get("OK", "موافق"), () => { if (!string.IsNullOrWhiteSpace(field.Text)) dialog.DialogResult = true; }, true);
        ok.IsDefault = true;
        buttons.Children.Add(ok);
        var cancel = Button(words.Get("Cancel", "إلغاء"), () => dialog.DialogResult = false);
        cancel.IsCancel = true;
        buttons.Children.Add(cancel);
        body.Children.Add(buttons);
        dialog.Content = body;
        dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        return dialog.ShowDialog() == true ? field.Text.Trim() : null;
    }
}
