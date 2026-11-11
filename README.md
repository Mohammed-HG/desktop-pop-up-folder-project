# Desktop Pop-up folder

Desktop Pop-up folder 1.4_3 gives each folder a Windows desktop shortcut, including on redirected desktops and paths with Arabic names. Double-click to open the floating folder. Drag items in or press Ctrl+V to move them into app storage. Drag a tile back out to move it to the desktop or File Explorer.

[سجل التحديثات بالعربية](CHANGELOG.md)

## Application specifications

| Area | Details |
| --- | --- |
| Platform | Windows 10 build 19044 or later and Windows 11 on x64. Built in C# with .NET 10 and WPF. The self-contained package includes the .NET Desktop Runtime. |
| Folder management | Create popup folders from the desktop context menu or manager. Each gets one desktop shortcut using the folder name. Choose the Windows folder icon, tint it, or add a transparent symbol in outline or solid style. |
| Shortcuts | Drag or paste app shortcuts and files into the popup to move them into local storage. The manager also supports adding references to installed AppsFolder entries, web addresses, and files. |
| Panel | Opens at the pointer or at its saved position. The window fits within the selected monitor’s work area and uses per-monitor DPI awareness. Set width from 240 to 1200 and height from 160 to 1000 device-independent pixels, subject to the monitor’s available space. Drag the panel title to move it and the corner grip to resize it. |
| Layout and appearance | Set up to 10 columns. Equal tiles wrap as the popup gets narrower, with aligned labels and rows. Windows supplies larger icon sources when available. Set the app and popup to light, dark, or follow Windows, or customize the popup background and transparency. |
| Motion and dismissal | Choose animations when creating or editing a folder. Windows reduced-motion preferences take precedence. Show or hide the corner **×** per folder. Press Esc to close at any time. Locked panels close immediately. |
| Lock | Popup folders support optional Windows Hello verification on Windows 11. Windows presents the configured PIN or biometric options. |
| Language and input | English and Arabic with right-to-left layout. Navigate with Tab or arrow keys, press Enter to launch, and press Esc to close. Outside-click dismissal is configurable. |
| Storage and privacy | Per-user JSON settings, atomic saves, one previous backup, and local diagnostic logs. The app needs no account or administrator privileges and has no telemetry. Links open in their registered Windows applications. |

## Install and open

1. Run `DesktopFolders-Setup-win-x64.exe` and open **Desktop Pop-up folder** from Start. Setup installs for the current user. The installer is unsigned.
2. For a portable install, extract the complete `DesktopFolders-win-x64.zip` to a permanent directory and run `DesktopFolders.exe` inside it. The runtime and `Demo.json` travel with the executable.
3. Right-click a blank area of the desktop and choose **New Desktop Folder**. On Windows 11, look under **Show more options**. For a portable install, first enable **Desktop right-click command** under the gear icon in the manager. You can also use the **+** icon in the manager. Hover over an icon to see its action.
4. Double-click the desktop shortcut for the folder. You can open it with the manager closed.
5. Drag a file or app shortcut from File Explorer or the desktop into the popup, or copy it with **Ctrl+C** and press **Ctrl+V** in the popup. Both actions move the selected item into local storage and remove it from the original location. A Windows shortcut points to its installed app, so moving that shortcut leaves the installed application where Windows installed it.
6. Use **Folder icon color** and **Folder symbol** during creation or under **Edit → Shortcuts**. Choose a color and optionally add a screwdriver, wrench, hammer, gear, home, cloud, star, or lightning symbol. The picker previews the outline and solid styles.
7. To move a stored item out, drag its tile to the desktop or a File Explorer folder. The tile disappears after Windows moves the file. An item added as an external reference moves out as a Windows shortcut.
8. Use **Appearance** for layout, transparency, and **Follow Windows**. Use **Behavior** for animations and the optional **×**. Edits and settings save automatically. **OK** closes the dialog. **About** shows version 1.4_3, the copyright notice, and separate GitHub profile and source links.

On startup, version 1.4_2 renames app-owned desktop shortcuts to the folder’s name and consolidates duplicates. A Windows directory with the same display name can coexist with the shortcut. An unrelated shortcut with the same filename is preserved. Use **Open stored files** in the folder’s menu to find moved items and folders migrated from earlier releases.

Double-click the tray icon to reopen the manager. Use **Repair desktop item** to recreate a missing popup shortcut. **Settings** contains startup, the desktop right-click command, language, and appearance. If you move the portable app, toggle the desktop command off and on to update its path. Removing a popup preserves its stored files.

## Lock a folder with Windows Hello

On Windows 11, configure Windows Hello under **Settings → Accounts → Sign-in options**. Open the folder editor, go to **Behavior**, enable **Lock with Windows Hello**, and save. The first save requires successful verification. If verification becomes unavailable later, locked folders stay closed. The desktop verification API requires Windows build 22000 or later, so the lock setting is disabled on Windows 10. [Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/userconsentverifierinterop/nf-userconsentverifierinterop-iuserconsentverifierinterop-requestverificationforwindowasync)

The lock gates the panel and editor. The original apps and files remain accessible through Windows, and the settings file is plain JSON under your Windows account. The app never receives your PIN or biometrics.

## Build, test, and package

Open `DesktopFolders.sln` in Visual Studio with .NET desktop development and the .NET 10 SDK, or run these commands from the project root in an interactive Windows PowerShell session.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

Install [Inno Setup 6](https://jrsoftware.org/isdl.php) and pass `-Installer` to compile the per-user installer. Use `-Iscc 'C:\path\to\ISCC.exe'` if the compiler is installed in a custom directory.

```powershell
winget install --id JRSoftware.InnoSetup --exact --source winget --scope user
powershell -ExecutionPolicy Bypass -File .\scripts\Publish.ps1 -Installer
```

The publish script runs the Windows smoke test against the self-contained executable and writes the executable directory, portable ZIP, and optional installer to `artifacts`. The smoke test covers Arabic Shell paths, duplicate shortcut cleanup, file moves and rollback, tile alignment at multiple widths, the 256 px icon frames, symbol previews, and the About window. Verify Windows Hello authentication, physical mixed-DPI placement, and Windows 10 behavior on representative machines.

## Architecture and local data

- `src/DesktopFolders.Core` contains models, the folder manager view model, shortcut validation, and the JSON store.
- `src/DesktopFolders` contains WPF views, tray and single-instance activation, and Windows Shell and Hello integration.
- `tests/DesktopFolders.Tests` contains MSTest tests. `samples/Demo.json` supplies the demo shortcuts.
- `installer/DesktopFolders.iss` builds the installer and registers the desktop context-menu command for the current user. Uninstall removes that command, popup shortcuts, and startup registration. Stored files are preserved.

The app stores settings at `%LOCALAPPDATA%\DesktopFolders\folders.json`, keeps the previous snapshot in `folders.json.bak`, and keeps moved files under `%LOCALAPPDATA%\DesktopFolders\Files`. It moves invalid JSON aside and tries the backup. Look in `errors.log` for operation names and error codes.
