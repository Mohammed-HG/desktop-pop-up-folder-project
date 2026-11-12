using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopFolders.Core;

namespace DesktopFolders;

internal static class FolderArtwork
{
    private sealed record Symbol(FolderMark Id, string English, string Arabic, string Outline, string Solid);

    private static readonly Symbol[] Symbols =
    [
        new(FolderMark.None, "No symbol", "بدون رمز", "", ""),
        new(FolderMark.Screwdriver, "Screwdriver", "مفك", "M4 20 L7 17 M7 17 L10 14 L14 18 L11 21 L7 17 M12 16 L20 8 M19 9 L22 4 L20 2 L15 5 L16 7 L12 11",
            "M3 19 L9 13 L11 15 L18 8 L17 6 L21 2 L23 4 L20 9 L18 9 L13 16 L15 18 L9 23 Z"),
        new(FolderMark.Wrench, "Wrench", "مفتاح ربط", "M19 3 L15 7 L17 10 L21 7 Q22 12 18 14 L14 14 L7 21 Q5 23 3 21 Q1 19 3 17 L10 10 Q9 6 12 3 Q15 1 19 3 Z",
            "M19 2 L14 7 L17 10 L22 5 Q24 13 18 15 L14 15 L7 22 Q5 24 2 21 Q0 19 2 17 L9 10 Q7 3 14 1 Z"),
        new(FolderMark.Hammer, "Hammer", "مطرقة", "M4 20 L14 10 M12 5 L16 1 L22 7 L18 11 L16 9 L7 22 L4 23 L1 20 L13 8 Z",
            "M12 4 L16 0 L24 8 L20 12 L17 9 L7 23 L3 24 L0 21 L13 7 Z"),
        new(FolderMark.Gear, "Gear", "ترس", "M9 2 L15 2 L16 5 L19 5 L22 9 L20 12 L22 15 L19 19 L16 19 L15 22 L9 22 L8 19 L5 19 L2 15 L4 12 L2 9 L5 5 L8 5 Z M16 12 A4 4 0 1 0 8 12 A4 4 0 1 0 16 12 Z",
            "M9 1 H15 L16 4 L20 4 L23 9 L21 12 L23 15 L20 20 H16 L15 23 H9 L8 20 H4 L1 15 L3 12 L1 9 L4 4 H8 Z M16 12 A4 4 0 1 0 8 12 A4 4 0 1 0 16 12 Z"),
        new(FolderMark.Home, "Home", "بيت", "M2 11 L12 2 L22 11 M5 9 V22 H19 V9 M10 22 V15 H14 V22",
            "M1 11 L12 1 L23 11 L20 11 V22 H15 V15 H9 V22 H4 V11 Z"),
        new(FolderMark.Cloud, "Cloud", "سحابة", "M5 20 C0 20 0 12 6 11 C6 2 18 1 19 10 C25 10 25 20 19 20 Z",
            "M5 21 C-1 21 -1 11 5 10 C6 0 19 1 20 9 C26 10 26 21 19 21 Z"),
        new(FolderMark.Star, "Star", "نجمة", "M12 1 L15 8 L23 9 L17 15 L18 23 L12 19 L5 23 L6 15 L1 9 L9 8 Z",
            "M12 0 L16 8 L24 9 L18 15 L19 24 L12 20 L5 24 L6 15 L0 9 L8 8 Z"),
        new(FolderMark.Bolt, "Lightning", "برق", "M13 1 L3 14 H10 L8 23 L21 9 H14 Z",
            "M14 0 L2 15 H10 L7 24 L23 8 H15 Z")
    ];

    private static Symbol Definition(FolderMark mark) => Symbols.FirstOrDefault(symbol => symbol.Id == mark) ?? Symbols[0];

    public static bool HasCustomArt(Folder folder) => IconPresets.IsFolderColor(folder.IconColor)
        || Definition(folder.Mark).Id != FolderMark.None;

    public static BitmapSource Preview(Folder folder) =>
        Render(128, FolderBase(folder.IconColor), Definition(folder.Mark), folder.MarkStyle);

    public static byte[] Ico(Folder folder)
    {
        var baseImage = FolderBase(folder.IconColor);
        var symbol = Definition(folder.Mark);
        int[] sizes = [16, 24, 32, 48, 64, 96, 128, 256];
        var images = new List<byte[]>(sizes.Length);
        foreach (var size in sizes)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(size, baseImage, symbol, folder.MarkStyle)));
            using var png = new MemoryStream();
            encoder.Save(png);
            images.Add(png.ToArray());
        }
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) writer.Write(image);
        return output.ToArray();
    }

    private static BitmapSource FolderBase(string color)
    {
        var native = WindowsServices.WindowsFolderImage;
        if (!IconPresets.IsFolderColor(color)) return native;
        var source = new FormatConvertedBitmap(native, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        var tint = (Color)ColorConverter.ConvertFromString(color);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0) continue;
            var shade = (pixels[i] + pixels[i + 1] * 5 + pixels[i + 2] * 2) / 8d;
            pixels[i] = Tint(tint.B, shade);
            pixels[i + 1] = Tint(tint.G, shade);
            pixels[i + 2] = Tint(tint.R, shade);
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }

    private static byte Tint(byte value, double shade) => (byte)Math.Clamp(shade <= 190
        ? value * shade / 190 : value + (255 - value) * (shade - 190) / 65, 0, 255);

    private static BitmapSource Render(int size, BitmapSource image, Symbol symbol, MarkStyle style)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            var ratio = (double)size / Math.Max(image.PixelWidth, image.PixelHeight);
            var width = image.PixelWidth * ratio;
            var height = image.PixelHeight * ratio;
            drawing.DrawImage(image, new Rect((size - width) / 2, (size - height) / 2, width, height));
            if (symbol.Id != FolderMark.None)
            {
                var accent = Ui.Brush("#1A2739");
                var geometry = Geometry.Parse(style == MarkStyle.Solid ? symbol.Solid : symbol.Outline);
                drawing.PushTransform(new TranslateTransform(size * .59, size * .10));
                drawing.PushTransform(new ScaleTransform(size * .34 / 24, size * .34 / 24));
                drawing.DrawGeometry(null, Line(Brushes.White, style == MarkStyle.Solid ? 3 : 4.5), geometry);
                drawing.DrawGeometry(style == MarkStyle.Solid ? accent : null,
                    Line(accent, style == MarkStyle.Solid ? .9 : 2.5), geometry);
                drawing.Pop();
                drawing.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Pen Line(Brush brush, double width) => new(brush, width)
    {
        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
    };

    public static void PickMark(Window owner, Words words, Folder folder, Action<FolderMark, MarkStyle> apply)
    {
        string L(string en, string ar) => words.Get(en, ar);
        var dialog = new Window { Owner = owner, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.SetWindow(dialog, words, L("Folder symbol", "رمز المجلد"));
        var body = new StackPanel { Margin = new(20) };
        var title = Ui.Text(dialog.Title, 23);
        title.FontWeight = FontWeights.SemiBold;
        body.Children.Add(title);
        var image = new Image { Width = 100, Height = 100 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        body.Children.Add(new Border { Child = image, Background = Ui.Card, CornerRadius = new(12), Padding = new(12),
            Margin = new(0, 10, 0, 10) });
        var marks = new ComboBox { ItemsSource = Symbols.Select(symbol => L(symbol.English, symbol.Arabic)).ToArray(),
            SelectedIndex = (int)Definition(folder.Mark).Id, Margin = new(0, 4, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(marks, dialog.Title);
        body.Children.Add(marks);
        var style = new ComboBox { ItemsSource = new[] { L("SVG outline", "خطوط SVG"), L("Solid icon", "أيقونة ممتلئة") },
            SelectedIndex = folder.MarkStyle == MarkStyle.Solid ? 1 : 0, Margin = new(0, 0, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetName(style, L("Symbol style", "نمط الرمز"));
        body.Children.Add(style);
        void Refresh(bool save)
        {
            if (marks.SelectedIndex < 0 || style.SelectedIndex < 0) return;
            var mark = Symbols[marks.SelectedIndex].Id;
            var selectedStyle = (MarkStyle)style.SelectedIndex;
            style.IsEnabled = mark != FolderMark.None;
            if (save) apply(mark, selectedStyle);
            image.Source = Render(128, FolderBase(folder.IconColor), Definition(mark), selectedStyle);
        }
        marks.SelectionChanged += (_, _) => Refresh(true);
        style.SelectionChanged += (_, _) => Refresh(true);
        Refresh(false);
        var ok = Ui.Ok(dialog.Close);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        body.Children.Add(ok);
        dialog.Content = body;
        dialog.ShowDialog();
    }
}
