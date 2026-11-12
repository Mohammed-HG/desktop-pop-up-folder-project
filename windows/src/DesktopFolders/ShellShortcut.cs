using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DesktopFolders;

internal static class ShellShortcut
{
    private static readonly Guid ClassId = new("00021401-0000-0000-C000-000000000046");

    public static bool IsManaged(string path, string arguments) => LauncherArguments(path) == arguments;

    public static bool IsFolderLauncher(string path) => LauncherArguments(path) is not null;

    private static string? LauncherArguments(string path)
    {
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return WithLink(link =>
            {
                ((IPersistFile)link).Load(path, 0);
                var buffer = new StringBuilder(512);
                link.GetArguments(buffer, buffer.Capacity);
                var arguments = buffer.ToString();
                if (!arguments.StartsWith("--open ", StringComparison.Ordinal) || !Guid.TryParse(arguments[7..], out var id))
                    return null;
                buffer.Clear();
                link.GetDescription(buffer, buffer.Capacity);
                return buffer.ToString() is "Desktop Folders" or AboutWindow.AppName ? $"--open {id:D}" : null;
            });
        }
        catch (COMException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void Save(string path, string? target, string arguments = "", string? description = null,
        string? workingDirectory = null, string? icon = null, int iconIndex = 0)
    {
        WithLink(link =>
        {
            var file = (IPersistFile)link;
            if (target is null) file.Load(path, 0);
            else
            {
                link.SetPath(target);
                link.SetArguments(arguments);
                if (description is not null) link.SetDescription(description);
                if (workingDirectory is not null) link.SetWorkingDirectory(workingDirectory);
            }
            if (icon is not null) link.SetIconLocation(icon, iconIndex);
            file.Save(path, true);
            return true;
        });
    }

    private static T WithLink<T>(Func<IShellLinkW, T> action)
    {
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId, throwOnError: true)!)!;
        try { return action((IShellLinkW)instance); }
        finally { Marshal.ReleaseComObject(instance); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, nint data, uint flags);
        void GetIDList(out nint item);
        void SetIDList(nint item);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string value, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string value, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string value);
    }
}
