# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

Windows users who keep applications, PWAs, scripts, and links in a tray launcher and need to find and launch them quickly without adding taskbar clutter.

## Product Purpose

TaskFolder provides immediate access to user-managed shortcuts from the Windows system tray. Success means a shortcut can be found and launched quickly with clear text, predictable menu behavior, and no visual friction.

## Operating Context

TaskFolder runs as a lightweight Windows Forms tray application on Windows 11 and Windows 10. The primary interaction is opening the tray menu by mouse or global hotkey, optionally filtering shortcuts, and navigating nested categories and profiles.

## Capabilities and Constraints

- Existing shortcut, category, profile, recent, most-used, icon, and search behavior must remain intact.
- The tray menu must remain usable at Windows display scaling from 100% through 200%.
- Menu text size and row height are device-local persisted settings.
- The interface is implemented with .NET 8 Windows Forms and native Windows drawing APIs; no production dependency is required for the redesign.
- Windows 11 is the primary visual target while Windows 10 remains supported.

## Brand Commitments

Preserve the TaskFolder name, application icon, optional user-selected menu logo, and restrained utility-app voice.

## Evidence on Hand

- Existing implementation and behavior in `Program.cs`, `Views/SettingsForm.cs`, and `Models/AppSettings.cs`.
- Product documentation and supported workflows in `README.md`.
- Existing application icon and imagery under `Resources/` and `Images/`.

## Product Principles

- Keep launching fast: common shortcuts remain one click away.
- Prefer crisp, readable, DPI-aware text over decorative density.
- Follow Windows interaction conventions across the root menu and every submenu.
- Make visual comfort adjustable without changing shortcut behavior.
- Preserve lightweight operation and backward-compatible settings.

## Accessibility & Inclusion

The primary menu must support Windows display scaling from 100% through 200%, adjustable text sizing, visible keyboard focus, and sufficient contrast in normal, hover, selected, disabled, and separator states.
