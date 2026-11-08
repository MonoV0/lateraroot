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
| Ink / canvas | #171717 / opaque white on inspectors; transparent unused widget area |
| Controls | Consistent 1.2 DIP vector strokes, native labeled buttons |
| Focus | Inset 2 DIP high-contrast ring; geometry stays fixed |

## Behavior and motion

- Tiles use one square geometry service across four edges, monitor scales, multiple bands and saved positions. Widget size can shrink on a constrained screen to retain complete squares.
- Desktop widgets belong above the shell and below normal applications. Compact access is a separate presentation of the same library and inspector.
- Enter: 260ms opacity plus 320ms ease-out translation, maximum 12 DIP. Visible file cells arrive from the docked edge at 24ms intervals, capped at seven delays.
- Hover: 180ms subtle ink state; exit: 140ms. No width/height tween, perpetual animation or ambient movement.
- Re-entry cancels a pending close. A foreground change, hide or settings change cancels stale completion callbacks.
- Windows client-area-animation preference and an explicit reduced-motion setting produce the same final state immediately.
- Native recycling rows realize visible files only. Icons are loaded by realized bindings and cached with a cap. Scrolling does not replay entry choreography.
- Mouse hover and click both open a widget. Keyboard focus, Ctrl+F, Escape, Page Up/Down and numeric edge/position settings provide alternatives to gestures.

## Review gates

Actual tile bounds must remain square, pointer and keyboard targets must agree, sparse grid areas must stay transparent, file name contrast must remain readable, interrupted animation must leave no invisible active surface, and large lists must retain bounded realized containers. Rendering snapshots verify final and intermediate states; they are app-rendered previews, not captures of a user's desktop.
