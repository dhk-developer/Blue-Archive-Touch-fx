# Blue-Archive-Touch-fx

A lightweight Windows desktop click-effect overlay inspired by Blue Archive's existing touch FX. 

When the user left-clicks, the executable draws a short blue burst effect at the cursor position via a transparent click-through overlay. The executable lives in system tray. As of V1.0, it is possible to load this on startup natively.

## Features

- Visual click effect inspired (not a 1-1 copy) by Blue Archive's touch fx.
- Start with Windows toggle#
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

## Notes

This is a fan-made visual utility and is not affiliated with, endorsed by, or connected to Blue Archive, Nexon, or any related rights holders.
