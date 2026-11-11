using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;

namespace DesktopFolders;

internal static class IconPresets
{
    private sealed record Preset(string Name, string Letter, string Color);
    private static readonly Preset[] Office =
    [
        new("Word", "W", "#2059AD"), new("Excel", "X", "#167447"),
        new("PowerPoint", "P", "#AF412A"), new("Outlook", "O", "#0069AE"),
        new("OneNote", "N", "#75379A"), new("Access", "A", "#A42639"),
        new("Publisher", "P", "#007E79"), new("Teams", "T", "#5145A3")
    ];

    internal static bool IsFolderColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static Preset? Find(string? value) => Office.FirstOrDefault(
        item => string.Equals("office:" + item.Name, value, StringComparison.OrdinalIgnoreCase));

    private static readonly Dictionary<string, ImageSource> Images = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Image(string? value)
    {
        var preset = Find(value);
        if (preset is null) return null;
        if (Images.TryGetValue(preset.Name, out var cached)) return cached;
        using var stream = new MemoryStream(Render(preset, null));
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return Images[preset.Name] = frame;
    }

    private static ImageSource FolderImage(string color)
    {
        try
        {
            if (Icon("", color) is not { } bytes) return WindowsServices.WindowsFolderImage;
            using var stream = new MemoryStream(bytes);
            return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception error)
        {
            WindowsServices.Log("folder-icon-preview", error);
            return WindowsServices.WindowsFolderImage;
        }
    }

    public static byte[]? Icon(string? icon, string? color = null)
    {
        if (Find(icon) is { } preset) return Render(preset, null);
        if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon))
        {
            using var source = new D.Bitmap(icon);
            using var resized = new D.Bitmap(source, 64, 64);
            return Encode(resized);
        }
        return IsFolderColor(color) ? Render(null, color) : null;
    }

    private static byte[] Render(Preset? preset, string? color)
    {
        if (preset is null) return TintWindowsFolder(color!);
        using var bitmap = new D.Bitmap(64, 64);
        using var graphics = D.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = D.Text.TextRenderingHint.AntiAliasGridFit;
        using var fill = new D.SolidBrush(D.ColorTranslator.FromHtml(preset.Color));
        graphics.FillRectangle(fill, 6, 6, 52, 52);
        using var shine = new D.SolidBrush(D.Color.FromArgb(38, D.Color.White));
        graphics.FillRectangle(shine, 44, 6, 14, 52);
        using var font = new D.Font("Segoe UI", 31, D.FontStyle.Bold, D.GraphicsUnit.Pixel);
        using var format = new D.StringFormat { Alignment = D.StringAlignment.Center, LineAlignment = D.StringAlignment.Center };
        graphics.DrawString(preset.Letter, font, D.Brushes.White, new D.RectangleF(5, 5, 52, 52), format);
        return Encode(bitmap);
    }

    private static byte[] TintWindowsFolder(string color)
    {
        var native = (BitmapSource)WindowsServices.WindowsFolderImage;
        var scaled = new TransformedBitmap(native, new ScaleTransform(64d / native.PixelWidth, 64d / native.PixelHeight));
        var source = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[64 * 64 * 4];
        source.CopyPixels(pixels, 64 * 4, 0);
        var tint = (Color)ColorConverter.ConvertFromString(color);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0) continue;
            var shade = (pixels[i] + pixels[i + 1] * 5 + pixels[i + 2] * 2) / 8d;
            byte Shade(byte component) => (byte)Math.Clamp(shade <= 190
                ? component * shade / 190
                : component + (255 - component) * (shade - 190) / 65, 0, 255);
            pixels[i] = Shade(tint.B);
            pixels[i + 1] = Shade(tint.G);
            pixels[i + 2] = Shade(tint.R);
        }
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var png = new MemoryStream();
        encoder.Save(png);
        return EncodePng(png.ToArray());
    }

    private static byte[] Encode(D.Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, D.Imaging.ImageFormat.Png);
        return EncodePng(png.ToArray());
    }

    private static byte[] EncodePng(byte[] png)
    {
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write(new byte[] { 0, 0, 1, 0, 1, 0, 64, 64, 0, 0, 1, 0, 32, 0 });
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);
        return file.ToArray();
    }

    public static string? Pick(Window owner, Words words)
    {
        string? chosen = null;
        var dialog = new Window { Owner = owner, Width = 395, Height = 250, ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.SetWindow(dialog, words, words.Get("Choose an Office icon", "اختر أيقونة Office"));
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new(14) };
        foreach (var preset in Office)
        {
            var key = "office:" + preset.Name;
            var button = Ui.Button(Glyph.Apps, preset.Name, () => { chosen = key; dialog.DialogResult = true; });
            button.Content = new Image { Source = Image(key), Width = 30, Height = 30 };
            System.Windows.Automation.AutomationProperties.SetName(button, preset.Name);
            tiles.Children.Add(button);
        }
        dialog.Content = tiles;
        return dialog.ShowDialog() == true ? chosen : null;
    }

    public static void PickColor(Window owner, Words words, string current, Action<string> apply)
    {
        string L(string en, string ar) => words.Get(en, ar);
        var dialog = new Window { Owner = owner, Width = 400, SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.SetWindow(dialog, words, L("Folder icon color", "لون أيقونة المجلد"));
        var body = new StackPanel { Margin = new(20) };
        var title = Ui.Text(dialog.Title, 24);
        title.FontWeight = FontWeights.SemiBold;
        body.Children.Add(title);
        var preview = new Image { Width = 70, Height = 70 };
        body.Children.Add(new Border { Child = preview, Background = Ui.Card, Padding = new(12),
            CornerRadius = new(12), Margin = new(0, 12, 0, 12) });
        var useWindows = new RadioButton { Content = L("Windows default", "لون مجلد ويندوز"), GroupName = "FolderColor",
            Margin = new(0, 0, 0, 6), IsChecked = !IsFolderColor(current) };
        var useCustom = new RadioButton { Content = L("Custom color", "اختيار لون"), GroupName = "FolderColor",
            IsChecked = IsFolderColor(current) };
        body.Children.Add(useWindows);
        body.Children.Add(useCustom);
        var custom = new StackPanel { Margin = new(0, 8, 0, 0) };
        body.Children.Add(custom);
        var field = Ui.Field(IsFolderColor(current) ? current : "#F7BE3D");
        field.FlowDirection = FlowDirection.LeftToRight;
        field.MaxLength = 7;
        field.Margin = new(0, 5, 0, 8);
        System.Windows.Automation.AutomationProperties.SetName(field, L("Custom color (#RRGGBB)", "لون مخصص (‎#RRGGBB)"));
        var colors = new WrapPanel();
        (string Name, string Color)[] palette =
        [
            (L("Yellow", "أصفر"), "#F7BE3D"), (L("Blue", "أزرق"), "#4998F4"),
            (L("Teal", "فيروزي"), "#31AFA2"), (L("Green", "أخضر"), "#72B755"),
            (L("Rose", "وردي"), "#EA768A"), (L("Purple", "بنفسجي"), "#A282EB"),
            (L("Orange", "برتقالي"), "#EC9541"), (L("Slate", "رمادي"), "#8696AF")
        ];
        foreach (var (name, color) in palette)
        {
            var button = Ui.Button(Glyph.Color, name, () => field.Text = color);
            button.Content = new System.Windows.Shapes.Ellipse { Fill = Ui.Brush(color), Width = 17, Height = 17 };
            colors.Children.Add(button);
        }
        custom.Children.Add(colors);
        custom.Children.Add(Ui.Text(L("Custom color (#RRGGBB)", "لون مخصص (‎#RRGGBB)")));
        custom.Children.Add(field);
        var applied = current;
        void Refresh()
        {
            custom.IsEnabled = useCustom.IsChecked == true;
            var selected = custom.IsEnabled ? field.Text : "";
            if (custom.IsEnabled && !IsFolderColor(selected)) return;
            preview.Source = FolderImage(selected);
            if (!string.Equals(applied, selected, StringComparison.OrdinalIgnoreCase))
            {
                apply(selected);
                applied = selected;
            }
        }
        useWindows.Checked += (_, _) => Refresh();
        useCustom.Checked += (_, _) => Refresh();
        field.TextChanged += (_, _) => Refresh();
        Refresh();
        var buttons = new WrapPanel();
        var save = Ui.Ok(() =>
        {
            if (useCustom.IsChecked == true && !IsFolderColor(field.Text))
            {
                field.Focus();
                return;
            }
            dialog.Close();
        });
        buttons.Children.Add(save);
        body.Children.Add(buttons);
        dialog.Content = body;
        dialog.ShowDialog();
    }
}
