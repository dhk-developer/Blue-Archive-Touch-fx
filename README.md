# Blue-Archive-Touch-fx

A lightweight Windows desktop click-effect overlay inspired by Blue Archive's existing touch FX. 

When the user left-clicks, the executable draws a short blue burst effect at the cursor position via a transparent click-through overlay. The executable lives in system tray. As of V1.0, it is possible to load this on startup natively.

## Features

- Visual click effect inspired (not a 1-1 copy) by Blue Archive's touch fx.
- Start with Windows toggle
- Automatic color modes (Light / Dark)
- `Ctrl + Alt + Q` emergency quit shortcut if needed.
- Lightweight rendering using a custom WPF drawing surface

## Requirements

- Windows 10 or Windows 11

## Installation

Download the latest release and run the executable.
If you are unsure which one to download, choose the standalone version. This will self-install a .NET dependency.

The app will appear in the system tray. Right-click the tray icon to access:

- **Start with Windows**
- **Exit**

By default, the app registers itself to start with Windows when launched. This can be turned off at any time from the tray menu.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl + Alt + Q` | Force quit the app |

## How it works

- **Click-through overlay.** A borderless, transparent, top-most WPF window spans the whole virtual screen. Its extended style is *transparent + layered + tool window*, so every click reaches the app underneath and the overlay stays out of Alt-Tab.
- **Global input.** A low-level mouse hook records left-button presses and hands them to the UI thread, so the hook returns immediately.
- **Render only while alive.** The drawing surface subscribes to WPF's per-frame render callback only while an effect exists, and unsubscribes when the last one fades.
- **Bounded cost.** Hard caps (32 burst particles, 3 pulses, 3 arcs). Rapid clicking shrinks new bursts instead of stacking them. Brushes, pens and geometry are frozen for cheap redraws.
- **Theme-aware.** Reads the Windows light/dark setting and uses higher-contrast colours on bright backgrounds.

Write-up with a screen recording: [dhk-developer.github.io/touch-fx.html](https://dhk-developer.github.io/touch-fx.html).

## Notes

This is a fan-made visual utility and is not affiliated with, endorsed by, or connected to Blue Archive, Nexon, or any related rights holders.
