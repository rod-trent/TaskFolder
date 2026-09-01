---
name: TaskFolder
description: A crisp, DPI-aware Windows 11 tray launcher built from restrained Fluent utility surfaces.
colors:
  focus-blue: "#0067C0"
  menu-surface: "#F9F9F9"
  input-surface: "#F3F3F3"
  border-subtle: "#D6D6D6"
  border-hover: "#ACACAC"
  row-hover: "#E8E8E8"
  row-pressed: "#E0E0E0"
  text-primary: "#1F1F1F"
  text-muted: "#616161"
  text-disabled: "#969696"
typography:
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, sans-serif"
    fontSize: "9pt-18pt"
    fontWeight: 400
  settings:
    fontFamily: "Segoe UI Variable Text, Segoe UI, sans-serif"
    fontSize: "9.5pt"
    fontWeight: 400
rounded:
  item: "4px"
  menu: "8px"
  field: "8px"
spacing:
  xs: "4px"
  sm: "6px"
  md: "8px"
  lg: "12px"
components:
  tray-menu:
    backgroundColor: "{colors.menu-surface}"
    textColor: "{colors.text-primary}"
    typography: "{typography.body}"
    rounded: "{rounded.menu}"
    padding: "6px 4px"
  tray-menu-row:
    backgroundColor: "{colors.menu-surface}"
    textColor: "{colors.text-primary}"
    typography: "{typography.body}"
    rounded: "{rounded.item}"
    height: "36px"
  tray-menu-row-hover:
    backgroundColor: "{colors.row-hover}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.item}"
  tray-menu-row-pressed:
    backgroundColor: "{colors.row-pressed}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.item}"
  search-field:
    backgroundColor: "{colors.input-surface}"
    textColor: "{colors.text-primary}"
    typography: "{typography.body}"
    rounded: "{rounded.field}"
    width: "264px"
    height: "38px"
  settings-dialog:
    backgroundColor: "{colors.menu-surface}"
    textColor: "{colors.text-primary}"
    typography: "{typography.settings}"
    width: "620px"
    height: "720px"
---

# Design System: TaskFolder

## Overview

**Creative North Star: "The Fluent Launch Surface"**

TaskFolder is a compact Windows utility, not a branded dashboard. Its visual system replaces legacy Win32 menu chrome with a restrained Windows 11-inspired surface while preserving the speed, density, hierarchy, and interaction model users expect from a tray launcher. The root tray menu, category and profile submenus, and shortcut context menus must feel like one continuous native system.

The design is quiet and functional: warm-neutral surfaces, crisp dark text, soft corners, one-pixel structure, and a single blue focus signal. Shortcut icons and the optional user-selected logo carry the visual identity; decoration must never compete with scanability or one-click launching.

The tokens and component behavior below were extracted from the WinForms/GDI implementation. Static review and the Release build passed with a SHIP disposition; actual Windows GUI appearance at 100–200% display scaling remains to be verified on representative monitors.

**Key Characteristics:**

- Compact, readable Windows 11 utility density.
- One renderer across root, nested, and right-click menus.
- Adjustable menu type and row height stored as device-local logical values.
- Solid tonal states, precise dividers, and OS-provided menu shadow.
- Resizable, scroll-safe Settings sections built from native adaptive layout panels.

## Colors

The palette is almost entirely neutral. Blue is reserved for active keyboard/text-entry focus; it is not a decorative accent.

### Primary

- **Windows Focus Blue:** Draws the two-pixel focused border around the search field so active text entry is unmistakable.

### Neutral

- **Soft Utility Canvas:** Carries every menu surface and the Settings dialog background.
- **Inset Search Surface:** Separates the search field from the menu without a card or shadow.
- **Quiet Structure:** Defines the outer menu border, idle search border, settings logo preview, and separators.
- **Responsive Gray:** Replaces Quiet Structure on search hover and communicates pointer readiness without color.
- **Hover Wash:** Fills selected rows and checked-item backgrounds.
- **Pressed Wash:** Deepens the row or checked-item fill during activation.
- **Primary Ink:** Carries enabled menu labels, search text, and settings copy.
- **Secondary Ink:** Carries submenu arrows and the search/clear glyphs.
- **Unavailable Ink:** Carries disabled rows and arrows, including no-results states.

**The Blue Means Focus Rule.** Use focus-blue only for a live focus affordance unless a future implemented control establishes another interaction role.

**The Solid-State Rule.** Hover and pressed feedback use solid tonal fills; do not reintroduce glossy Win32 gradients.

**The Clean Repaint Rule.** Repaint every menu row with Soft Utility Canvas before applying an optional hover or pressed wash so selection changes cannot leave stale highlight pixels.

## Typography

**Display Font:** None; this utility has no display typography.

**Body Font:** Segoe UI Variable Text with Segoe UI fallback.

**Character:** Native, clear, and deliberately unshowy. The same regular-weight family carries shortcut rows, search, nested menus, context menus, and Settings so hierarchy comes from grouping and state rather than decorative type changes.

### Hierarchy

- **Menu Body:** Regular weight, user-adjustable from 9pt through 18pt; applies together to root rows, search text, submenus, and shortcut context menus.
- **Settings Body:** Regular 9.5pt; inherited by labels, group boxes, buttons, check boxes, combo boxes, and numeric inputs unless Windows supplies a native control-specific treatment.
- **Empty/Disabled Label:** Uses the Menu Body role with Unavailable Ink; it remains a row in the menu rhythm but cannot be activated.

Text is rendered as a single vertically centered line with end ellipsis and no added text padding. Long shortcut and category names truncate rather than increasing row height or wrapping.

**The One Type Voice Rule.** Do not introduce headings or novelty faces into tray-menu rows; logo imagery is the optional branding moment.

**The Logical Size Rule.** Persist point size as an unscaled preference, then let WinForms and the current monitor DPI produce the physical result.

## Layout

The tray menu is a single compact vertical stack. Its container uses md-radius corners and asymmetric inset padding from the xs/sm spacing steps. An optional logo-only header precedes the search field; both are separated from shortcut content with full-width rhythm and inset one-pixel dividers. The search control is a fixed logical 264 by 38 pixels with an md spacing margin horizontally and sm spacing margin vertically.

Menu rows default to 36 logical pixels and may be set from 32 through 72 in two-pixel increments. The full menu tree uses the same row-height and font preferences. Row hover geometry is inset by the xs spacing step on each horizontal edge and half that amount vertically, preventing selected fills from touching the menu border.

Separators begin and end at the lg spacing inset. Category, Most Used, Recently Used, and Switch Profile entries open nested menus; shortcut rows also expose a right-click context menu. All descendants use the same surface, typography, row sizing, and renderer rather than falling back to legacy submenu chrome.

Settings opens as a resizable 620 by 720 logical-pixel shell with a 560 by 600 minimum and `AutoScaleMode.Dpi`. A fill-docked scroll host provides vertical overflow fallback, while a top-docked, single-column `TableLayoutPanel` grows to the real content height. Auto-sized native group boxes form the vertical sections with 8-pixel gaps; the right-aligned OK/Cancel footer remains the final content row.

Hotkey fields, logo actions, and item-height/text-size setting rows use wrapping `FlowLayoutPanel` groups so narrow or high-scale windows can reflow instead of clipping. The logo and shortcuts layouts use percentage/auto-sized table columns, file paths use end ellipsis, and About copy computes its own height within a bounded readable width.

**The Whole Tree Rule.** Any new submenu or shortcut context action must be created through the shared Windows 11 menu classes so it inherits the same renderer and DPI behavior.

**The Scale Once Rule.** Treat stored row height, field size, radius, padding, and offsets as 96-DPI logical values; scale them once against the owning control's `DeviceDpi`.

## Elevation & Depth

The system is flat inside each surface. Depth comes from the rounded one-pixel menu outline, the inset search surface, tonal interaction fills, and the native Windows drop shadow enabled on each context menu. There are no custom shadow values, gradients, translucency, or animated elevation tokens in the implementation.

**The Native Shadow Rule.** Let Windows own context-menu shadow rendering; do not approximate it with a second custom shadow layer.

## Shapes

The recurring silhouette is a softly rounded 8-logical-pixel container or field enclosing tighter 4-logical-pixel row states. The outer menu region and its painted border use the same DPI-scaled radius, so clipping and outline stay aligned. Separators remain straight one-pixel rules. Native Settings controls retain their Windows-provided geometry; only the logo preview explicitly adds a single-line rectangular border.

**The Nested Radius Rule.** Outer surfaces and fields use the larger radius; transient row highlights use the smaller radius and remain inset from the edge.

## Components

### Tray Menu Container

- **Character:** A compact rounded utility surface with a quiet outline and native drop shadow.
- **Content:** Optional logo header, search, dynamic shortcut sections, utility actions, Settings, and Exit.
- **Behavior:** Uses the manager-level renderer so automatically created dropdowns match the root surface.
- **DPI:** Padding, clipping radius, row metrics, search size, and separator insets scale from the menu's current `DeviceDpi`.

### Menu Rows

- **Default:** Soft Utility Canvas with Primary Ink and left-aligned icon/text.
- **Hover / Selected:** Inset Hover Wash with the smaller item radius.
- **Pressed:** Inset Pressed Wash with the same silhouette.
- **Repaint:** The renderer clears the complete item rectangle to Soft Utility Canvas before drawing any optional state wash.
- **Disabled / Empty:** Unavailable Ink, no activation, and the same vertical rhythm.
- **Text:** Vertically centered, single-line, end-ellipsized, with no extra renderer padding.
- **Height:** Defaults to 36 logical pixels and follows the persisted 32–72 range.

### Nested and Context Menus

- **Nested navigation:** Category, Most Used, Recently Used, and profile rows use muted native arrows.
- **Checked state:** The active profile uses the shared neutral hover/pressed fills behind the native check indicator.
- **Shortcut context:** Rename, Remove, Open File Location, Change Icon, Move Up, and Move Down use identical menu rows and separators.
- **Consistency:** Submenus create the same context-menu class with the same font size and renderer; no descendant may use a separate color table.

### Search Field

- **Structure:** Inset Search Surface, larger field radius, one-pixel idle border, left search glyph, and an unbordered native text editor.
- **Hover:** Border changes to Responsive Gray.
- **Focus:** Border changes to Windows Focus Blue and increases to two pixels.
- **Clear action:** A muted X appears in the right 32-logical-pixel zone only when text exists; hovering that zone uses the hand cursor, and clicking clears the filter.
- **Sizing:** 264 by 38 logical pixels, with 34 pixels reserved at left and 32 at right before DPI scaling.
- **Accessibility:** Both host and editor expose the accessible name “Search shortcuts.”

### Logo Header

- **Presence:** Optional and logo-only; no header text is rendered.
- **Sizing:** The loaded bitmap follows the current menu row height, clamped to 32–64 scaled pixels.
- **Placement:** Middle-left with compact internal padding, followed by a separator.

### Separators and Arrows

- **Separators:** One-pixel Quiet Structure lines inset by 12 logical pixels; the light side matches the surface.
- **Arrows:** Secondary Ink when enabled and Unavailable Ink when disabled.

### Settings Dialog

- **Shell:** Resizable, centered 620 by 720 logical-pixel dialog with a 560 by 600 minimum, shared Soft Utility Canvas, and vertical AutoScroll fallback.
- **Grouping:** A top-docked auto-sized table stack holds auto-height native group boxes for Startup, Notifications, Global Hotkey, Menu Appearance, Shortcuts Management, and About.
- **Inputs:** Native check boxes, drop-down lists, numeric steppers, file dialog, and buttons preserve Windows interaction and focus behavior.
- **Responsive rows:** Hotkey, logo-action, and setting rows wrap horizontally when width is constrained; file-path labels ellipsize, and About copy grows vertically instead of clipping.
- **Appearance controls:** Item height accepts 32–72 in steps of 2; text size accepts 9–18pt in steps of 0.5pt; both numeric inputs are 80 logical pixels wide, and the 40-pixel logo preview centers a 32-pixel bitmap.
- **Actions:** Choose/Remove buttons have an 88 by 32 minimum, shortcut-management buttons have a 190 by 34 minimum, and footer buttons have a 96 by 34 minimum. OK is the accept action and Cancel is the cancel action.

## Do's and Don'ts

### Do:

- **Do** use the shared renderer, context-menu class, and font factory for every tray, nested, and shortcut context surface.
- **Do** preserve a single-line, vertically centered, ellipsized label in every adjustable-height menu row.
- **Do** scale logical menu geometry from the owning control's current DPI and keep persisted preferences unscaled.
- **Do** use native WinForms controls and Windows visual styles for Settings unless implementation establishes a complete replacement system.
- **Do** preserve visible default, hover/selected, pressed, disabled, separator, checked, and focused-search states.
- **Do** clear a menu row to the base surface before drawing its current hover or pressed state.
- **Do** keep Settings sections auto-sized, wrapping, and vertically scrollable at constrained sizes and higher DPI.

### Don't:

- **Don't** mix a legacy-rendered submenu or context menu into the Windows 11 menu tree.
- **Don't** use blue as decoration, a row hover fill, or a general brand wash; it currently means focused text entry.
- **Don't** add gradients, extra shadows, translucency, animation, or ornamental typography that is absent from the implementation.
- **Don't** wrap menu labels or allow text size to change the configured minimum row rhythm.
- **Don't** restore fixed-position Settings sections or fixed-height copy that clips when the window narrows or DPI increases.
- **Don't** claim 100–200% DPI behavior is visually verified until the real GUI has been exercised on representative Windows displays.
