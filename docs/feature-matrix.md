# Feature matrix

Sources: [features](https://epicpen.com/features), [pricing](https://epicpen.com/pricing), [user guide](https://epicpen.com/userguide), [FAQ](https://epicpen.com/faq).

What the **Tested** values mean:
- **Desktop** — exercised by hand in the running app on Windows 11 (one 1920×1080 monitor at 100%).
- **Unit** — covered by an automated test in `tests/ScreenQuill.Tests`.
- **No** — not verified yet.
- **Not hardware-tested** — the hardware wasn't available.

| Feature | Official source | Reference image(s) | Implemented | Tested | Notes |
| --- | --- | --- | --- | --- | --- |
| Draw over any application (pen) | features, userguide | maxresdefault.jpg, images (1).jfif | Yes | Desktop | Smoothed with quadratic curves; round caps and joins |
| Pen width independent of other tools | userguide | images (1).jfif (size dot) | Yes | Unit (ToolState), Desktop (highlighter vs pen) | Size flyout offers presets and a slider; not exercised by hand |
| Highlighter (translucent) | features, userguide | images (1).jfif (pen flyout) | Yes | Desktop | Whole-stroke opacity, so a stroke never darkens where it overlaps itself; opacity is configurable |
| Eraser | features, userguide | images (1).jfif | Yes | Desktop (whole shape erased) | Erases whole objects: anything the eraser circle touches is removed, in one undo step per drag. Inverted stylus or barrel button also erases. |
| Clear all | features, userguide | images (1).jfif (trash) | Yes | Unit (document Clear/undo) | Trash button plus Ctrl+Alt+D. Toolbar button not exercised by hand. |
| Cursor / pass-through mode | userguide | images (1).jfif (cursor selected) | Yes | Desktop: clicked and typed in Notepad under visible ink | WS_EX_TRANSPARENT overlay; ink stays visible |
| Hide/show ink (eye) | userguide | images (1).jfif | Yes | Desktop | Ctrl+Alt+V |
| Region screenshot to file | features, userguide | images (1).jfif (camera) | Yes | Desktop: PNG saved with ink | Folder is configurable; files are named `ScreenQuill yyyy-MM-dd HH-mm-ss.png` |
| Screenshot to clipboard | userguide | — | Yes | Desktop: image found on the clipboard | Ctrl+Alt+X; also optional after a save |
| Screenshot cancel (Esc) | — | — | Yes | Desktop | Right-click also cancels |
| 16 basic / 24 Pro preset colours | pricing, features | images.jfif | Yes, 24 | Unit (palette), Desktop (renders) | Colours sampled from the reference; one chip estimated |
| Custom colour picker | pricing, features | images.jfif | Yes | No | HSV square, hue slider, hex and RGB fields; custom colours persisted |
| Selected-colour indication and persistence | — | images (1).jfif | Yes | Desktop: colour kept after restart | |
| Global hotkeys for tools and actions | userguide | — | Yes, 24 actions | Unit, Desktop | Ctrl+Alt+letter **project defaults**, because the official defaults weren't verified (see README) |
| Editable hotkeys, persisted | userguide | — | Yes | Desktop: rebinding Pen to Ctrl+Alt+J worked after restart | |
| Hotkey conflict detection | — | — | Yes | Unit, Desktop (duplicate flagged, OK disabled) | Also flags OS-reserved chords and chords already registered by other apps |
| Hotkeys suspended during text entry and recording | — | — | Yes | Desktop (recording) | |
| Ghost Mode | features, userguide | — | Yes | Desktop (Ctrl+Alt+G hides and restores) | Tray click and a second launch also restore the toolbar |
| Pen pressure | features, userguide | — | Yes | Not hardware-tested | WPF Stylus with pointer support; width scales 0.25×–1.5× |
| Multi-touch drawing | features, userguide | — | Yes | Not hardware-tested | One session per touch contact; mouse events promoted from touch are ignored |
| Text tool (Pro) | features, pricing | images (1).jfif (pen flyout) | Yes | Desktop (place and commit) | Enter commits, Shift+Enter adds a line, Esc cancels, drag moves, click edits, emptying deletes. Uses the current colour and text size. |
| Line (Pro) | features | images (2).jfif | Yes | Desktop, all drag directions | Shift snaps to 45° |
| Arrow (Pro) | features | images (2).jfif | Yes | Desktop | Filled head that scales with width |
| Rectangle (Pro) | features | — | Yes | Desktop | Shift makes a square |
| Ellipse (Pro) | features | — | Yes | Desktop | Shift makes a circle |
| Whiteboard (Pro) | features, pricing | images (1).jfif (board) | Yes | Desktop (sits behind ink) | Ctrl+Alt+W |
| Blackboard (Pro) | features, pricing | images (1).jfif | Yes | Desktop | Ctrl+Alt+B; board is #141414 |
| Fading ink: short/medium/long (Pro) | userguide, features | images (1).jfif (fade) | Yes | Desktop (Short, about 3 s); Unit (independent lifetimes, expiry, undo pruning) | Durations 2 / 5 / 10 s plus a 1 s fade-out are **project defaults** (the official ones aren't published); configurable in Settings. Medium and Long not checked by hand. |
| Highlighted cursor (Pro) | features, pricing | — | Yes | Desktop (stays centred, click-through) | Colour, radius, opacity and ring configurable; Ctrl+Alt+M |
| Ad-free, no feature locks | pricing | — | Yes | — | No accounts, no network access |
| Draggable toolbar with remembered position | — | images (3).jfif | Yes | No | Drag the logo; click it to collapse; position clamped when monitors change |
| Settings window | userguide | — | Yes | Desktop (hotkeys tab) | Tabs: General, Tools, Fading & Cursor, Screenshots, Hotkeys, About |
| Tray icon | — | — | Yes | No | Menu: show toolbar, tools, clear, halo, screenshot, settings, exit |
| Launch at startup | — | — | Yes | No | HKCU Run key; written only when the setting is toggled |
| Multi-monitor and mixed DPI | faq | — | Yes | Not hardware-tested | One overlay per monitor; global physical-pixel model; rebuilt when displays change |
| Undo / redo (supplemental, not an Epic Pen feature) | — | — | Yes | Unit | Ctrl+Alt+Z / Ctrl+Alt+Y |
| Exit cleanup | — | — | Yes | No | Unregisters hotkeys, disposes the tray icon, removes the hook, closes overlays |
