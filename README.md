# Desktop Folders

Desktop Folders creates ordinary Windows desktop folders for apps, links, and files. Create one from the desktop right-click menu, give it a colored folder icon, and double-click it to open File Explorer. The manager also offers an Android-inspired floating panel.

## Application specifications

| Area | Details |
| --- | --- |
| Platform | Windows 10 build 19044 or later and Windows 11 on x64. Built in C# with .NET 10 and WPF. The self-contained package includes the .NET Desktop Runtime. |
| Folder management | Create normal folders from the desktop context menu or manager. Rename, duplicate, reorder, and repair managed folders. Choose the Windows default icon, a preset color, a custom hex color, an Office icon, or an image. Start with Windows is optional. |
| Shortcuts | Add installed apps from Windows AppsFolder, local `.exe`, `.lnk`, and `.url` files, and HTTP/HTTPS addresses. Use the file picker or drop files into the editor. Choose a custom image or a built-in Office-style icon for Word, Excel, PowerPoint, Outlook, OneNote, Access, Publisher, or Teams. |
| Panel | Opens at the pointer or at its saved position. The window fits within the selected monitor’s work area and uses per-monitor DPI awareness. Set width from 240 to 1200 and height from 160 to 1000 device-independent pixels, subject to the monitor’s available space. Drag the panel title to move it and the corner grip to resize it. |
| Layout and appearance | Choose 1–10 columns, icon size, spacing, padding, label visibility and size, and corner radius. The grid scrolls vertically. Light, dark, custom solid, and translucent backgrounds support 0–100% transparency. At 100% the backdrop is invisible and the icons and contrast labels remain interactive. Native acrylic and mica are outside this release. |
| Motion | Opening and closing transitions and optional icon hover effects. Choose a duration up to 400 ms and use the global animation setting or a folder override. Windows reduced-motion preferences take precedence. Locked panels close immediately. |
| Lock | Existing popup folders support optional Windows Hello verification on Windows 11. Windows presents the configured PIN or biometric options. Normal Windows folders open directly in File Explorer. |
| Language and input | English and Arabic with right-to-left layout. Navigate with Tab or arrow keys, press Enter to launch, and press Esc to close. Outside-click dismissal is configurable. |
| Storage and privacy | Per-user JSON settings, atomic saves, one previous backup, and local diagnostic logs. The app needs no account or administrator privileges and has no telemetry. Links open in their registered Windows applications. |

## Install and open

1. Run `DesktopFolders-Setup-win-x64.exe` and open **Desktop Folders** from Start. Setup installs for the current user. The supplied installer is unsigned.
2. For a portable install, extract the complete `DesktopFolders-win-x64.zip` to a permanent directory and run `DesktopFolders.exe` inside it. The runtime and `Demo.json` travel with the executable.
3. Right-click a blank area of the desktop and choose **New Desktop Folder**. On Windows 11, look under **Show more options**. For a portable install, first enable **Desktop right-click command** in the manager. You can also use **New folder** or **Add demo folder** in the manager.
4. Double-click the new desktop folder to open it in File Explorer. You can put files and shortcuts in it as with any Windows folder. Files added directly in Explorer stay in the folder. To include a shortcut in the floating panel, add it through the manager.
5. In the manager, select the folder and choose **Edit → Shortcuts**. Choose **Folder icon color** for a palette or enter a custom `#RRGGBB` color. To apply an Office icon, select an app shortcut and choose **Office icon**, or choose **Office folder icon** to apply it to the folder itself. Choose **Save changes**.
6. Use **Add files**, **Installed apps**, or **Add URL** to add app-managed shortcuts. Drag them in the editor to change their order in the panel. File Explorer uses its own sort settings. **Change target** repairs a moved file. Removing a managed shortcut leaves its original file in place.
7. Choose **Open panel** in the manager for the floating grid. Use **Appearance** to customize its layout and transparency and **Behavior** to change how it opens and closes.

Existing popup shortcuts keep their behavior. To convert one, open **Edit → Shortcuts**, select **Use a normal Windows folder on the desktop**, and save. Turn off Windows Hello first if the popup is locked. The conversion replaces the app-managed desktop shortcut with an Explorer folder.

Double-click the tray icon to reopen the manager. Use **Repair desktop item** to recreate a missing desktop folder or shortcut. If you move the portable app, toggle **Desktop right-click command** off and on to update its path. Removing a normal folder from the manager leaves the Windows folder and its contents in place. To delete the directory itself, use File Explorer. Use **العربية** or **English** at the bottom of the manager to switch languages.

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

The publish script runs the Windows smoke test against the self-contained executable and writes the executable directory, portable ZIP, and optional installer to `artifacts`. Automated tests cover settings bounds, target classification, folder operations, atomic persistence, and recovery. The smoke test covers AppsFolder enumeration, Shell shortcut creation and removal, Explorer folder creation and renaming, color and Office icon assets, preservation of user files, Arabic editor rendering, and a fully transparent popup. Verify Windows Hello authentication, physical mixed-DPI placement, and Windows 10 behavior on representative machines.

## Architecture and local data

- `src/DesktopFolders.Core` contains models, the folder manager view model, shortcut validation, and the JSON store.
- `src/DesktopFolders` contains WPF views, tray and single-instance activation, and Windows Shell and Hello integration.
- `tests/DesktopFolders.Tests` contains MSTest tests. `samples/Demo.json` supplies the demo shortcuts.
- `installer/DesktopFolders.iss` builds the installer and registers the desktop context-menu command for the current user. Uninstall removes that command, popup shortcuts, and startup registration. It preserves normal Windows folders and their contents.

The app stores settings at `%LOCALAPPDATA%\DesktopFolders\folders.json` and keeps a previous snapshot in `folders.json.bak`. It moves invalid JSON aside and tries the backup. Look in `errors.log` in the same directory for operation names and error codes when Windows cannot save, create a shortcut, or launch a target.
