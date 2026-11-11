# Desktop Folders

Desktop Folders puts Android-inspired app folders on the Windows desktop. Open a folder shortcut to see its apps and links in a floating grid near the pointer. Each folder has its own desktop shortcut, settings, and layout.

## Application specifications

| Area | Details |
| --- | --- |
| Platform | Windows 10 build 19044 or later and Windows 11 on x64. Built in C# with .NET 10 and WPF. The self-contained package includes the .NET Desktop Runtime. |
| Folder management | Create, rename, duplicate, delete, reorder, and assign custom folder icons. Manage folders from the Start menu or tray. Start with Windows is optional. |
| Shortcuts | Add installed apps from Windows AppsFolder, local `.exe`, `.lnk`, and `.url` files, and HTTP/HTTPS addresses. Use the file picker or drop files into the editor. Rename shortcuts, change targets, apply custom icons, and reorder by dragging or using buttons. |
| Panel | Opens at the pointer or at its saved position. The window fits within the selected monitor’s work area and uses per-monitor DPI awareness. Set width from 240 to 1200 and height from 160 to 1000 device-independent pixels, subject to the monitor’s available space. Drag the panel title to move it and the corner grip to resize it. |
| Layout and appearance | Choose 1–10 columns, icon size, spacing, padding, label visibility and size, and corner radius. The grid scrolls vertically. Light, dark, custom solid, and translucent backgrounds support 0–100% transparency. At 100% the backdrop is invisible and the icons and contrast labels remain interactive. Native acrylic and mica are outside this release. |
| Motion | Opening and closing transitions and optional icon hover effects. Choose a duration up to 400 ms and use the global animation setting or a folder override. Windows reduced-motion preferences take precedence. Locked panels close immediately. |
| Lock | Optional Windows Hello verification for opening and editing folders on Windows 11. Windows presents the configured PIN or biometric verification options. Each close ends the unlocked popup session, and an optional idle timeout closes the panel. |
| Language and input | English and Arabic with right-to-left layout. Navigate with Tab or arrow keys, press Enter to launch, and press Esc to close. Outside-click dismissal is configurable. |
| Storage and privacy | Per-user JSON settings, atomic saves, one previous backup, and local diagnostic logs. The app needs no account or administrator privileges and has no telemetry. Links open in their registered Windows applications. |

## Install and open

1. Run `DesktopFolders-Setup-win-x64.exe` and open **Desktop Folders** from Start. Setup installs for the current user. The supplied installer is unsigned.
2. For a portable install, extract the complete `DesktopFolders-win-x64.zip` to a permanent directory and run `DesktopFolders.exe` inside it. The runtime and `Demo.json` travel with the executable.
3. Choose **New folder**, enter a name, and select the folder. **Add demo folder** creates a folder with Notepad, Calculator where installed, and Microsoft Learn.
4. Choose **Edit**. In **Shortcuts**, use **Add files**, **Installed apps**, or **Add URL**. Drag shortcuts in the list to change their order. Use **Change target** to repair a moved file. Removing a shortcut leaves its source file in place.
5. Use **Appearance** to change the dimensions, grid, colors, and transparency while viewing the live preview. Use **Behavior** for placement, dismissal, animation, and Windows Hello settings. Choose **Save changes**.
6. Double-click the matching Windows desktop shortcut to open the panel. Its filename includes a short ID to prevent collisions. Select a tile to launch the app or link.

Double-click the tray icon to reopen the manager. Right-click a selected folder for management actions. Use **Repair desktop shortcut** after moving the portable app or if a desktop shortcut is missing. To switch languages, use **العربية** or **English** at the bottom of the manager. Custom icon files are referenced at their chosen paths.

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

The publish script runs the Windows smoke test against the self-contained executable and writes the executable directory, portable ZIP, and optional installer to `artifacts`. Automated tests cover settings bounds, target classification, folder operations, atomic persistence, and recovery. The smoke test covers AppsFolder enumeration, Windows shortcut creation/rename/removal in a temporary directory, Arabic editor rendering, and a fully transparent popup inside the active monitor’s work area. Verify successful Hello enrollment and authentication, physical mixed-DPI placement, and Windows 10 behavior on representative machines.

## Architecture and local data

- `src/DesktopFolders.Core` contains models, the folder manager view model, shortcut validation, and the JSON store.
- `src/DesktopFolders` contains WPF views, tray and single-instance activation, and Windows Shell and Hello integration.
- `tests/DesktopFolders.Tests` contains MSTest tests. `samples/Demo.json` supplies the demo shortcuts.
- `installer/DesktopFolders.iss` builds the installer. The uninstall action removes app-managed desktop shortcuts and startup registration and preserves saved folders.

The app stores settings at `%LOCALAPPDATA%\DesktopFolders\folders.json` and keeps a previous snapshot in `folders.json.bak`. It moves invalid JSON aside and tries the backup. Look in `errors.log` in the same directory for operation names and error codes when Windows cannot save, create a shortcut, or launch a target.
