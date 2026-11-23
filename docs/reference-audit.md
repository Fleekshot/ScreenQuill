# Reference audit

All five images in `Reference images/` were opened and inspected before UI work began. They are third-party product screenshots used only as a visual guide. No pixels from them are shipped: every icon in ScreenQuill is vector geometry written from scratch in `src/ScreenQuill/Toolbar/Icons.cs`.

| File | Image size | What is pictured | Layout/color measurements | States/features seen | Implemented match | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `images.jfif` | 494×404 | Vertical toolbar with the colour-palette flyout open to its left | Palette is 3 columns × 8 rows of square chips with thin white gutters, in a white panel with a light grey border and a notch pointing at the current-colour swatch. Chip colours were sampled (see `Palette.cs`). | Palette flyout open; current-colour swatch at the bottom of the toolbar | 3×8 palette flyout in a white panel with a #C5C5C5 border and a notch; bottom-aligned with the swatch | The last chip in row 6 is partly hidden, so its value (FF8A00) is an estimate. The custom-colour row and "+" are a project addition for the custom picker. |
| `images (1).jfif` | 335×597 | Full vertical toolbar | About 42 px wide; button pitch about 36 px; round black logo about 40 px at the top; selected-button fill about #2FC5EA. Order: logo, eye, cursor, fade, pen, shapes, eraser, size dot, undo, trash, board, camera, clipboard, 2×2 CMYK swatches, large current colour. | Eye and cursor selected (cyan fill); small triangles at the bottom-right of buttons that have flyouts; yellow current colour | Same order, cell size, selected fill and flyout triangles (`ToolbarWindow.cs`, `ToolButton.cs`) | Small pink dots on some buttons look like "Pro" badges. They were left out on purpose because this app has no paid tier. |
| `images (2).jfif` | 494×404 | Same toolbar | Same geometry as above | Shape group showing the line icon, selected; black current colour | The shape button shows the last-used shape and is highlighted when active | Confirms that a group button takes the icon of its last-used tool. |
| `images (3).jfif` | 678×452 | Promotional image: a monitor with the toolbar docked near the right edge and ink on screen | Toolbar near the top-right of the screen | Typical placement and annotation in use | Default toolbar position is the top-right of the primary work area | Too small to take measurements from. |
| `maxresdefault.jpg` | 1280×720 | Desktop with the toolbar at the right edge and red freehand ink | Toolbar about 42 px wide at this scale | Pen selected; red current colour; ink over apps | Pen is the default drawing tool; default colour is red (FE0202) | Used to confirm the ink style (round caps, solid colour). |

## Unresolved ambiguities

- **Clipboard-shaped icon (last button):** the images don't show what it does. It opens the app menu: screenshot to clipboard, open folder, redo, Ghost Mode, settings, help, exit.
- **2×2 CMYK swatches:** in the images they look fixed. Here they are quick-pick slots, and right-clicking a slot stores the current colour in it.
- **Hover states and tooltips** are not shown in any image. Hover uses a light cyan (#E3F5FC), and tooltips are standard WPF tooltips.
- **Settings window:** no reference image exists, so its layout is a project design.

## Side-by-side comparison status

Fresh toolbar screenshots were taken on Windows 11 at 100% scaling while testing. By eye, they match `images (1).jfif` and `images.jfif` in button order, cell pitch, selected colour and palette layout. No pixel-difference measurement was done, so pixel-perfect fidelity is **not** claimed.
