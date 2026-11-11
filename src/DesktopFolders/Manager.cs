using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DesktopFolders.Core;

namespace DesktopFolders;

internal sealed class Manager : Window
{
    private readonly App app;
    public FolderManagerViewModel ViewModel { get; }
    private readonly ListBox folders = new() { Padding = new(6), FontSize = 16 };
    private Words Words => app.Words;
    private string L(string en, string ar) => Words.Get(en, ar);

    public Manager(App app)
    {
        this.app = app;
        ViewModel = new(app.Data, app.Save);
        DataContext = ViewModel;
        Width = 690;
        Height = 500;
        MinWidth = 460;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        folders.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(FolderManagerViewModel.Folders)));
        folders.SetBinding(ListBox.SelectedItemProperty, new Binding(nameof(FolderManagerViewModel.Selected)) { Mode = BindingMode.TwoWay });
        var template = new DataTemplate(typeof(Folder));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Folder.Name)));
        text.SetValue(TextBlock.MarginProperty, new Thickness(8));
        template.VisualTree = text;
        folders.ItemTemplate = template;
        folders.MouseDoubleClick += async (_, _) => { if (Selected is { } folder) await app.Open(folder); };
        Build();
        folders.SelectedIndex = 0;
    }

    private Folder? Selected => ViewModel.Selected;

    private void Build()
    {
        Ui.SetWindow(this, Words, L("Desktop Folders", "مجلدات سطح المكتب"));
        var root = new DockPanel { Margin = new(24), LastChildFill = true };
        var header = new StackPanel { Margin = new(0, 0, 0, 16) };
        var title = Ui.Text(Title, 30);
        title.FontWeight = FontWeights.SemiBold;
        header.Children.Add(title);
        var hint = Ui.Text(L("Create folders for your Windows desktop and manage their shortcuts.", "أنشئ مجلدات على سطح مكتب Windows وأدر اختصاراتها."));
        hint.Foreground = Ui.Muted;
        header.Children.Add(hint);
        var create = new WrapPanel { Margin = new(0, 12, 0, 0) };
        create.Children.Add(Ui.Button(L("New folder", "مجلد جديد"), () => Create(false), true));
        create.Children.Add(Ui.Button(L("Add demo folder", "إضافة مجلد تجريبي"), () => Create(true)));
        header.Children.Add(create);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel { Margin = new(0, 12, 0, 0) };
        var actions = new WrapPanel();
        var menu = new ContextMenu { FlowDirection = Words.Direction };
        Add(L("Open", "فتح"), async () => { if (Selected is { } folder) await app.Open(folder); });
        Add(L("Edit", "تعديل"), async () => { if (Selected is { } folder) await app.Edit(folder); });
        Add(L("Duplicate", "تكرار"), Duplicate);
        Add(L("Delete", "حذف"), Delete);
        Add(L("Move up", "نقل لأعلى"), () => ViewModel.MoveSelection(-1));
        Add(L("Move down", "نقل لأسفل"), () => ViewModel.MoveSelection(1));
        Add(L("Repair desktop shortcut", "إصلاح اختصار سطح المكتب"), () =>
        {
            if (Selected is { } folder) app.TryShell(() => WindowsServices.SyncLink(folder));
        });
        folders.ContextMenu = menu;
        bottom.Children.Add(actions);
        var preferences = new WrapPanel { Margin = new(0, 8, 0, 0) };
        var startup = Check(L("Start with Windows", "البدء مع Windows"), app.Data.StartWithWindows, value =>
        {
            var old = app.Data.StartWithWindows;
            try
            {
                WindowsServices.SetStartup(value);
                app.Data.StartWithWindows = value;
                if (app.Save()) return;
                WindowsServices.SetStartup(old);
            }
            catch (Exception error)
            {
                WindowsServices.Log("startup", error);
                MessageBox.Show(this, L("Could not update Windows startup.", "تعذر تحديث إعداد بدء التشغيل."));
            }
            app.Data.StartWithWindows = old;
            Build();
        });
        preferences.Children.Add(startup);
        preferences.Children.Add(Check(L("Animations", "الحركات"), app.Data.Animate, value =>
        {
            app.Data.Animate = value;
            if (!app.Save()) { app.Data.Animate = !value; Build(); }
        }));
        preferences.Children.Add(Ui.Button(L("العربية", "English"), () =>
        {
            app.Data.Arabic = !app.Data.Arabic;
            if (!app.Save()) { app.Data.Arabic = !app.Data.Arabic; return; }
            Build();
            app.BuildTray();
        }));
        bottom.Children.Add(preferences);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        if (folders.Parent is Panel parent) parent.Children.Remove(folders);
        root.Children.Add(folders);
        Content = root;

        void Add(string name, Action run)
        {
            actions.Children.Add(Ui.Button(name, run));
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
    }

    private static CheckBox Check(string name, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = name, IsChecked = value, Margin = new(0, 0, 14, 6), VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private void Create(bool demo)
    {
        var name = Ui.Prompt(this, Words, L("Folder name", "اسم المجلد"), demo ? L("Demo", "تجريبي") : "");
        if (name is null) return;
        var folder = new Folder { Name = name };
        if (demo)
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<Entry>>(
                    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Demo.json")), JsonSerializerOptions.Web)!;
                foreach (var entry in entries)
                {
                    entry.Target = Environment.ExpandEnvironmentVariables(entry.Target);
                    folder.Entries.Add(entry);
                }
            }
            catch (Exception error)
            {
                WindowsServices.Log("load-demo", error);
                MessageBox.Show(this, L("Could not read the demo configuration.", "تعذرت قراءة الإعدادات التجريبية."));
                return;
            }
        }
        AddFolder(folder);
    }

    private void AddFolder(Folder folder)
    {
        if (ViewModel.Add(folder)) app.TryShell(() => WindowsServices.SyncLink(folder));
    }

    private void Duplicate()
    {
        if (Selected is not { } folder) return;
        AddFolder(FolderManagerViewModel.Duplicate(folder, folder.Name + L(" copy", " نسخة")));
    }

    private void Delete()
    {
        if (Selected is not { } folder || MessageBox.Show(this,
            L("Delete this folder and its desktop shortcut?", "هل تريد حذف هذا المجلد واختصاره من سطح المكتب؟"),
            Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!ViewModel.Delete(folder)) return;
        app.TryShell(() =>
        {
            WindowsServices.RemoveLinks(folder.Id);
            File.Delete(Path.Combine(WindowsServices.DataDirectory, $"{folder.Id:N}.ico"));
        });
    }
}
