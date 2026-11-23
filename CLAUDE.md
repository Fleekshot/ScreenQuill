# CLAUDE.md — Epic Pen Pro–style Windows Screen Annotation Application

## Mission
Build a **fully functional, production-quality Windows 10/11 desktop screen-annotation application** with extremely high **visual and behavioral fidelity to the Epic Pen Pro reference**, including every publicly documented Basic and Pro annotation feature. The finished product must behave like a real always-on-top annotation utility—not a webpage, Photoshop-style canvas, static mockup, or merely attractive toolbar.

**First and mandatory: inspect every reference image in the project folder** before changing code. Treat those images as the primary source of truth for visual layout, tool ordering, icon silhouettes, toolbar geometry, spacing, state indicators, colors, open menus, settings, and hover/selected states. Use the official sources below for functionality. If a screenshot and written description disagree on appearance, the screenshot wins; for behavior, use official documentation and actual observed behavior when available. Do not claim pixel-perfect fidelity until measured and verified.

This is an **independently implemented, Epic Pen–inspired application**, not a modification or redistribution of Epic Pen. Do not use proprietary source code, decompile Epic Pen, bypass its licensing, or bundle extracted Epic Pen assets. Recreate UI elements from scratch. Use the project's own name/branding for distribution unless the owner has permission to use Epic Pen trademarks. The goal is complete feature parity and maximum fidelity, not copying protected assets.

## Ground-truth references (reviewed November 22, 2026)

- Official feature list: https://epicpen.com/features
- Official Basic-versus-Pro comparison: https://epicpen.com/pricing
- Official user guide, tool behavior, screenshots, hotkeys, peripherals and settings: https://epicpen.com/userguide
- Official FAQ (multi-monitors, fullscreen caveats, sharing and deployment): https://epicpen.com/faq
- Official product home: https://epicpen.com/
- Product screenshots in the **local project folder**: primary visual reference.

**Important source nuance:** Some promotional pricing-page bullet lists mention Pro tools under Basic, but the explicit comparison tables, feature page and user guide designate text, shapes/arrows, boards, fading ink, cursor highlight and expanded/custom colors as Pro features. Implement **all** of them as available features in this project; no paywall is required. The original product's subscription, licensing transfer, account infrastructure, commercial billing, ad promotions and managed deployment are service/business features, not necessary to deliver an independent fully featured annotation tool. Do not pretend to integrate Epic Pen accounts or activation codes.

## Mandatory execution workflow

1. **Inventory reference materials:** recursively locate images (PNG, JPG, JPEG, WEBP, GIF, BMP, SVG) in the repository root and folders such as `references/`, `reference/`, `screenshots/`, `assets/`, `design/`, `images/`. Open and inspect **every** reference image, not just filenames. If a GIF/animation exists, examine representative frames. Record image name, dimensions, screen/toolbar state, visual details and any unresolved ambiguity in `docs/reference-audit.md`. Do not invent details not visible in images. If the images are absent, record that fact and continue using official references; make comparison to local images a required follow-up when supplied.
2. Inspect the existing repository, build system, installed dependencies, platform assumptions and project instructions. **Extend existing code** when possible; do not discard a working project without reason. Maintain a living checklist in `docs/feature-matrix.md`.
3. Build a **native Windows application** capable of desktop-wide transparent overlay rendering, system-wide pointer handling, per-monitor DPI awareness and global hotkeys. If the project has no framework, prefer **C# / .NET 8+ with WPF** for UI with Win32 interop for layered overlay windows, pointer input and hotkeys. A proven alternative native framework is acceptable if it supports the same real OS-level features; justify any change in `docs/architecture.md`. A browser-only application is **not acceptable**.
4. Implement working core overlay/input architecture first; then tool behavior; then screenshots/settings/hotkeys; then match the reference UI visually; then run automated and manual tests.
5. Keep a working build at all times. Rebuild and run tests after meaningful changes. Fix actual runtime/interaction bugs before claiming completion. Do not stop at pseudocode, TODOs, placeholder panels or cosmetic demonstrations.
6. Deliver complete sources, build/run instructions, release packaging if feasible, a feature matrix with verified results and a list of platform limitations (if any).

## Non-negotiable desktop behavior

- Launch as a lightweight, **floating toolbar over any Windows application**. It must remain above normal windows but not steal focus unnecessarily.
- Render annotations in one or more **transparent, always-on-top overlay windows** that span the appropriate monitor bounds (including negative display coordinates), preserving the desktop and underlying applications.
- Distinguish **drawing mode** from **cursor/pass-through mode**. In drawing mode the overlay receives drawing input; in cursor mode mouse clicks, scrolling and typing reach underlying apps normally while existing ink remains visible. Switching modes must not discard marks.
- The toolbar remains interactive regardless of whether the ink overlay is click-through. Do not make the toolbar itself unclickable when toggling overlay transparency.
- Ensure correct pointer coordinates across monitor boundaries, display scaling, resolutions and mixed-DPI monitors. Handle screen reconfiguration without crashing or offsetting ink.
- No permanent opaque full-screen window, unwanted taskbar entry for each overlay, broken focus capture or system-wide mouse lock. Avoid disruptive interference with secure desktop/UAC and other apps.
- Maintain responsive stroke drawing with no noticeable lag or flicker. Avoid excessive CPU use when idle and unnecessary rerenders. Smooth variable-pressure input when available.
- Annotation is **local**. Do not upload screenshots, underlying desktop imagery or user inputs. Any diagnostics must be opt-in.

## Full feature inventory — implement all

### A. Free/core tools, included in the complete clone

**1. Pen**
- Freehand drawing over any visible application with smooth continuous strokes.
- Configurable stroke width **independent of other tool widths**.
- Respect active color and alpha; round joins/caps; visually clean antialiasing.
- Support mouse, stylus/pen-tablet and touch input; respond to pen pressure when hardware exposes it.

**2. Highlighter**
- Translucent marker strokes that keep the underlying screen legible.
- Independent width setting and selectable colors; appropriate alpha blending.
- Preserve quality at different scales and while capturing a screenshot.

**3. Eraser and clear**
- Interactive eraser that can remove annotations or intersected stroke portions reliably (choose a clear strategy and document it).
- Instant clear-all action (toolbar button plus hotkey), including shapes/text where appropriate.
- Do not clear underlying application content.
- Consider undo/redo as quality-of-life enhancements **only after** documented behaviors work; label as supplemental, not a documented Epic Pen Pro feature.

**4. Screenshot capture**
- User can capture a chosen screen region, including composited overlay ink; support save to user-selectable directory and copy to clipboard.
- Rectangular region selection has clear visual bounds and cancel behavior (Escape).
- Provide destination folder setting, sensible naming and no unexpected inclusion of capture-selection UI.
- Support multi-monitor screenshots, negative coordinate origins and DPI correctness.

**5. Color palette**
- Implement all **24 quick-pick colors** in full-featured mode, plus unrestricted custom color via color picker (RGB/hex and, optionally, alpha where useful).
- Use screenshot references for actual chip arrangement/shades; avoid inventing exact RGB values when not shown.
- Show current color clearly, support changing colors while switching tools, and persist custom choice.
- Note source distinction: the original Basic plan lists 16 preset colors, while Pro lists 24 + arbitrary custom colors. Our finished app exposes the full Pro set.

**6. Global hotkeys**
- Hotkey to select every major tool and invoke important actions: pen, highlighter, eraser, cursor/pass-through, shapes, text, screenshot, clear, color/size operations where appropriate, board state, fading ink, cursor highlight, toolbar visibility and Ghost Mode.
- Editable hotkey assignments in Settings, persisted across restarts.
- Detect conflicts (with one another and OS-reserved combinations where feasible), support reset to defaults and avoid hotkeys firing during text entry or hotkey recording.
- The actual default bindings should match local references or independently verified Epic Pen defaults; **do not fabricate them**. If unknown, choose sensible project defaults and label them as such.

**7. Ghost Mode**
- Hide the entire toolbar while leaving annotations visible and hotkeys functional.
- Provide a reliable global shortcut to restore the toolbar without quitting the app.
- Preserve existing tool, ink and settings through mode transitions.

**8. Pen pressure and multi-touch**
- Support Windows Pointer / Ink APIs as appropriate for stylus pressure and touch contact IDs.
- Allow multiple simultaneous touch contacts to produce independent strokes on compatible hardware; avoid joining two fingers into one line.
- Gracefully fall back to mouse-equivalent drawing on devices without pressure or touch.
- Test cancellation, contact release, pen barrel events and touch versus mouse synthetic event duplication.

### B. Every official Pro annotation tool

**9. Text tool (Pro)**
- Click anywhere on the overlay to place text; enter and edit text through keyboard input.
- User can set text color and size. Provide practical editing interactions such as commit, cancel, reposition and delete; align them with references when visible.
- Render crisp text on top of the desktop; screenshot capture includes it; scaling and monitor moves are handled correctly.
- Text editing must temporarily preserve keyboard focus, while returning to pass-through behavior afterward.

**10. Shape/line/arrow tools (Pro)**
- Draw straight lines, directional arrows, rectangles and ellipses with mouse/stylus drag.
- Live preview while dragging; commit on release; correct handling when dragging in any direction.
- Match source image iconography and options (outline, stroke widths, colors, arrowhead shape). Apply antialiasing and crisp endpoints.
- Shapes obey eraser, clear, screenshot and visibility behaviors.

**11. Whiteboard and blackboard (Pro)**
- Single action to cover desktop behind annotations with solid **white** or **black** board, and return to transparent desktop view.
- Boards must be behind ink and should not permanently alter screenshots or desktop windows. Mark visibility remains predictable on board transitions.
- Capturing a board screenshot produces a clean solid background behind notes.
- Ensure settings/toolbar remain legible on both board colors; work on selected display(s) consistently.

**12. Fading ink (Pro)**
- Enable/disable automatic disappearance of newly drawn annotations; expose **short, medium and long** fade durations as documented in the official guide.
- Time each annotation independently from the time it is created; implement alpha fading smoothly and fully remove expired elements.
- Ensure transitions are correct when clearing, erasing, changing tools, toggling fading or switching boards.
- Do not invent official numeric durations without a source. Choose clearly labeled project defaults if needed, keep them configurable, and document the difference.

**13. Highlighted cursor (Pro)**
- Display a prominent, colored halo centered on the actual cursor position for presentations/recordings.
- Toggle via toolbar and hotkey; offer sensible color/radius/opacity controls if source references show them.
- Never block clicks or interfere with normal pointer movement; the halo works in cursor/pass-through mode.
- Minimize latency and keep the halo aligned at different DPI scales and across monitors.

**14. Complete Pro visual palette**
- 24 quick colors, custom color picker and correct feedback for selected color. Ensure all tools support the applicable current color and width.

**15. Ad-free, uninterrupted utility experience**
- No advertisements, upsells, pop-ups or paid feature locks. All annotation tools function immediately and offline.

### C. Product-level fidelity and usability

**16. Toolbar / menus / Settings**
- Match local reference screenshots **as closely as possible**: vertical/horizontal orientation, toolbar width/height, corner radii, padding, visual hierarchy, icon sizing, dividers, menus, flyouts, palette layout, dark/light surfaces, shadows, selected states and hover/tooltip behavior.
- Draggable compact floating toolbar; persist last location and clamp to screen when monitors change. Avoid accidental pen drawing when dragging the toolbar.
- Tool switching immediately updates selected state, active cursor/interaction mode and per-tool width control.
- Settings must have actual working controls for hotkeys, screenshot directory and meaningful UI/device options. Persist preferences.
- Provide a tray icon and clean exit/relaunch behavior; menu items should work and have no dead buttons.

**17. Multi-display, touch, screen sharing and performance**
- Annotate on multiple connected monitors, including different scaling factors, with correct local/global coordinate translations.
- Capture desktop-screen ink in whole-display screen sharing/recording (e.g., typical Zoom/Teams/OBS display capture), recognizing that individual-window capture generally excludes separate overlay windows.
- Note an OS limitation: some exclusive-fullscreen games/apps may draw above overlays; borderless/windowed mode is a reasonable documented workaround. Do not falsely promise universal capture or overlay above secure/fullscreen surfaces.
- Test on Windows 10/11 where feasible and document environments not physically available.

## Visual-reference fidelity protocol

Create `docs/reference-audit.md` with an entry **for each local reference image**, including:

| File | Image size | What is pictured | Layout/color measurements | States/features seen | Implemented match | Notes |
| --- | --- | --- | --- | --- | --- | --- |

For every reference, identify exact toolbar structure, button order, icons, colors, borders, shadows, typography, menus, flyouts, palette, settings elements and toolbar placement. Map each visible control to its implemented action. Generate fresh screenshots of the built app in matching states and compare **side-by-side**; improve until differences in geometry/states are minimal. Where appropriate, use objective geometry/pixel-difference checks, but visual inspection is still required. Avoid using copyrighted screenshots as distributable UI sprites. If reference images are not present, do not imply they were inspected.

## Suggested architecture and implementation guardrails

- **Native UI shell:** lightweight toolbar, settings dialog, context menus, tray icon, help/about.
- **Overlay manager:** transparent, topmost, non-activating per-monitor window(s), DPI awareness, pointer hit testing/click-through toggles, z-order handling, monitor-change handling.
- **Input controller:** mouse/stylus/touch contact sessions, normalized screen-to-overlay coordinates, pressure samples, mouse/pen deduplication, gesture cancellation.
- **Annotation model:** strokes, shape objects, text items, creation timestamps, colors, widths, alpha, z-order and deletion. Render model rather than burning marks irreversibly into an uneditable bitmap.
- **Renderer:** performant retained ink rendering, antialiasing, independent board background, cursor halo rendered without intercepting pointer events.
- **Tools:** explicit state machine (cursor, pen, highlighter, eraser, text, line, arrow, rectangle, ellipse, board, screenshot selection); no simultaneous accidental interactions.
- **Capture service:** screen capture with final annotations composited, safe copy-to-clipboard and save-to-file operations.
- **Hotkey and preferences service:** global registration, conflict validation, reliable persistence and defaults.
- **Lifecycle:** startup, app tray, window ownership, display changes, proper disposal and graceful exceptions.

Do not simulate overlays using a giant interactive transparent rectangle that prevents normal desktop use in cursor mode. Use documented Windows APIs and test click-through with a real browser/file explorer underneath. Do not request administrative privileges merely to annotate; clearly document genuine Windows privilege limitations where applicable.

## Acceptance tests — do not mark complete without verifying

1. Open browser/notepad, draw freehand on top, switch to cursor, then **click, scroll and type in the underlying app** while ink remains visible.
2. Pen and highlighter have visibly different blending and separate persistent stroke widths; active color updates correctly.
3. Erase one or more marks without wiping the desktop; clear action removes all annotations reliably.
4. Draw clean arrows, straight lines, rectangles and ellipses with preview in all drag directions.
5. Place, type, color, resize/edit and remove text; typing does not trigger global tool shortcuts.
6. Enter whiteboard, enter blackboard, return to transparent desktop; ink and screenshots behave correctly.
7. Activate fading ink; verify independent mark lifetimes and all three duration options.
8. Activate cursor halo and move across monitors; halo remains centered and does not intercept clicks.
9. Verify 24 quick colors, custom picker, selected indication and persistence.
10. Hide toolbar in Ghost Mode, draw/clear/switch using keyboard, restore toolbar with the designated shortcut.
11. Change a shortcut, restart, verify it works; check duplicate/conflicting shortcut handling.
12. Select a screenshot region with annotations, save to chosen directory, copy to clipboard, verify output contents and cancellation behavior.
13. If available, test pen pressure, stylus, simultaneous fingers and mixed DPI monitors; explicitly mark **not hardware-tested** when unavailable.
14. Compare screenshots of final toolbar, menus, settings and each expanded tool against every local design reference.
15. Restart app: settings persist; quitting cleans up overlays, hotkeys and tray icon.
16. Run automated unit tests (tool state, geometry, model, fade timing, persistence and hotkeys) plus a smoke test on Windows.

Use a feature matrix with columns `Feature | Official source | Reference image(s) | Implemented | Tested | Notes`; each documented Basic and Pro item must have a row. Do not mark a feature tested merely because a build succeeded.

## Definition of done

The project is complete only when it is a launchable real Windows desktop application with **all documented Basic and Pro tools implemented and operational**, the reference screenshots carefully audited and matched where available, click-through and overlay functionality validated, settings persisted, and tests/build instructions supplied. Do not accept a partial prototype as the finished clone. If the execution environment cannot run Windows apps, still implement the real native solution and tests, state the validation gap accurately, and provide straightforward steps for the user to test on Windows.

Final deliverables: application source, runnable build/installer or precise build instructions, `docs/reference-audit.md`, `docs/feature-matrix.md`, `docs/architecture.md`, `README.md`, test results, and known limitations. Always prioritize correctness of Windows-native behavior over superficial visual similarity, then close the remaining visual gaps by checking the screenshots.
