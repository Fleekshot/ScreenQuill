# Architecture

**Stack:** C# / .NET 8. The UI is WPF, with Win32 interop for overlay window styles, global hotkeys, the low-level mouse hook, GDI screen capture and the layered halo window. WinForms is used only for `NotifyIcon` and monitor enumeration. This is the stack CLAUDE.md prefers, so no deviation needs justifying.

## Projects

| Project | Target | Contents |
| --- | --- | --- |
| `src/ScreenQuill.Core` | net8.0 (no UI) | Annotation model, geometry, tool state, palette, hotkey parsing and validation, settings and persistence, screenshot naming. All unit-testable. |
| `src/ScreenQuill` | net8.0-windows | WPF app: overlays, input, renderer, toolbar, flyouts, settings, services |
| `tests/ScreenQuill.Tests` | net8.0 | xUnit tests for Core (61 tests) |

## Coordinate model

- Annotations are stored in **global physical screen pixels**, so ink lines up across monitors with different scale factors and negative origins.
- Each monitor's `InkSurface` converts the model to WPF DIPs with one transform: `scale = 1/dpiScale`, `offset = −origin/dpiScale`.
- The app is Per-Monitor-V2 DPI aware (`app.manifest`). Overlays are positioned with `SetWindowPos` in physical pixels.

## Main components

- **AppController** (`AppController.cs`) is the composition root. It:
  - owns the document, tool state, settings, overlays, toolbar, hotkeys, halo and tray icon;
  - implements `IInkHost`;
  - routes every command (select tool, board, fade, colour, screenshot and so on) and pushes state out through `ApplyState`;
  - saves settings on a 600 ms debounce.
- **OverlayWindow:** one borderless, transparent, topmost, non-activating tool window per monitor.
  - *Drawing mode:* the background is alpha 1/255, so the window receives hit-tests.
  - *Cursor mode:* the background is transparent and `WS_EX_TRANSPARENT` is set, so every click passes through to the app underneath.
  - `WM_MOUSEACTIVATE → MA_NOACTIVATE` stops focus being stolen. The overlay may take focus only while a text box is being edited.
  - Boards are drawn as the window background, behind the ink.
- **InkInteraction:** state machine for each pointer contact: pen, highlighter, shapes, eraser and text.
  - Stylus and touch use WPF Stylus events with pointer support turned on, one session per contact ID; mouse events promoted from them are ignored.
  - Live previews are coalesced to one redraw per frame.
- **AnnotationRenderer / InkSurface:** retained-mode rendering with one `DrawingVisual` per annotation, so a change re-renders only that element. Highlighter, colour alpha and fade are applied as visual opacity.
- **AnnotationDocument (Core):** ordered elements plus an undo/redo command stack.
  - `RemoveExpired` deletes faded elements and prunes them from history, so undo can't bring back expired ink.
  - The clock can be injected for tests.
- **HotkeyManager:** `RegisterHotKey` on a message-only window.
  - Suspended while a text box is open, while a hotkey is being recorded and during screenshot selection.
  - Reports chords that are already taken by other apps.
- **CursorHalo:** a dedicated STA thread with its own message loop that owns:
  - the `WH_MOUSE_LL` hook, installed only while the halo is on or a flyout is open;
  - a raw layered window drawn with `UpdateLayeredWindow` and re-rendered when the DPI changes;
  - a 15 ms cursor-poll fallback.

  Keeping the hook off the UI thread means UI work can never lag the system cursor.
- **ScreenCapture:**
  1. Hide the toolbar and halo.
  2. Capture the whole virtual screen with GDI `BitBlt(SRCCOPY|CAPTUREBLT)`. The ink and board are on screen, so they are included.
  3. Show a frozen, dimmed copy in one selector window per monitor.
  4. Track the selection with physical `GetCursorPos` coordinates.
  5. Crop, then save as PNG and/or copy to the clipboard.
- **Settings:** JSON in `%APPDATA%\ScreenQuill\settings.json`.
  - Writes are atomic: a temp file, then `File.Replace`.
  - A corrupt file is moved to `settings.json.bad` and defaults are used.
- **Lifecycle:**
  - A named mutex keeps one instance running; a second launch signals the first to leave Ghost Mode.
  - Unhandled UI exceptions go to the local `error.log` and the app keeps running.
  - Overlays are rebuilt when the display configuration changes.
  - Topmost order is reasserted every 1.5 s.

## Privacy

Nothing is sent over the network. Screenshots go only to the chosen folder and/or the clipboard. Errors are written only to the local `error.log`.
