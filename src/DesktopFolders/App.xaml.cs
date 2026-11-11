using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
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
        if (e.Args is ["--smoke-ui"])
        {
            _ = RunSmokeTest();
            return;
        }

        channel = $"DesktopFolders-{WindowsIdentity.GetCurrent().User!.Value}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        instance = new Mutex(false, @"Local\" + channel);
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }

        var command = e.Args is ["--open", var id] && Guid.TryParse(id, out var key)
            ? key.ToString("D") : e.Args.Contains("--new-folder") ? "new-folder"
            : e.Args.Contains("--tray") ? "tray" : e.Args.Contains("--uninstall") ? "uninstall" : "manager";
        if (!ownsInstance)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", channel, PipeDirection.Out);
                client.Connect(2500);
                using var writer = new StreamWriter(client);
                writer.WriteLine(command);
            }
            catch (Exception error)
            {
                WindowsServices.Log("activate-instance", error);
                MessageBox.Show("Desktop Folders / مجلدات سطح المكتب — Could not contact the running app / تعذر الاتصال بالتطبيق");
            }
            Shutdown();
            return;
        }

        Data = Store.Load();
        if (command == "uninstall") { Cleanup(); return; }
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
        Dispatch(command);
    }

    private async Task RunSmokeTest()
    {
        try
        {
            var apps = WindowsServices.InstalledApps();
            if (apps.Count == 0) throw new InvalidOperationException("AppsFolder returned no launchable entries.");
            var folder = new Folder { Name = "تجريبي" };
            folder.Panel.Transparency = 100;
            folder.Panel.OutsideClose = false;
            folder.Entries.Add(new Entry { Name = "Notepad", Target = Path.Combine(Environment.SystemDirectory, "notepad.exe") });
            folder.Entries.Add(apps[0]);
            CheckShortcutLifecycle();
            CheckExplorerFolders();
            var data = new Preferences { Arabic = true };
            var managerView = new Manager(this);
            managerView.Show();
            var editor = new Editor(folder, data) { Owner = managerView };
            editor.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (editor.FlowDirection != FlowDirection.RightToLeft || editor.ActualWidth <= 0)
                throw new InvalidOperationException("Editor did not render in RTL.");
            editor.Close();
            managerView.Close();
            var panel = new PanelWindow(folder, data, _ => { }, () => { });
            panel.ShowNear(Forms.Cursor.Position);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!panel.IsVisible || !panel.FitsWorkArea()
                || panel.Preview.Background is not System.Windows.Media.SolidColorBrush { Color.A: 0 })
                throw new InvalidOperationException("Popup did not render within the monitor with a transparent backdrop.");
            panel.DismissImmediately();
            Shutdown();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Shutdown(1);
        }
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
            var path = ExplorerFolders.Sync(folder, desktop.FullName);
            if (path == existing.FullName || !File.Exists(Path.Combine(path, "Notes.lnk"))
                || !File.Exists(Path.Combine(path, "Docs.url"))
                || !File.ReadAllText(Path.Combine(path, "desktop.ini")).Contains("IconResource=")
                || Directory.GetFiles(path, "*.ico").Length != 2
                || IconPresets.Image("office:Word") is null)
                throw new InvalidOperationException("Explorer folder, shortcuts, or icons were not created.");

            File.WriteAllText(Path.Combine(path, "Notes.lnk"), "user replacement");
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
            desktop.Delete();
        }
    }

    private static void CheckShortcutLifecycle()
    {
        var directory = Directory.CreateTempSubdirectory("DesktopFolders.Smoke-");
        try
        {
            var folder = new Folder { Name = "First" };
            WindowsServices.SyncLink(folder, directory.FullName);
            var initial = directory.GetFiles("*.lnk").Single();
            folder.Name = "Renamed";
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
                if (command is not null) await Dispatcher.InvokeAsync(() => Dispatch(command));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                WindowsServices.Log("activation-listener", error);
                await Task.Delay(250);
            }
        }
    }

    private void Dispatch(string command)
    {
        if (command == "manager") ShowManager();
        else if (command == "new-folder") CreateFromDesktop();
        else if (command == "uninstall") Cleanup();
        else if (Guid.TryParse(command, out var id))
        {
            var folder = Data.Folders.FirstOrDefault(item => item.Id == id);
            if (folder is null) MessageBox.Show(Words.Get("This folder no longer exists.", "لم يعد هذا المجلد موجودًا."));
            else _ = Open(folder);
        }
    }

    private void CreateFromDesktop()
    {
        if (busy) return;
        busy = true;
        try
        {
            var name = Ui.Prompt(null, Words, Words.Get("New Desktop Folder", "مجلد سطح مكتب جديد"));
            if (name is null) return;
            var folder = new Folder { Name = name, ExplorerFolder = true };
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
        menu.Items.Add(Words.Get("Exit", "خروج"), null, (_, _) => Shutdown());
        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = Words.Get("Desktop Folders", "مجلدات سطح المكتب"),
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

    internal async Task Open(Folder folder)
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
            popup = new PanelWindow(folder, Data, Launch, () => Save());
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
        stop.Cancel();
        if (tray is not null) { tray.Visible = false; tray.Dispose(); }
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
