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

    internal void Build()
    {
        var selected = Selected;
        Ui.SetWindow(this, Words, AboutWindow.AppName);
        var root = new DockPanel { Margin = new(24), LastChildFill = true };
        var header = new StackPanel { Margin = new(0, 0, 0, 16) };
        var title = Ui.Text(Title, 30);
        title.FontWeight = FontWeights.SemiBold;
        var heading = new DockPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(Ui.Button(Glyph.Settings, L("Settings", "الإعدادات"), () =>
            new SettingsWindow(this, app).ShowDialog()));
        controls.Children.Add(Ui.Button(Glyph.Info, L("About", "حول التطبيق"), () =>
            new AboutWindow(Words) { Owner = this }.ShowDialog()));
        DockPanel.SetDock(controls, Dock.Right);
        heading.Children.Add(controls);
        heading.Children.Add(title);
        header.Children.Add(heading);
        var hint = Ui.Text(L("Create folders for your Windows desktop and manage their shortcuts.", "أنشئ مجلدات على سطح مكتب Windows وأدر اختصاراتها."));
        hint.Foreground = Ui.Muted;
        header.Children.Add(hint);
        var create = new WrapPanel { Margin = new(0, 12, 0, 0) };
        create.Children.Add(Ui.Button(Glyph.Add, L("New folder", "مجلد جديد"), () => Create(false), true));
        create.Children.Add(Ui.Button(Glyph.Demo, L("Add demo folder", "إضافة مجلد تجريبي"), () => Create(true)));
        header.Children.Add(create);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel { Margin = new(0, 12, 0, 0) };
        var actions = new WrapPanel();
        var menu = new ContextMenu { FlowDirection = Words.Direction };
        Add(Glyph.Open, L("Open panel", "فتح اللوحة"), async () => { if (Selected is { } folder) await app.Open(folder); });
        Add(Glyph.Edit, L("Edit", "تعديل"), async () => { if (Selected is { } folder) await app.Edit(folder); });
        Add(Glyph.Copy, L("Duplicate", "تكرار"), Duplicate);
        Add(Glyph.Delete, L("Delete", "حذف"), Delete);
        Add(Glyph.Up, L("Move up", "نقل لأعلى"), () => ViewModel.MoveSelection(-1));
        Add(Glyph.Down, L("Move down", "نقل لأسفل"), () => ViewModel.MoveSelection(1));
        Add(Glyph.Repair, L("Repair desktop item", "إصلاح عنصر سطح المكتب"), () =>
        {
            if (Selected is { } folder) app.TryShell(() => WindowsServices.SyncDesktop(folder));
        });
        Add(Glyph.File, L("Open stored files", "فتح الملفات المخزنة"), () =>
        {
            if (Selected is { } folder) app.TryShell(() => FolderStorage.Open(folder.Id));
        });
        folders.ContextMenu = menu;
        bottom.Children.Add(actions);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        if (folders.Parent is Panel parent) parent.Children.Remove(folders);
        root.Children.Add(folders);
        Content = root;
        if (selected is not null) folders.SelectedItem = selected;

        void Add(string glyph, string name, Action run)
        {
            actions.Children.Add(Ui.Button(glyph, name, run));
            var item = new MenuItem { Header = name, Icon = Ui.Icon(glyph, 15) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
    }

    private void Create(bool demo)
    {
        if (NewFolderDialog.Ask(this, Words, demo ? L("Demo", "تجريبي") : "") is not { } folder) return;
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
        if (ViewModel.Add(folder)) app.TryShell(() => WindowsServices.SyncDesktop(folder));
    }

    private void Duplicate()
    {
        if (Selected is not { } folder) return;
        AddFolder(FolderManagerViewModel.Duplicate(folder, folder.Name + L(" copy", " نسخة")));
    }

    private void Delete()
    {
        if (Selected is not { } folder || MessageBox.Show(this,
            L("Delete this popup and its desktop shortcut? Stored files will be kept.",
                "هل تريد حذف المجلد المنبثق واختصاره؟ ستبقى الملفات المخزنة."),
            Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!ViewModel.Delete(folder)) return;
        app.TryShell(() =>
        {
            WindowsServices.RemoveLinks(folder.Id);
            WindowsServices.RemoveFolderIcons(folder.Id);
        });
    }
}
