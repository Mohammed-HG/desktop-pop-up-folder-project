using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DesktopFolders.Core;
using Forms = System.Windows.Forms;

namespace DesktopFolders;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private Forms.NotifyIcon? tray;
    private readonly CancellationTokenSource stop = new();
    private Manager? manager;
    private PanelWindow? popup;
    private bool busy;
    private string channel = "";
    internal Store Store { get; } = new(WindowsServices.DataDirectory);
    internal Preferences Data { get; private set; } = new();
    internal Words Words => new(Data.Arabic);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--smoke-ui"] or ["--smoke-ui", "--check-desktop"])
        {
            _ = RunSmokeTest(e.Args.Length == 2);
            return;
        }

        channel = $"DesktopFolders-{WindowsIdentity.GetCurrent().User!.Value}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        instance = new Mutex(false, @"Local\" + channel);
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }

        if (!ownsInstance)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", channel, PipeDirection.Out);
                client.Connect(2500);
                WindowsServices.ForwardForeground(client.SafePipeHandle);
                using var writer = new StreamWriter(client);
                writer.WriteLine(JsonSerializer.Serialize(e.Args));
            }
            catch (Exception error)
            {
                WindowsServices.Log("activate-instance", error);
                MessageBox.Show(AboutWindow.AppName + " — Could not contact the running app / تعذر الاتصال بالتطبيق");
            }
            Shutdown();
            return;
        }

        Data = Store.Load();
        Ui.Configure(Data.Appearance);
        Ui.WindowsColors.ColorValuesChanged += OnSystemColorsChanged;
        if (e.Args is ["--uninstall"]) { Cleanup(); return; }
        _ = Task.Run(Listen);
        BuildTray();
        if (Store.Recovery is not null)
        {
            var status = Store.Recovery switch
            {
                "backup" => Words.Get("Recovered settings from the local backup.", "تمت استعادة الإعدادات من النسخة الاحتياطية المحلية."),
                "unreadable" => Words.Get("Settings are unreadable. Check local permissions.", "تعذرت قراءة الإعدادات. تحقق من الأذونات المحلية."),
                _ => Words.Get("Damaged settings were set aside. Starting with default settings.", "تم الاحتفاظ بالإعدادات التالفة. سيتم البدء بالإعدادات الافتراضية.")
            };
            MessageBox.Show(status);
        }
        if (!Store.ReadOnly && Store.Recovery is null)
        {
            foreach (var folder in Data.Folders)
            {
                try { WindowsServices.SyncLink(folder); }
                catch (Exception error) { WindowsServices.Log("refresh-desktop-launcher", error); }
            }
        }
        Dispatch(e.Args);
    }

    private async Task RunSmokeTest(bool checkDesktop)
    {
        try
        {
            Ui.Configure(AppAppearance.Dark);
            var apps = WindowsServices.InstalledApps();
            if (apps.Count == 0) throw new InvalidOperationException("AppsFolder returned no launchable entries.");
            var folder = new Folder { Name = "تجريبي" };
            folder.Panel.Transparency = 100;
            folder.Panel.OutsideClose = false;
            folder.Entries.Add(new Entry { Name = "Notepad", Target = Path.Combine(Environment.SystemDirectory, "notepad.exe") });
            folder.Entries.Add(apps[0]);
            CheckShortcutLifecycle();
            if (checkDesktop) CheckRedirectedDesktop();
            CheckExplorerFolders();
            var data = new Preferences { Arabic = true };
            var managerView = new Manager(this);
            managerView.Show();
            var settings = new SettingsWindow(managerView, this);
            settings.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (settings.ActualHeight <= 0) throw new InvalidOperationException("Settings did not render.");
            settings.Close();
            var editor = new Editor(folder, data) { Owner = managerView };
            editor.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (editor.FlowDirection != FlowDirection.RightToLeft || editor.ActualWidth <= 0)
                throw new InvalidOperationException("Editor did not render in RTL.");
            editor.Close();
            managerView.Close();
            var panel = new PanelWindow(folder, data, _ => { }, () => { },
                import: paths => ImportEntries(folder, paths, [], () => true));
            panel.ShowNear(Forms.Cursor.Position);
            var transfer = new DataObject();
            transfer.SetData(DataFormats.FileDrop, new[] { Path.Combine(AppContext.BaseDirectory, "Demo.json") });
            if (!panel.Receive(transfer) || !folder.Entries.Any(entry => entry.Name == "Demo.json") || panel.Receive(transfer))
                throw new InvalidOperationException("The popup did not import a dropped file exactly once.");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!panel.IsVisible || !panel.FitsWorkArea()
                || panel.Preview.Background is not System.Windows.Media.SolidColorBrush { Color.A: 0 })
                throw new InvalidOperationException("Popup did not render within the monitor with a transparent backdrop.");
            panel.DismissImmediately();
            var about = new AboutWindow(new Words(false));
            about.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (AboutWindow.DisplayVersion != "1.4" || about.ActualHeight <= 0)
                throw new InvalidOperationException("The About window did not render version 1.4.");
            about.Close();
            Shutdown();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Shutdown(1);
        }
    }

    private static void CheckRedirectedDesktop()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(WindowsServices.Desktop, $"اختبار الاختصار {id:N}.lnk");
        var arguments = $"--open {id:D}";
        try
        {
            ShellShortcut.Save(path, Environment.ProcessPath!, arguments, AboutWindow.AppName, AppContext.BaseDirectory);
            if (!File.Exists(path) || !ShellShortcut.IsManaged(path, arguments))
                throw new InvalidOperationException("The Unicode shortcut could not be read from the redirected desktop.");
        }
        finally { File.Delete(path); }
    }

    private static void CheckExplorerFolders()
    {
        var desktop = Directory.CreateTempSubdirectory("DesktopFolders.ExplorerSmoke-");
        try
        {
            var existing = desktop.CreateSubdirectory("Work");
            File.WriteAllText(Path.Combine(existing.FullName, "keep.txt"), "unmanaged");
            var folder = new Folder { Name = "Work", ExplorerFolder = true, IconColor = "#72B755" };
            folder.Entries.Add(new Entry { Name = "Notes", Target = Path.Combine(Environment.SystemDirectory, "notepad.exe"), Icon = "office:Word" });
            folder.Entries.Add(new Entry { Name = "Docs", Target = "https://example.org/" });
            WindowsServices.SyncDesktop(folder, desktop.FullName);
            var path = ExplorerFolders.Find(folder.Id, desktop.FullName)
                ?? throw new InvalidOperationException("The desktop folder was not registered.");
            if (path == existing.FullName || !File.Exists(Path.Combine(path, "Notes.lnk"))
                || desktop.GetFiles("*.lnk").Length != 1
                || !File.Exists(Path.Combine(path, "Docs.url"))
                || !File.ReadAllText(Path.Combine(path, "desktop.ini")).Contains("IconResource=")
                || Directory.GetFiles(path, "*.ico").Length != 2
                || IconPresets.Image("office:Word") is null)
                throw new InvalidOperationException("Explorer folder, shortcuts, or icons were not created.");

            File.WriteAllText(Path.Combine(path, "Notes.lnk"), "user replacement");
            File.WriteAllText(Path.Combine(path, "Report.txt"), "copied into the Windows folder");
            var visible = ExplorerFolders.UserEntries(folder.Id, desktop.FullName);
            if (!visible.Any(entry => entry.Name == "Report.txt") || visible.Any(entry => entry.Name == "Docs"))
                throw new InvalidOperationException("The popup did not discover files copied into the Windows folder.");
            folder.Entries.RemoveAt(1);
            folder.Name = "Renamed";
            folder.IconColor = "#4998F4";
            var moved = ExplorerFolders.Sync(folder, desktop.FullName);
            if (Directory.Exists(path) || Path.GetFileName(moved) != "Renamed"
                || File.ReadAllText(Path.Combine(moved, "Notes.lnk")) != "user replacement"
                || File.Exists(Path.Combine(moved, "Docs.url"))
                || !File.Exists(Path.Combine(moved, "Notes (2).lnk")))
                throw new InvalidOperationException("Explorer folder update did not preserve user files.");
            ExplorerFolders.Detach(folder.Id, desktop.FullName);
            if (ExplorerFolders.Find(folder.Id, desktop.FullName) is not null || !Directory.Exists(moved)
                || !File.Exists(Path.Combine(moved, "Notes (2).lnk")))
                throw new InvalidOperationException("Detaching did not preserve the Windows folder.");
        }
        finally
        {
            foreach (var directory in desktop.GetDirectories())
            {
                foreach (var file in directory.GetFiles())
                {
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                }
                directory.Attributes = FileAttributes.Directory;
                directory.Delete();
            }
            foreach (var file in desktop.GetFiles()) file.Delete();
            desktop.Delete();
        }
    }

    private static void CheckShortcutLifecycle()
    {
        var root = Directory.CreateTempSubdirectory("DesktopFolders.Smoke-");
        var directory = root.CreateSubdirectory("سطح المكتب");
        try
        {
            var folder = new Folder { Name = "ألعاب" };
            WindowsServices.SyncLink(folder, directory.FullName);
            var initial = directory.GetFiles("*.lnk").Single();
            folder.Name = "ألعاب جديدة";
            WindowsServices.SyncLink(folder, directory.FullName);
            var renamed = directory.GetFiles("*.lnk").Single();
            if (initial.Name == renamed.Name) throw new InvalidOperationException("Shortcut was not renamed.");
            WindowsServices.RemoveLinks(folder.Id, directory: directory.FullName);
            if (directory.GetFiles("*.lnk").Length != 0) throw new InvalidOperationException("Shortcut was not removed.");
        }
        finally
        {
            foreach (var file in directory.GetFiles()) file.Delete();
            directory.Delete();
            root.Delete();
        }
    }

    private async Task Listen()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(channel, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(server);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(2000);
                var command = await reader.ReadLineAsync(timeout.Token);
                if (command is { Length: <= 262144 } && JsonSerializer.Deserialize<string[]>(command) is { } arguments)
                    await Dispatcher.InvokeAsync(() => Dispatch(arguments));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                WindowsServices.Log("activation-listener", error);
                await Task.Delay(250);
            }
        }
    }

    private void Dispatch(string[] arguments)
    {
        if (arguments is ["--new-folder"]) CreateFromDesktop();
        else if (arguments is ["--uninstall"]) Cleanup();
        else if (arguments is ["--open", var key, ..] && Guid.TryParse(key, out var id))
        {
            var folder = Data.Folders.FirstOrDefault(item => item.Id == id);
            if (folder is null) MessageBox.Show(Words.Get("This folder no longer exists.", "لم يعد هذا المجلد موجودًا."));
            else _ = Open(folder, arguments.Skip(2).ToArray());
        }
        else if (arguments is not ["--tray"]) ShowManager();
    }

    private void CreateFromDesktop()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (NewFolderDialog.Ask(null, Words) is not { } folder) return;
            var viewModel = manager?.ViewModel ?? new FolderManagerViewModel(Data, Save);
            if (viewModel.Add(folder)) TryShell(() => WindowsServices.SyncDesktop(folder));
        }
        finally { busy = false; }
    }

    private void Cleanup()
    {
        if (!Store.ReadOnly && Store.Recovery is null)
        {
            foreach (var folder in Data.Folders)
                TryShell(() => WindowsServices.RemoveLinks(folder.Id));
        }
        TryShell(() => WindowsServices.SetStartup(false));
        TryShell(() => WindowsServices.SetDesktopMenu(false, Words));
        if (Data.StartWithWindows)
        {
            Data.StartWithWindows = false;
            if (!Store.ReadOnly) Save();
        }
        Shutdown();
    }

    internal void BuildTray()
    {
        tray?.Dispose();
        var menu = new Forms.ContextMenuStrip { RightToLeft = Data.Arabic ? Forms.RightToLeft.Yes : Forms.RightToLeft.No };
        menu.Items.Add(Words.Get("Manage folders", "إدارة المجلدات"), null, (_, _) => ShowManager());
        menu.Items.Add(Words.Get("About", "حول التطبيق"), null, (_, _) => new AboutWindow(Words).ShowDialog());
        menu.Items.Add(Words.Get("Exit", "خروج"), null, (_, _) => Shutdown());
        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = AboutWindow.AppName,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowManager();
    }

    private void ShowManager()
    {
        if (manager is null)
        {
            manager = new Manager(this);
            manager.Closed += (_, _) => manager = null;
            manager.Show();
        }
        if (manager.WindowState == WindowState.Minimized) manager.WindowState = WindowState.Normal;
        manager.Activate();
    }

    internal async Task Open(Folder folder, string[]? dropped = null)
    {
        if (busy) return;
        busy = true;
        try
        {
            popup?.DismissImmediately();
            var anchor = Forms.Cursor.Position;
            if (folder.Locked && !await Authorize())
                return;
            if (!Data.Folders.Contains(folder)) return;
            IReadOnlyList<Entry> external = [];
            if (folder.ExplorerFolder && !folder.Locked)
            {
                try { external = ExplorerFolders.UserEntries(folder.Id, WindowsServices.Desktop); }
                catch (Exception error) { WindowsServices.Log("read-explorer-folder", error); }
            }
            bool Add(string[] paths)
            {
                if (!Data.Folders.Contains(folder) || !ImportEntries(folder, paths, external, Save)) return false;
                if (folder.ExplorerFolder && !folder.Locked)
                    TryShell(() => ExplorerFolders.Sync(folder, WindowsServices.Desktop));
                return true;
            }
            if (dropped is { Length: > 0 }) Add(dropped);
            popup = new PanelWindow(folder, Data, Launch, () => Save(), import: Add, externalEntries: external);
            popup.Closed += (_, _) => popup = null;
            popup.ShowNear(anchor);
        }
        catch (Exception error)
        {
            WindowsServices.Log("open-folder", error);
            MessageBox.Show(Words.Get("The folder panel could not open. See the local error log.", "تعذر فتح لوحة المجلد. راجع سجل الأخطاء المحلي."));
        }
        finally { busy = false; }
    }

    private static bool ImportEntries(Folder folder, string[] paths, IReadOnlyList<Entry> external, Func<bool> save)
    {
        var additions = Targets.NewEntries(paths, folder.Entries.Concat(external)).ToArray();
        if (additions.Length == 0) return false;
        foreach (var entry in additions) folder.Entries.Add(entry);
        if (save()) return true;
        foreach (var entry in additions) folder.Entries.Remove(entry);
        return false;
    }

    internal async Task Edit(Folder folder)
    {
        if (busy || manager is null) return;
        busy = true;
        try
        {
            popup?.DismissImmediately();
            if (folder.Locked && !await Authorize()) return;
            var owner = manager;
            if (owner is not { IsVisible: true } || !Data.Folders.Contains(folder)) return;
            var editor = new Editor(Store.Clone(folder), Data) { Owner = owner };
            if (editor.ShowDialog() == true && owner.ViewModel.Apply(folder, editor.Draft))
                TryShell(() => WindowsServices.SyncDesktop(editor.Draft));
        }
        finally { busy = false; }
    }

    private async Task<bool> Authorize()
    {
        if (manager is { IsVisible: true, WindowState: WindowState.Normal })
            return await Hello.Verify(manager, Words);

        var gate = new Window { Width = 380, Height = 130, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize };
        Ui.SetWindow(gate, Words, Words.Get("Locked folder", "مجلد مقفل"));
        gate.Content = new System.Windows.Controls.Border { Padding = new(18),
            Child = Ui.Text(Words.Get("Waiting for Windows Hello verification…", "بانتظار التحقق بواسطة Windows Hello…")) };
        gate.Show();
        try { return await Hello.Verify(gate, Words) && gate.IsVisible; }
        finally { if (gate.IsVisible) gate.Close(); }
    }

    internal bool Save()
    {
        try { Store.Save(Data); return true; }
        catch (Exception error)
        {
            WindowsServices.Log("save-settings", error);
            MessageBox.Show(Words.Get("Settings could not be saved. See the local error log.", "تعذر حفظ الإعدادات. راجع سجل الأخطاء المحلي."));
            return false;
        }
    }

    internal bool ApplySettings(AppAppearance appearance, bool arabic, bool startup, bool desktopMenu)
    {
        var previous = (Data.Appearance, Data.Arabic, Data.StartWithWindows, DesktopMenu: WindowsServices.HasDesktopMenu());
        var startupChanged = startup != previous.StartWithWindows;
        var desktopChanged = desktopMenu != previous.DesktopMenu || (desktopMenu && arabic != previous.Arabic);
        try
        {
            if (startupChanged) WindowsServices.SetStartup(startup);
            if (desktopChanged) WindowsServices.SetDesktopMenu(desktopMenu, new Words(arabic));
            Data.Appearance = appearance;
            Data.Arabic = arabic;
            Data.StartWithWindows = startup;
            if (Save())
            {
                Ui.Configure(Data.Appearance);
                manager?.Build();
                popup?.DismissImmediately();
                if (arabic != previous.Arabic) BuildTray();
                return true;
            }
        }
        catch (Exception error)
        {
            WindowsServices.Log("apply-settings", error);
            MessageBox.Show(Words.Get("Could not apply the settings.", "تعذر تطبيق الإعدادات."));
        }
        Data.Appearance = previous.Appearance;
        Data.Arabic = previous.Arabic;
        Data.StartWithWindows = previous.StartWithWindows;
        if (startupChanged) TryShell(() => WindowsServices.SetStartup(previous.StartWithWindows));
        if (desktopChanged) TryShell(() => WindowsServices.SetDesktopMenu(previous.DesktopMenu, Words));
        return false;
    }

    private void OnSystemColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            manager?.Build();
            popup?.Refresh();
        });
    }

    internal void TryShell(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            WindowsServices.Log("update-windows", error);
            MessageBox.Show(Words.Get("Windows could not update the desktop item or setting. See the local error log.",
                "تعذر على Windows تحديث عنصر سطح المكتب أو الإعداد. راجع سجل الأخطاء المحلي."));
        }
    }

    private void Launch(Entry entry)
    {
        try { WindowsServices.Launch(entry); popup?.Dismiss(); }
        catch (Exception error)
        {
            WindowsServices.Log("launch-target", error);
            MessageBox.Show(Words.Get("Windows could not open this shortcut. Check its target in the folder manager.", "تعذر فتح الاختصار. تحقق من وجهته في مدير المجلدات."));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance) Ui.WindowsColors.ColorValuesChanged -= OnSystemColorsChanged;
        stop.Cancel();
        if (tray is not null) { tray.Visible = false; tray.Dispose(); }
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
