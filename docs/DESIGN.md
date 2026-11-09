# Widget design system

Lateraroot is a Windows desktop-customization utility. Its primary objects are interactive file widgets. A category tile and a file tile share a square module; their dimensions must never stretch to fill a monitor edge.

## References and decisions

- [Fences 6](https://store.steampowered.com/app/3165690/Fences_6/) informs grouped desktop access and customization; [Rainmeter](https://www.rainmeter.net/) informs resident widgets and deliberate desktop placement. This application is not a skin host or a clone of either product.
- [Nagoya Biyo](https://nagoyabiyo.ac.jp/) was inspected through live DOM/CSS/GSAP state. Its useful principles are spare editorial hierarchy, crisp geometric crops, generous unpainted space, thin directional strokes and staggered arrivals. Observed heading transitions use 660ms and 25ms character staggering; image movement uses a 1.25s ease-out. Desktop interaction uses a shorter, interruptible interpretation of those motions.
- [UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill), installed in this task's working folder, supplied verified Minimalism/Swiss, Productivity Tool, reduced-motion and WPF virtualization guidance. Its generated web hero/CTA pattern and suggested web fonts do not apply here. The user's Zen Kaku Gothic New Medium and category colors take precedence.

## Tokens

| Token | Value |
| --- | --- |
| Square module | 88 DIP, configurable 72–112 in 8 DIP steps |
| Spacing | 8 / 12 / 24 / 32 DIP |
| Main font | Bundled Zen Kaku Gothic New Medium |
| Widget label / filename | 12 DIP; full name in accessible name and tooltip |
| Metadata | 10 DIP, short counts only |
| Inspector heading / body | 24 / 13 DIP |
| Default palette | #FFFFFF / #000000; arbitrary user tint, existing settings retained |
| Ink / canvas | Automatic black/white ink on glass; white editor content; #F5F5F7 settings shell; transparent unused widget area |
| Material | Translucent gradient tint, specular rim, inner bevel; 16 DIP radius inside a square module |
| Controls | Consistent 1.2 DIP vector strokes, native labeled buttons |
| Focus | Inset 2 DIP high-contrast ring; geometry stays fixed |

## Behavior and motion

- Tiles use one square geometry service across four edges, monitor scales, multiple bands and saved positions. Widget size can shrink on a constrained screen to retain complete squares.
- Desktop widgets belong above the shell and below normal applications. Compact access is a separate presentation of the same library and inspector.
- Enter: 260ms opacity plus 320ms ease-out translation, maximum 12 DIP. Visible file cells arrive from the docked edge at 24ms intervals, capped at seven delays.
- Hover: 180ms subtle ink state; exit: 140ms. No width/height tween, perpetual animation or ambient movement.
- Re-entry cancels a pending close. A foreground change, hide or settings change cancels stale completion callbacks.
- Motion is enabled by default, including when Windows client-area animation is off. The explicit reduced-motion option always wins; following Windows is a separate opt-in preference. The application never edits OS preferences. Verification exercises the normal policy without forcing animations on.
- Stagger delays are keyframe holds from opacity zero, established synchronously. Newly realized cells may join the current opening sequence for 550ms; later scrolling does not replay it. Minimize/hide completion is ticketed so rapid restore cannot trigger a stale hide; disabling motion completes an active exit immediately.
- Native recycling rows realize visible files only. Icons are loaded by realized bindings and cached with a cap. Scrolling does not replay entry choreography.
- Mouse hover and click both open a widget. Keyboard focus, Ctrl+F, Escape, Page Up/Down and numeric edge/position settings provide alternatives to gestures.

## Review gates

Actual tile bounds must remain square, pointer and keyboard targets must agree, sparse grid areas must stay transparent, file name contrast must remain readable, interrupted animation must leave no invisible active surface, and large lists must retain bounded realized containers. Rendering snapshots verify final and intermediate states; they are app-rendered previews, not captures of a user's desktop.

The compact header is one 66-DIP glass drag control with an integrated text mark and Flaticon grip. It has no opaque system Thumb background. Locked handles show a lock and normal pointer. Compact genre names use balanced two-line labels; category cells fit between the header and three 30-DIP action targets, with a work-area-bounded rail length. These actions share the rail surface, without individual raised glass capsules; hover and keyboard focus reveal their hit areas. UI icons use 21 compiled **Uicons by Flaticon Thin Rounded** vectors; attribution and separate licensing are in Settings and THIRD_PARTY_NOTICES. The minimize outline receives a 0.75-DIP optical expansion because its tiny horizontal contour otherwise loses coverage during rasterization. Its rendered visibility is checked at 100%, 150% and 200% DPI. The original icon font is not bundled and runtime never fetches icons.

## Glass material and genre editing (v0.5)

The shared WPF material uses tinted alpha blending, static vector rims and finite hover/entry motion. Background capture, GPU blur, refraction and the dedicated glass preview have been removed. There is no capture-exclusion setting or background-rendering worker.

One `GlassSurface` draws categories, file cells, compact framing, controls and the editor preview. Its frozen brush/pen cache is capped at 64 materials. Tints near the contrast threshold are adjusted conservatively; black/white ink is chosen automatically. Rendered text areas are checked against both black and white backdrops at three vertical positions for ten representative colors (4.5:1 minimum). Empty corners remain alpha-zero. Reduced transparency removes reflections and uses opaque fills. Windows high contrast forces an opaque surface while retaining the chosen tint and automatic contrasting ink; changes are handled by one application-level preference listener.

The default palette alternates white and black. A user's existing colors are preserved. Genre editing uses a list, named color swatches, a standard Windows color dialog, a validated HEX field and a live preview. Selection is marked with a check as well as color. The fallback genre remains available but can be renamed and recolored. Priority has explicit up/down controls. Edits use a cloned draft; only Save applies them. Resetting colors preserves names, rules and IDs. Size/layout/behavior live in a separate tab, leaving the default tab focused on genres and colors.

The v0.6 file-acquisition/presentation split, shared organizer actions, change-driven refresh and placement lock are documented in [ARCHITECTURE.md](ARCHITECTURE.md). They reuse this material and geometry system.

## Genre icons and sorting

The development build can replace the category name, ordinal and count with a centered 28-DIP compact / 34-DIP desktop icon. Full names stay in tooltips and accessibility labels. One global switch controls the display and each genre can select an icon or infer it from its classification. Both presentations use the same label builder. The compact rail grows to fit eight or more genres, bounded by the screen's working area; it scrolls when necessary.

File sorting exposes name/date/type/size and ascending/descending through shared right-click actions, settings and the inspector. Genre priority/order remains the existing explicit up/down editor. Game classification is shortcut-metadata based and reuses the same library and rule precedence. It reuses the same file catalog without a separate rendering pipeline.
