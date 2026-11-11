# Desktop Pop-up folder

Desktop Pop-up folder 1.4_1 gives each folder a Windows desktop shortcut, including on redirected desktops and paths with Arabic names. Double-click to open the floating folder. Drag items in to move them, or use Ctrl+V to add links.

[سجل التحديثات بالعربية](CHANGELOG.md)

## Application specifications

| Area | Details |
| --- | --- |
| Platform | Windows 10 build 19044 or later and Windows 11 on x64. Built in C# with .NET 10 and WPF. The self-contained package includes the .NET Desktop Runtime. |
| Folder management | Create popup folders from the desktop context menu or manager. Each gets one desktop shortcut. Rename, duplicate, reorder, and repair folders. Choose the Windows folder icon or tint the same shape with a custom color. |
| Shortcuts | Drop app shortcuts or files into the popup to move them into app storage. Use Ctrl+V to add a reference to the original. The manager also supports installed AppsFolder entries, web addresses, and individual app icons. |
| Panel | Opens at the pointer or at its saved position. The window fits within the selected monitor’s work area and uses per-monitor DPI awareness. Set width from 240 to 1200 and height from 160 to 1000 device-independent pixels, subject to the monitor’s available space. Drag the panel title to move it and the corner grip to resize it. |
| Layout and appearance | Choose 1–10 columns, icon size, spacing, padding, labels, and corner radius. Set the app to light, dark, or follow Windows, with the Windows accent color on controls. Each popup can follow the Windows background and text colors or use its own appearance and transparency. |
| Motion and dismissal | Choose animations when creating or editing a folder. Windows reduced-motion preferences take precedence. Show or hide the corner **×** per folder. Press Esc to close at any time. Locked panels close immediately. |
| Lock | Popup folders support optional Windows Hello verification on Windows 11. Windows presents the configured PIN or biometric options. |
| Language and input | English and Arabic with right-to-left layout. Navigate with Tab or arrow keys, press Enter to launch, and press Esc to close. Outside-click dismissal is configurable. |
| Storage and privacy | Per-user JSON settings, atomic saves, one previous backup, and local diagnostic logs. The app needs no account or administrator privileges and has no telemetry. Links open in their registered Windows applications. |

## Install and open

1. Run `DesktopFolders-Setup-win-x64.exe` and open **Desktop Pop-up folder** from Start. Setup installs for the current user. The installer is unsigned.
2. For a portable install, extract the complete `DesktopFolders-win-x64.zip` to a permanent directory and run `DesktopFolders.exe` inside it. The runtime and `Demo.json` travel with the executable.
3. Right-click a blank area of the desktop and choose **New Desktop Folder**. On Windows 11, look under **Show more options**. For a portable install, first enable **Desktop right-click command** under the gear icon in the manager. You can also use the **+** icon in the manager. Hover over an icon to see its action.
4. Double-click the desktop shortcut for the folder. You can open it with the manager closed.
5. Drag an app shortcut or a file from File Explorer or the desktop into the popup to move it into local storage. Dragging an installed app’s shortcut moves the shortcut. To leave the source file in its current location, copy it and press **Ctrl+V** in the popup.
6. Use **Folder icon color** during creation or under **Edit → Shortcuts**. Choose the standard Windows folder color or tint the same shape and see the result immediately in the color picker.
7. Use **Appearance** for layout, transparency, and **Follow Windows**. Use **Behavior** for animations and the optional **×**. Edits and settings save automatically. **OK** closes the dialog. **About** shows version 1.4_1, the copyright notice, and separate GitHub profile and source links.

When you start version 1.4_1, it consolidates shortcuts pointing to the same popup and removes the internal ID from the filename. If a normal Windows folder shares the name, the shortcut uses **(Pop-up)** to distinguish it. It moves older app-created folders to local storage. User-created folders stay on the desktop. Use **Open stored files** in the folder’s menu to find moved items.

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

The publish script runs the Windows smoke test against the self-contained executable and writes the executable directory, portable ZIP, and optional installer to `artifacts`. The smoke test covers Arabic Shell paths, duplicate shortcut cleanup, migration of app-owned folders, file moves and rollback, dark Settings, and the About window. Verify Windows Hello authentication, physical mixed-DPI placement, and Windows 10 behavior on representative machines.

## Architecture and local data

- `src/DesktopFolders.Core` contains models, the folder manager view model, shortcut validation, and the JSON store.
- `src/DesktopFolders` contains WPF views, tray and single-instance activation, and Windows Shell and Hello integration.
- `tests/DesktopFolders.Tests` contains MSTest tests. `samples/Demo.json` supplies the demo shortcuts.
- `installer/DesktopFolders.iss` builds the installer and registers the desktop context-menu command for the current user. Uninstall removes that command, popup shortcuts, and startup registration. Stored files are preserved.

The app stores settings at `%LOCALAPPDATA%\DesktopFolders\folders.json`, keeps the previous snapshot in `folders.json.bak`, and keeps moved files under `%LOCALAPPDATA%\DesktopFolders\Files`. It moves invalid JSON aside and tries the backup. Look in `errors.log` for operation names and error codes.
