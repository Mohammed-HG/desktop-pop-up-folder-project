using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopFolders.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopFolders;

internal sealed class PanelWindow : Window
{
    private readonly Folder folder;
    private readonly PanelSettings settings;
    private readonly Action<Entry> launch;
    private readonly Action save;
    private readonly bool preview;
    private readonly Words words;
    private readonly bool animations;
    private readonly Func<string[], bool>? import;
    private readonly Func<string[], bool>? move;
    private readonly IReadOnlyList<Entry> externalEntries;
    private readonly Border surface = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly Dictionary<Entry, (string Path, string Target, ImageSource Image)> icons = new();
    private readonly DispatcherTimer? inactivity;
    private readonly List<Button> tiles = [];
    private Drawing.Rectangle work;
    private double pixelsPerDip = 1;
    private bool closing;
    private bool positioned;

    public Border Preview => surface;
    private string L(string en, string ar) => words.Get(en, ar);

    public PanelWindow(Folder folder, Preferences preferences, Action<Entry> launch, Action save, bool preview = false,
        Func<string[], bool>? import = null, IReadOnlyList<Entry>? externalEntries = null,
        Func<string[], bool>? move = null)
    {
        this.folder = folder;
        settings = folder.Panel;
        this.launch = launch;
        this.save = save;
        this.preview = preview;
        this.import = import;
        this.move = move;
        this.externalEntries = externalEntries ?? [];
        words = new(preferences.Arabic);
        animations = SystemParameters.ClientAreaAnimation && settings.Animation switch
        {
            AnimationChoice.On => true,
            AnimationChoice.Off => false,
            _ => true
        };

        if (!preview)
        {
            Ui.SetWindow(this, words, folder.Name);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            surface.RenderTransform = scale;
            surface.RenderTransformOrigin = new(0.5, 0.15);
            Content = surface;
            AllowDrop = move is not null;
            PreviewDragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && e.AllowedEffects.HasFlag(DragDropEffects.Move)
                    ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            PreviewDrop += (_, e) =>
            {
                e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Move) && Receive(e.Data, moving: true)
                    ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            Deactivated += (_, _) => { if (positioned && settings.OutsideClose) Dismiss(); };
            PreviewKeyDown += OnKey;
            Closed += (_, _) => inactivity?.Stop();
            if (folder.Locked && settings.LockMinutes > 0)
            {
                inactivity = new DispatcherTimer { Interval = TimeSpan.FromMinutes(settings.LockMinutes) };
                inactivity.Tick += (_, _) => DismissImmediately();
                PreviewKeyDown += (_, _) => ResetTimer();
                PreviewMouseMove += (_, _) => ResetTimer();
                PreviewMouseDown += (_, _) => ResetTimer();
                PreviewMouseWheel += (_, _) => ResetTimer();
            }
        }
        Refresh();
    }

    public void Refresh()
    {
        settings.Normalize();
        var fill = settings.Background == Backdrop.Windows ? Ui.WindowsPanelColor :
            (Color)ColorConverter.ConvertFromString(settings.Background switch
        {
            Backdrop.Light => "#F0F4FD",
            Backdrop.Dark => "#1C2437",
            _ => settings.Color
        });
        var alpha = 1 - settings.Transparency / 100;
        if (settings.Background == Backdrop.Translucent) alpha *= .78;
        var luminance = .2126 * Linear(fill.R) + .7152 * Linear(fill.G) + .0722 * Linear(fill.B);
        var lightInk = luminance < .179;
        var foreground = settings.Background == Backdrop.Windows
            ? new SolidColorBrush(Ui.WindowsPanelInk) : lightInk ? Brushes.White : Brushes.Black;
        var labelFill = alpha < .88 ? new SolidColorBrush(fill) : Brushes.Transparent;
        fill.A = (byte)Math.Round(255 * alpha);
        surface.Background = new SolidColorBrush(fill);
        surface.CornerRadius = new(settings.Radius);
        surface.Padding = new(settings.Padding);
        surface.FlowDirection = words.Direction;
        surface.BorderBrush = alpha == 0 ? Brushes.Transparent : Ui.Brush(lightInk ? "#52617D" : "#BCCBE4");
        surface.BorderThickness = new(alpha == 0 ? 0 : 1);
        tiles.Clear();

        var content = new DockPanel { LastChildFill = true };
        var top = new Grid { Margin = new(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new());
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = folder.Name, FontSize = 19, FontWeight = FontWeights.SemiBold,
            Foreground = foreground, TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center
        };
        var headingBadge = new Border { Background = labelFill, CornerRadius = new(7), Child = heading,
            HorizontalAlignment = HorizontalAlignment.Left };
        top.Children.Add(headingBadge);
        if (!preview)
        {
            headingBadge.Cursor = Cursors.SizeAll;
            headingBadge.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ButtonState != MouseButtonState.Pressed) return;
                DragMove();
                ClampCurrent();
                RememberPosition();
            };
        }
        if (settings.ShowCloseButton)
        {
            var close = Ui.CornerClose(L("Close", "إغلاق"), Dismiss, foreground);
            close.IsHitTestVisible = !preview;
            close.Focusable = !preview;
            Grid.SetColumn(close, 1);
            top.Children.Add(close);
        }
        DockPanel.SetDock(top, Dock.Top);
        content.Children.Add(top);

        if (!preview)
        {
            var resize = new Thumb { Width = 23, Height = 19, HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeNWSE, ToolTip = L("Drag to resize", "اسحب لتغيير الحجم") };
            System.Windows.Automation.AutomationProperties.SetName(resize, L("Resize panel", "تغيير حجم اللوحة"));
            var glyph = new FrameworkElementFactory(typeof(TextBlock));
            glyph.SetValue(TextBlock.TextProperty, "◢");
            glyph.SetValue(TextBlock.ForegroundProperty, foreground);
            glyph.SetValue(TextBlock.BackgroundProperty, labelFill);
            glyph.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            resize.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = glyph };
            resize.DragDelta += (_, e) =>
            {
                Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
                Height = Math.Clamp(Height + e.VerticalChange, MinHeight, MaxHeight);
                ClampCurrent();
            };
            resize.DragCompleted += (_, _) =>
            {
                settings.Width = Width;
                settings.Height = Height;
                settings.Normalize();
                RememberPosition();
            };
            DockPanel.SetDock(resize, Dock.Bottom);
            content.Children.Add(resize);
        }

        var grid = new UniformGrid { Columns = settings.Columns, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(0, 0, 2, 0) };
        var configured = folder.Entries.Select(entry => entry.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in folder.Entries.Concat(externalEntries.Where(entry => !configured.Contains(entry.Target))))
        {
            var button = Tile(entry, foreground, labelFill);
            tiles.Add(button);
            grid.Children.Add(button);
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
        if (tiles.Count == 0)
        {
            var empty = Ui.Text(L("Drag a file here to move it, or press Ctrl+V to add a link.",
                "اسحب ملفًا إلى هنا لنقله، أو اضغط Ctrl+V لإضافة اختصار."));
            empty.Padding = new(8);
            empty.Foreground = foreground;
            content.Children.Add(new Border { Background = labelFill, CornerRadius = new(8),
                Child = empty, VerticalAlignment = VerticalAlignment.Top });
        }
        else content.Children.Add(scroll);
        surface.Child = content;
    }

    private Button Tile(Entry entry, Brush foreground, Brush labelFill)
    {
        if (!icons.TryGetValue(entry, out var icon) || icon.Path != entry.Icon || icon.Target != entry.Target)
        {
            icon = (entry.Icon, entry.Target, WindowsServices.EntryIcon(entry));
            icons[entry] = icon;
        }
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var image = new Image
        {
            Source = icon.Image, MaxWidth = settings.IconSize, Height = settings.IconSize,
            Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center
        };
        body.Children.Add(image);
        if (settings.Labels)
        {
            var text = new TextBlock
            {
                Text = entry.Name, Foreground = foreground, TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = settings.FontSize, TextAlignment = TextAlignment.Center, Padding = new(3, 1, 3, 1)
            };
            body.Children.Add(new Border { Child = text, Background = labelFill, CornerRadius = new(5), Margin = new(0, 4, 0, 0) });
        }
        var button = new Button
        {
            Content = body, Margin = new(settings.Spacing / 2),
            ToolTip = entry.Name, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand, Focusable = !preview
        };
        System.Windows.Automation.AutomationProperties.SetName(button, entry.Name);
        var chrome = new FrameworkElementFactory(typeof(Border), "TileBorder");
        chrome.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        chrome.SetValue(Border.PaddingProperty, new Thickness(2, 4, 2, 4));
        chrome.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            var accent = Ui.AccentColor;
            trigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)), "TileBorder"));
            template.Triggers.Add(trigger);
        }
        button.Template = template;
        if (!preview)
        {
            button.Click += (_, _) => launch(entry);
            button.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; launch(entry); } };
        }
        if (animations && settings.Hover && settings.Duration > 0 && !preview)
        {
            var transform = new ScaleTransform(1, 1);
            image.RenderTransform = transform;
            image.RenderTransformOrigin = new(.5, .5);
            button.MouseEnter += (_, _) => Zoom(transform, 1.08, Math.Min(140, settings.Duration));
            button.MouseLeave += (_, _) => Zoom(transform, 1, Math.Min(140, settings.Duration));
        }
        return button;
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); return; }
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && import is not null)
        {
            e.Handled = true;
            Paste();
            return;
        }
        var direction = words.Direction == FlowDirection.RightToLeft ? -1 : 1;
        var offset = e.Key switch
        {
            Key.Left => -direction, Key.Right => direction, Key.Up => -settings.Columns,
            Key.Down => settings.Columns, _ => 0
        };
        if (offset == 0 || tiles.Count == 0) return;
        var index = Keyboard.FocusedElement is Button focused ? tiles.IndexOf(focused) : -1;
        tiles[index < 0 ? 0 : Math.Clamp(index + offset, 0, tiles.Count - 1)].Focus();
        e.Handled = true;
    }

    private void Paste()
    {
        try
        {
            if (Clipboard.GetDataObject() is { } data) Receive(data);
        }
        catch (ExternalException error) { WindowsServices.Log("paste-clipboard", error); }
    }

    internal bool Receive(IDataObject data, bool moving = false)
    {
        var transfer = moving ? move : import;
        if (transfer is null || closing) return false;
        var paths = data.GetData(DataFormats.FileDrop) as string[];
        if (!moving && paths is null && data.GetData(DataFormats.UnicodeText) is string text)
            paths = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (paths is null || !transfer(paths)) return false;
        ResetTimer();
        Refresh();
        if (tiles.Count != 0) tiles[^1].Focus();
        return true;
    }

    public void ShowNear(Drawing.Point anchor)
    {
        if (settings.RememberPosition && settings.X is int x && settings.Y is int y)
        {
            var saved = new Drawing.Point(x, y);
            if (Forms.Screen.FromPoint(saved).Bounds.Contains(saved)) anchor = saved;
        }
        work = Forms.Screen.FromPoint(anchor).WorkingArea;
        var monitor = MonitorFromPoint(anchor, 2);
        if (GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0) pixelsPerDip = dpi / 96d;
        SetLimits();
        Width = Math.Clamp(settings.Width, MinWidth, MaxWidth);
        Height = Math.Clamp(settings.Height, MinHeight, MaxHeight);
        SourceInitialized += (_, _) => MovePixels(anchor, resize: true);
        if (animations && settings.Duration > 0) { surface.Opacity = 0; scale.ScaleX = scale.ScaleY = .96; }
        Show();
        positioned = true;
        if (settings.RememberPosition && GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)
            && (settings.X != rect.Left || settings.Y != rect.Top))
            RememberPosition();
        Activate();
        if (tiles.Count != 0) tiles[0].Focus();
        if (animations && settings.Duration > 0)
        {
            surface.BeginAnimation(OpacityProperty, Motion(1, settings.Duration));
            Zoom(scale, 1, settings.Duration);
        }
        ResetTimer();
    }

    private void SetLimits()
    {
        MaxWidth = Math.Min(1200, work.Width / pixelsPerDip);
        MaxHeight = Math.Min(1000, work.Height / pixelsPerDip);
        MinWidth = Math.Min(240, MaxWidth);
        MinHeight = Math.Min(160, MaxHeight);
    }

    internal bool FitsWorkArea()
    {
        return GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)
            && work.Contains(Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    private void MovePixels(Drawing.Point anchor, bool resize)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var width = (int)Math.Ceiling(Width * pixelsPerDip);
        var height = (int)Math.Ceiling(Height * pixelsPerDip);
        SetWindowPos(hwnd, 0, Math.Clamp(anchor.X, work.Left, Math.Max(work.Left, work.Right - width)),
            Math.Clamp(anchor.Y, work.Top, Math.Max(work.Top, work.Bottom - height)),
            width, height, resize ? 0x14u : 0x15u);
    }

    private void ClampCurrent()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out var rect)) return;
        work = Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = GetDpiForWindow(hwnd);
        if (dpi > 0) pixelsPerDip = dpi / 96d;
        SetLimits();
        MovePixels(new(rect.Left, rect.Top), resize: false);
    }

    private void RememberPosition()
    {
        if (settings.RememberPosition && GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
        {
            settings.X = rect.Left;
            settings.Y = rect.Top;
        }
        save();
    }

    private void ResetTimer() { inactivity?.Stop(); inactivity?.Start(); }

    public void DismissImmediately() { if (IsVisible) Close(); }

    public void Dismiss()
    {
        if (closing || !IsVisible) return;
        closing = true;
        if (!animations || settings.Duration == 0 || folder.Locked) { Close(); return; }
        IsHitTestVisible = false;
        var duration = Math.Min(settings.Duration, 180);
        var animation = Motion(0, duration);
        animation.Completed += (_, _) => DismissImmediately();
        surface.BeginAnimation(OpacityProperty, animation);
        Zoom(scale, .96, duration);
    }

    private static DoubleAnimation Motion(double target, int milliseconds) => new(target, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

    private static void Zoom(ScaleTransform transform, double target, int milliseconds)
    {
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(target, milliseconds));
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(target, milliseconds));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32")] private static extern nint MonitorFromPoint(Drawing.Point point, uint flags);
    [DllImport("shcore")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
