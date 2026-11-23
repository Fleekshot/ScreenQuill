# ScreenQuill

ScreenQuill is a native Windows 10/11 screen-annotation tool. It shows a floating toolbar and lets you draw on transparent, always-on-top overlays above any application. It was inspired by Epic Pen Pro and written independently: it contains no Epic Pen code, assets or branding.

## Features

- Pen and highlighter, each with its own width
- Eraser and clear-all
- Line, arrow, rectangle and ellipse; hold Shift to snap or constrain
- Text tool
- Whiteboard and blackboard
- Fading ink with short, medium and long durations
- Highlighted-cursor halo
- 24 preset colours, a custom colour picker and 4 quick-pick slots
- Region screenshot to a PNG file and/or the clipboard
- Ghost Mode (hides the toolbar)
- Editable global hotkeys with conflict detection
- Pen pressure and multi-touch
- Multi-monitor and per-monitor DPI support
- Tray icon
- Undo and redo (an extra; Epic Pen doesn't document it)

See [docs/feature-matrix.md](docs/feature-matrix.md) for the verified status of each feature.

## Install

1. Download **ScreenQuill-Setup.exe** from the [latest release](https://github.com/Fleekshot/ScreenQuill/releases/latest).
2. Run it and click **Install**.

Setup downloads the latest `ScreenQuill-win-x64.zip` from GitHub Releases and checks its SHA-256. It then:
- installs to `%LOCALAPPDATA%\Programs\ScreenQuill` for your account, with no admin rights needed;
- adds a Start Menu shortcut, and a desktop shortcut if you tick the box;
- registers ScreenQuill under **Settings → Apps**, so you can uninstall it there.

Run Setup again to update. For a scripted install, use `ScreenQuill-Setup.exe /silent`.

Setup and the app aren't code-signed, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**.

## Build and run

You need the .NET 8 SDK (`global.json` pins 8.0.200 or a later feature band).

```powershell
dotnet build
```

```powershell
dotnet test
```

```powershell
.\src\ScreenQuill\bin\Debug\net8.0-windows\ScreenQuill.exe
```

To build a self-contained release folder:

```powershell
dotnet publish src\ScreenQuill -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Administrator rights are not needed.

To build the installer: `dotnet build installerScreenQuill.Setup -c Release` (output: `installerScreenQuill.SetupinRelease
et48ScreenQuill-Setup.exe`).

## Using it

- **Logo:** drag it to move the toolbar, click it to collapse the toolbar, right-click it for the menu.
- **Flyouts:** buttons with a small triangle open one. Click the triangle, or right-click the button.
- **Quick colours:** right-click a quick-colour slot to store the current colour in it.
- **Cursor mode:** clicks, scrolling and typing reach the apps underneath, and your ink stays visible.

### Default hotkeys (project defaults)

The official Epic Pen default bindings couldn't be verified, so ScreenQuill uses its own defaults. All of them can be changed in Settings → Hotkeys.

| Action | Keys | Action | Keys |
| --- | --- | --- | --- |
| Cursor | Ctrl+Alt+C | Screenshot | Ctrl+Alt+S |
| Pen | Ctrl+Alt+P | Screenshot to clipboard | Ctrl+Alt+X |
| Highlighter | Ctrl+Alt+H | Clear all | Ctrl+Alt+D |
| Eraser | Ctrl+Alt+E | Undo / Redo | Ctrl+Alt+Z / Ctrl+Alt+Y |
| Text | Ctrl+Alt+T | Whiteboard / Blackboard | Ctrl+Alt+W / Ctrl+Alt+B |
| Line / Arrow | Ctrl+Alt+L / Ctrl+Alt+A | Cycle fading ink | Ctrl+Alt+F |
| Rectangle / Ellipse | Ctrl+Alt+R / Ctrl+Alt+O | Highlighted cursor | Ctrl+Alt+M |
| Bigger / smaller | Ctrl+Alt+= / Ctrl+Alt+- | Show/hide ink | Ctrl+Alt+V |
| Next quick colour | Ctrl+Alt+N | Ghost Mode | Ctrl+Alt+G |

Pressing an active tool's hotkey again switches back to Cursor mode.

### Default fade durations (project defaults)

Short is 2 s, Medium 5 s and Long 10 s, each followed by a 1 s fade-out. Epic Pen doesn't publish its numbers. You can change these in Settings → Fading & Cursor.

## Files

- Settings: `%APPDATA%\ScreenQuill\settings.json`
- Local error log: `%APPDATA%\ScreenQuill\error.log`
- Screenshots: `Pictures\ScreenQuill` by default

Nothing is uploaded.

## Test results

- **Unit tests:** `dotnet test` passed 61 of 61 (geometry, model, undo, fade timing, tool state, hotkeys, persistence, palette, naming).
- **Manual checks:** done on Windows 11 with one 1920×1080 monitor at 100% scaling. They covered:
  - drawing over apps, then clicking and typing in Notepad in Cursor mode while the ink stayed visible;
  - the highlighter;
  - all four shapes, dragged in every direction;
  - text;
  - the eraser;
  - both boards;
  - region screenshot to file and clipboard, and Esc to cancel;
  - Ghost Mode;
  - the halo;
  - Short fading;
  - flyouts;
  - hotkey conflict detection;
  - a rebound hotkey still working after a restart;
  - colour persistence.
- **Not yet checked by hand:** custom colour picker, toolbar drag and collapse, tray menu, Medium and Long fade, exit cleanup.
- **Not hardware-tested:** pen pressure, multi-touch, multiple monitors, mixed DPI.

## Known limitations

- Exclusive-fullscreen games and apps can draw above the overlays. Use borderless or windowed mode.
- Screen sharing or recording of a **single window** (Zoom, Teams, OBS window capture) won't include the ink. Share the whole display instead.
- The overlays can't appear on the secure desktop: UAC prompts, the lock screen, Ctrl+Alt+Del.
- Ctrl+Alt hotkeys can briefly show access-key hints in some apps, such as Notepad. Rebind them in Settings if this bothers you.
- Annotations aren't saved when you quit.
- The eraser removes whole objects. It can't cut part of a stroke.
