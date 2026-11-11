using System.Windows;
using System.Windows.Controls;
using DesktopFolders.Core;

namespace DesktopFolders;

internal static class NewFolderDialog
{
    public static Folder? Ask(Window? owner, Words words, string initial = "")
    {
        string L(string en, string ar) => words.Get(en, ar);
        var dialog = new Window { Owner = owner, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner };
        Ui.SetWindow(dialog, words, L("New folder", "مجلد جديد"));
        var body = new StackPanel { Margin = new(22) };
        var title = Ui.Text(dialog.Title, 25);
        title.FontWeight = FontWeights.SemiBold;
        body.Children.Add(title);
        var name = Ui.Field(initial);
        Add(L("Folder name", "اسم المجلد"), name);
        var animations = new ComboBox { ItemsSource = AnimationLabels(words), SelectedIndex = 0 };
        Add(L("Animations", "الحركات"), animations);
        var showClose = new CheckBox { Content = L("Show the close icon", "إظهار علامة الإغلاق"), IsChecked = true, Margin = new(0, 4, 0, 12) };
        body.Children.Add(showClose);
        var folder = new Folder();
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) };
        colorRow.Children.Add(Ui.Button(Glyph.Color, L("Folder icon color", "لون أيقونة المجلد"), () =>
            IconPresets.PickColor(dialog, words, folder, selected => folder.IconColor = selected)));
        colorRow.Children.Add(Ui.Button("\uE734", L("Folder symbol", "رمز المجلد"), () =>
            FolderArtwork.PickMark(dialog, words, folder, (mark, style) => { folder.Mark = mark; folder.MarkStyle = style; })));
        body.Children.Add(colorRow);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var create = Ui.Ok(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            folder.Name = name.Text.Trim();
            folder.Panel.Animation = (AnimationChoice)animations.SelectedIndex;
            folder.Panel.ShowCloseButton = showClose.IsChecked == true;
            dialog.DialogResult = true;
        });
        actions.Children.Add(create);
        body.Children.Add(actions);
        dialog.Content = body;
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        return dialog.ShowDialog() == true ? folder : null;

        void Add(string label, FrameworkElement control)
        {
            var text = Ui.Text(label);
            text.Margin = new(0, 8, 0, 4);
            body.Children.Add(text);
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            body.Children.Add(control);
        }
    }

    public static string[] AnimationLabels(Words words) =>
        [words.Get("Follow Windows", "إعداد ويندوز"), words.Get("On", "تشغيل"), words.Get("Off", "إيقاف")];
}
