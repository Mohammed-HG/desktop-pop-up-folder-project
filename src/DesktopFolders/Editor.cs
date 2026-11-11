using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopFolders.Core;
using Microsoft.Win32;

namespace DesktopFolders;

internal sealed class Editor : Window
{
    private readonly Words words;
    private readonly PanelWindow preview;
    private readonly ListBox entries = new() { Height = 190, DisplayMemberPath = nameof(Entry.Name), AllowDrop = true };
    private Point dragOrigin;
    private Entry? dragEntry;
    private bool validName = true;
    private bool validColor = true;
    private bool verifying;
    private readonly bool originallyLocked;
    public Folder Draft { get; }
    private PanelSettings Panel => Draft.Panel;
    private string L(string en, string ar) => words.Get(en, ar);

    public Editor(Folder draft, Preferences preferences)
    {
        Draft = draft;
        originallyLocked = draft.Locked;
        words = new(preferences.Arabic);
        Width = 910;
        Height = 680;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.SetWindow(this, words, L("Edit folder", "تعديل المجلد"));
        preview = new(draft, preferences, _ => { }, () => { }, preview: true);

        var root = new DockPanel { Margin = new(18) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var apply = Ui.Button(L("Save changes", "حفظ التغييرات"), async () =>
        {
            if (verifying) return;
            if (Draft.ExplorerFolder && Draft.Locked)
            {
                MessageBox.Show(this, L("Turn off Windows Hello before converting to a Windows folder.",
                    "أوقف Windows Hello قبل التحويل إلى مجلد Windows."));
                return;
            }
            if (!validName || !validColor)
            {
                MessageBox.Show(this, L("Enter a folder name and a color in #RRGGBB format.", "أدخل اسمًا للمجلد ولونًا بالتنسيق ‎#RRGGBB."));
                return;
            }
            if (Draft.Locked && !originallyLocked)
            {
                verifying = true;
                try { if (!await Hello.Verify(this, words) || !IsVisible) return; }
                finally { verifying = false; }
            }
            Draft.Panel.Normalize();
            DialogResult = true;
        }, true);
        actions.Children.Add(apply);
        var cancel = Ui.Button(L("Cancel", "إلغاء"), () => DialogResult = false);
        cancel.IsCancel = true;
        actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new() { Width = new(1.13, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var tabs = new TabControl { Margin = new(0, 0, 12, 0) };
        tabs.Items.Add(Tab(L("Shortcuts", "الاختصارات"), ShortcutTab()));
        tabs.Items.Add(Tab(L("Appearance", "المظهر"), AppearanceTab()));
        tabs.Items.Add(Tab(L("Behavior", "السلوك"), BehaviorTab()));
        layout.Children.Add(tabs);
        var previewLayout = new DockPanel();
        var heading = Ui.Text(L("Live preview", "معاينة مباشرة"), 18);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new(0, 0, 0, 10);
        DockPanel.SetDock(heading, Dock.Top);
        previewLayout.Children.Add(heading);
        previewLayout.Children.Add(new Border
        {
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#8CAFF4"),
                (Color)ColorConverter.ConvertFromString("#D5A5C3"), 45),
            CornerRadius = new(16), Padding = new(12),
            Child = new Viewbox { StretchDirection = StretchDirection.DownOnly, Child = preview.Preview }
        });
        Grid.SetColumn(previewLayout, 1);
        layout.Children.Add(previewLayout);
        root.Children.Add(layout);
        Content = root;
        UpdatePreview();
    }

    private static TabItem Tab(string title, UIElement content) => new()
    {
        Header = title,
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content }
    };

    private void UpdatePreview()
    {
        preview.Preview.Width = Panel.Width;
        preview.Preview.Height = Panel.Height;
        preview.Refresh();
    }

    private StackPanel ShortcutTab()
    {
        var body = new StackPanel { Margin = new(10) };
        var name = Ui.Field(Draft.Name);
        name.MaxLength = 120;
        name.TextChanged += (_, _) =>
        {
            validName = !string.IsNullOrWhiteSpace(name.Text);
            if (validName) Draft.Name = name.Text.Trim();
            UpdatePreview();
        };
        Row(body, L("Folder name", "اسم المجلد"), name);
        var native = Check(body, L("Use a normal Windows folder on the desktop", "استخدام مجلد Windows عادي على سطح المكتب"),
            Draft.ExplorerFolder, value => Draft.ExplorerFolder = value);
        native.IsEnabled = !Draft.ExplorerFolder;
        native.ToolTip = L("A Windows folder opens in File Explorer and supports files. Windows Hello is available for popup folders.",
            "يفتح مجلد Windows في مستكشف الملفات ويدعم الملفات. يتوفر Windows Hello لمجلدات اللوحة المنبثقة.");
        var icons = new WrapPanel();
        icons.Children.Add(Ui.Button(L("Folder icon color", "لون أيقونة المجلد"), () =>
        {
            if (IconPresets.PickColor(this, words, Draft.IconColor) is not { } color) return;
            Draft.Icon = "";
            Draft.IconColor = color;
        }));
        icons.Children.Add(Ui.Button(L("Office folder icon", "أيقونة Office للمجلد"), () =>
        {
            if (IconPresets.Pick(this, words) is { } icon) Draft.Icon = icon;
        }));
        icons.Children.Add(Ui.Button(L("Choose folder icon", "اختيار أيقونة المجلد"), () =>
        {
            if (PickImage() is { } image) Draft.Icon = image;
        }));
        icons.Children.Add(Ui.Button(L("Reset folder icon", "إعادة ضبط أيقونة المجلد"), () =>
        {
            Draft.Icon = "";
            Draft.IconColor = "";
        }));
        body.Children.Add(icons);

        var add = new WrapPanel { Margin = new(0, 9, 0, 0) };
        add.Children.Add(Ui.Button(L("Add files", "إضافة ملفات"), () =>
        {
            var picker = new OpenFileDialog { Multiselect = true,
                Filter = L("App shortcuts (*.exe, *.lnk, *.url)|*.exe;*.lnk;*.url", "اختصارات التطبيقات (*.exe, *.lnk, *.url)|*.exe;*.lnk;*.url") };
            if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames);
        }));
        add.Children.Add(Ui.Button(L("Installed apps", "التطبيقات المثبتة"), ChooseApp));
        add.Children.Add(Ui.Button(L("Add URL", "إضافة رابط"), () =>
        {
            var url = Ui.Prompt(this, words, L("Enter an HTTP or HTTPS URL", "أدخل رابط HTTP أو HTTPS"), "https://");
            if (url is null) return;
            if (Targets.Classify(url) == TargetKind.Web) Add(new() { Name = new Uri(url).Host, Target = url });
            else MessageBox.Show(this, L("Enter a valid web address.", "أدخل عنوان ويب صالحًا."));
        }));
        body.Children.Add(add);
        var tip = Ui.Text(L("Drop files here. Drag shortcuts to reorder them.", "أفلت الملفات هنا. اسحب الاختصارات لإعادة ترتيبها."), 12);
        tip.Foreground = Ui.Muted;
        tip.Margin = new(0, 5, 0, 5);
        body.Children.Add(tip);
        entries.ItemsSource = Draft.Entries;
        entries.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragOrigin = e.GetPosition(entries);
            dragEntry = ItemAt(e.OriginalSource as DependencyObject);
        };
        entries.PreviewMouseMove += (_, e) =>
        {
            if (dragEntry is not { } entry || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(entries) - dragOrigin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragEntry = null;
            DragDrop.DoDragDrop(entries, entry, DragDropEffects.Move);
        };
        entries.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(typeof(Entry)) ? DragDropEffects.Move :
                e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        entries.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
            else if (e.Data.GetData(typeof(Entry)) is Entry entry && Draft.Entries.Contains(entry))
            {
                var target = ItemAt(e.OriginalSource as DependencyObject);
                Move(entry, target is null ? Draft.Entries.Count - 1 : Draft.Entries.IndexOf(target));
            }
        };
        body.Children.Add(entries);
        var manage = new WrapPanel { Margin = new(0, 9, 0, 0) };
        manage.Children.Add(Ui.Button(L("Rename shortcut", "إعادة تسمية الاختصار"), () =>
        {
            if (entries.SelectedItem is Entry entry && Ui.Prompt(this, words,
                L("Shortcut label", "اسم الاختصار"), entry.Name) is { } label)
            {
                entry.Name = label;
                entries.Items.Refresh();
                UpdatePreview();
            }
        }));
        manage.Children.Add(Ui.Button(L("Change target", "تغيير الوجهة"), () =>
        {
            if (entries.SelectedItem is not Entry entry) return;
            var target = Ui.Prompt(this, words, L("Shortcut path or URL", "مسار الاختصار أو الرابط"), entry.Target);
            if (target is null) return;
            if (Targets.Classify(target) == TargetKind.Invalid)
            {
                MessageBox.Show(this, L("This shortcut target is unsupported.", "وجهة الاختصار غير مدعومة."));
                return;
            }
            entry.Target = target;
            UpdatePreview();
        }));
        manage.Children.Add(Ui.Button(L("Custom icon", "أيقونة مخصصة"), () =>
        {
            if (entries.SelectedItem is Entry entry && PickImage() is { } image) { entry.Icon = image; UpdatePreview(); }
        }));
        manage.Children.Add(Ui.Button(L("Office icon", "أيقونة Office"), () =>
        {
            if (entries.SelectedItem is Entry entry && IconPresets.Pick(this, words) is { } icon)
            {
                entry.Icon = icon;
                UpdatePreview();
            }
        }));
        manage.Children.Add(Ui.Button(L("Reset icon", "إعادة ضبط الأيقونة"), () =>
        {
            if (entries.SelectedItem is Entry entry) { entry.Icon = ""; UpdatePreview(); }
        }));
        manage.Children.Add(Ui.Button(L("Remove", "إزالة"), () =>
        {
            if (entries.SelectedItem is Entry entry) { Draft.Entries.Remove(entry); UpdatePreview(); }
        }));
        manage.Children.Add(Ui.Button(L("Move up", "نقل لأعلى"), () =>
        {
            if (entries.SelectedItem is Entry entry) Move(entry, entries.SelectedIndex - 1);
        }));
        manage.Children.Add(Ui.Button(L("Move down", "نقل لأسفل"), () =>
        {
            if (entries.SelectedItem is Entry entry) Move(entry, entries.SelectedIndex + 1);
        }));
        body.Children.Add(manage);
        return body;
    }

    private Entry? ItemAt(DependencyObject? origin) =>
        origin is not null && ItemsControl.ContainerFromElement(entries, origin) is ListBoxItem container
            ? container.Content as Entry : null;

    private void Move(Entry entry, int destination)
    {
        if (destination < 0 || destination >= Draft.Entries.Count) return;
        Draft.Entries.Move(Draft.Entries.IndexOf(entry), destination);
        entries.SelectedItem = entry;
        UpdatePreview();
    }

    private string? PickImage()
    {
        var picker = new OpenFileDialog
        {
            Filter = L("Images (*.png, *.jpg, *.bmp, *.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico",
                "الصور (*.png, *.jpg, *.bmp, *.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico")
        };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
    }

    private void AddFiles(string[] paths)
    {
        foreach (var path in paths.Where(path => Targets.Classify(path) == TargetKind.File))
            Draft.Entries.Add(new() { Name = Path.GetFileNameWithoutExtension(path), Target = path });
        UpdatePreview();
    }

    private void Add(Entry entry)
    {
        Draft.Entries.Add(entry);
        entries.SelectedItem = entry;
        UpdatePreview();
    }

    private void ChooseApp()
    {
        System.Collections.Generic.IReadOnlyList<Entry> apps;
        try { apps = WindowsServices.InstalledApps(); }
        catch (Exception error)
        {
            WindowsServices.Log("enumerate-installed-apps", error);
            MessageBox.Show(this, L("Could not read the installed app list.", "تعذرت قراءة قائمة التطبيقات المثبتة."));
            return;
        }
        var dialog = new Window { Owner = this, Width = 465, Height = 500, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.SetWindow(dialog, words, L("Installed apps", "التطبيقات المثبتة"));
        var content = new DockPanel { Margin = new(14) };
        var search = Ui.Field("");
        search.Margin = new(0, 0, 0, 8);
        System.Windows.Automation.AutomationProperties.SetName(search, L("Search apps", "البحث عن تطبيقات"));
        DockPanel.SetDock(search, Dock.Top);
        content.Children.Add(search);
        var list = new ListBox { ItemsSource = apps, DisplayMemberPath = nameof(Entry.Name) };
        var choose = Ui.Button(L("Add selected app", "إضافة التطبيق المحدد"), () =>
        {
            if (list.SelectedItem is Entry entry)
            {
                Add(new() { Name = entry.Name, Target = entry.Target });
                dialog.DialogResult = true;
            }
        }, true);
        choose.IsDefault = true;
        choose.Margin = new(0, 8, 0, 0);
        list.MouseDoubleClick += (_, _) => choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        search.TextChanged += (_, _) =>
        {
            list.ItemsSource = apps.Where(entry => entry.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            list.SelectedIndex = 0;
        };
        DockPanel.SetDock(choose, Dock.Bottom);
        content.Children.Add(choose);
        content.Children.Add(list);
        dialog.Content = content;
        list.SelectedIndex = 0;
        dialog.ShowDialog();
    }

    private StackPanel AppearanceTab()
    {
        var body = new StackPanel { Margin = new(10) };
        Number(body, L("Width (240–1200)", "العرض (240–1200)"), Panel.Width, 240, 1200, value => Panel.Width = value);
        Number(body, L("Height (160–1000)", "الارتفاع (160–1000)"), Panel.Height, 160, 1000, value => Panel.Height = value);
        Number(body, L("Columns (1–10)", "الأعمدة (1–10)"), Panel.Columns, 1, 10, value => Panel.Columns = (int)value);
        Slider(body, L("Icon size", "حجم الأيقونات"), Panel.IconSize, 20, 96, value => Panel.IconSize = value);
        Slider(body, L("Icon spacing", "المسافة بين الأيقونات"), Panel.Spacing, 0, 24, value => Panel.Spacing = value);
        Slider(body, L("Panel padding", "الحشوة الداخلية للوحة"), Panel.Padding, 0, 40, value => Panel.Padding = value);
        Slider(body, L("Corner radius", "استدارة الزوايا"), Panel.Radius, 0, 48, value => Panel.Radius = value);
        Check(body, L("Show labels", "إظهار الأسماء"), Panel.Labels, value => Panel.Labels = value);
        Slider(body, L("Label font size", "حجم خط الأسماء"), Panel.FontSize, 9, 24, value => Panel.FontSize = value);
        Choices(body, L("Background", "الخلفية"), [
            L("Light", "فاتحة"), L("Dark", "داكنة"), L("Custom solid", "لون مخصص مصمت"), L("Translucent", "شبه شفافة")],
            (int)Panel.Background, value => Panel.Background = (Backdrop)value);
        var color = Ui.Field(Panel.Color);
        color.FlowDirection = FlowDirection.LeftToRight;
        color.MaxLength = 7;
        color.TextChanged += (_, _) =>
        {
            validColor = color.Text.Length == 7 && color.Text[0] == '#' && color.Text[1..].All(Uri.IsHexDigit);
            if (validColor) { Panel.Color = color.Text; UpdatePreview(); }
        };
        Row(body, L("Custom color (#RRGGBB)", "اللون المخصص (‎#RRGGBB)"), color);
        Slider(body, L("Transparency (%)", "الشفافية (%)"), Panel.Transparency, 0, 100, value => Panel.Transparency = value);
        return body;
    }

    private StackPanel BehaviorTab()
    {
        var body = new StackPanel { Margin = new(10) };
        Check(body, L("Close when clicking outside", "الإغلاق عند النقر خارج اللوحة"), Panel.OutsideClose, value => Panel.OutsideClose = value);
        Check(body, L("Remember panel position", "تذكر موضع اللوحة"), Panel.RememberPosition, value =>
        {
            Panel.RememberPosition = value;
            if (!value) { Panel.X = null; Panel.Y = null; }
        });
        Choices(body, L("Animations", "الحركات"), [L("Use global setting", "استخدام الإعداد العام"), L("On", "تشغيل"), L("Off", "إيقاف")],
            (int)Panel.Animation, value => Panel.Animation = (AnimationChoice)value);
        Check(body, L("Animate icons on hover", "تحريك الأيقونات عند مرور المؤشر"), Panel.Hover, value => Panel.Hover = value);
        Number(body, L("Duration in ms (0–400)", "المدة بالمللي ثانية (0–400)"), Panel.Duration, 0, 400, value => Panel.Duration = (int)value);
        var lockToggle = Check(body, L("Lock with Windows Hello", "القفل باستخدام Windows Hello"), Draft.Locked, value => Draft.Locked = value);
        if (Draft.ExplorerFolder && !Draft.Locked)
        {
            lockToggle.IsEnabled = false;
            body.Children.Add(Ui.Text(L("Normal Windows folders open directly in File Explorer.", "تُفتح مجلدات Windows العادية مباشرة في مستكشف الملفات."), 12));
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            lockToggle.IsEnabled = false;
            body.Children.Add(Ui.Text(L("Folder locking requires Windows 11 and Windows Hello.", "يتطلب قفل المجلد Windows 11 وWindows Hello."), 12));
        }
        Number(body, L("Lock after idle minutes (0 = off)", "القفل بعد الخمول بالدقائق (0 = إيقاف)"), Panel.LockMinutes, 0, 120,
            value => Panel.LockMinutes = (int)value);
        body.Children.Add(Ui.Text(L("The lock protects this folder interface only. Its original apps and files remain accessible in Windows.",
            "يحمي القفل واجهة هذا المجلد فقط. تظل التطبيقات والملفات الأصلية متاحة في Windows."), 12));
        return body;
    }

    private static void Row(StackPanel body, string label, FrameworkElement control)
    {
        var text = Ui.Text(label, 13);
        text.Margin = new(0, 6, 0, 2);
        body.Children.Add(text);
        control.Margin = new(0, 0, 0, 6);
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        body.Children.Add(control);
    }

    private void Number(StackPanel body, string label, double initial, int min, int max, Action<double> change)
    {
        var field = Ui.Field(Math.Round(initial).ToString(CultureInfo.InvariantCulture));
        var lastValid = field.Text;
        field.TextChanged += (_, _) =>
        {
            if (!int.TryParse(field.Text, out var value) || value < min || value > max) return;
            lastValid = field.Text;
            change(value);
            UpdatePreview();
        };
        field.LostKeyboardFocus += (_, _) =>
        {
            if (!int.TryParse(field.Text, out var value)) field.Text = lastValid;
            else field.Text = Math.Clamp(value, min, max).ToString(CultureInfo.InvariantCulture);
            lastValid = field.Text;
        };
        Row(body, label, field);
    }

    private void Slider(StackPanel body, string label, double value, double min, double max, Action<double> change)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true,
            AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft };
        var display = Ui.Text($"{value:0}");
        slider.ValueChanged += (_, _) => { display.Text = $"{slider.Value:0}"; change(slider.Value); UpdatePreview(); };
        Row(body, label, slider);
        body.Children.Add(display);
    }

    private CheckBox Check(StackPanel body, string label, bool value, Action<bool> change)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new(0, 7, 0, 7) };
        box.Click += (_, _) => { change(box.IsChecked == true); UpdatePreview(); };
        body.Children.Add(box);
        return box;
    }

    private void Choices(StackPanel body, string label, string[] items, int value, Action<int> change)
    {
        var select = new ComboBox { ItemsSource = items, SelectedIndex = value, MinHeight = 30 };
        select.SelectionChanged += (_, _) => { change(select.SelectedIndex); UpdatePreview(); };
        Row(body, label, select);
    }
}
