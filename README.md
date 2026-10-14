# SwitchPeek

A tiny Windows utility that shows a macOS/GNOME-style keyboard layout indicator in the center of the screen whenever you switch input languages.

![SwitchPeek in light and dark themes](docs/preview.png)

Windows only changes a couple of letters in the corner of the taskbar when you switch layouts. SwitchPeek briefly shows a large, clear badge in the middle of the screen (`EN / English`, `RU / Русский`, `UA / Українська`, …) and fades it out, so you always know what you're about to type.

## Features

- **Shows only on a real layout switch.** Moving between windows that have different layouts doesn't trigger it.
- **Works with any switching method:** Alt+Shift, Ctrl+Shift, Win+Space, the language bar, or any language installed in Windows.
- **Theme-aware:** a light or dark badge, matching the system theme.
- **Multi-monitor and DPI-aware:** appears on the monitor with the active window and stays crisp at any scaling.
- **Unobtrusive:** never steals focus, clicks pass through it, and it stays hidden over fullscreen games.
- **Lightweight:** a single ~20 KB executable with no dependencies, no installer required, and near-zero CPU usage.
- **Tray icon** with *Show now*, *Run at Windows startup*, and *Exit*. The menu follows the Windows display language (English, Russian, or Ukrainian).

## Requirements

- Windows 10 or 11
- .NET Framework 4.x (preinstalled on every supported version of Windows)

## Build

No Visual Studio or SDK needed. SwitchPeek is compiled with the C# compiler that ships with Windows:

```bat
build.cmd
```

The result is `bin\SwitchPeek.exe`.

## Install

1. Copy `SwitchPeek.exe` anywhere, for example `%LOCALAPPDATA%\Programs\SwitchPeek\`.
2. Run it. A tray icon appears, and the indicator starts working right away.
3. To launch it at sign-in, right-click the tray icon and enable **Run at Windows startup**.

To uninstall, turn off autostart in the tray menu, choose **Exit**, and delete the file.

## Customization

All tunables live at the top of [`src/SwitchPeek.cs`](src/SwitchPeek.cs) in the `Settings` class. Rebuild after changing them.

| Setting | Default | Meaning |
|---|---|---|
| `ShowMs` | `700` | How long the badge stays fully visible (ms) |
| `FadeInMs` / `FadeOutMs` | `70` / `300` | Fade animation duration (ms) |
| `Width` / `Height` | `200` / `150` | Badge size in logical pixels |
| `Radius` | `26` | Corner radius |
| `CodeFontPx` / `NameFontPx` | `60` / `17` | Font size of the language code and name |
| `ShadowSoftness` / `ShadowOffsetY` | `6` / `4` | Shadow blur radius and vertical offset |
| `ShadowAlphaLight` / `ShadowAlphaDark` | `70` / `120` | Shadow opacity in the light and dark themes |
| `PollMs` | `60` | How often the current layout is checked (ms) |
| `IgnoreAfterFocusMs` | `400` | Grace period after a window switch |

To render the badges to PNG files without running the app (handy when tweaking the look):

```bat
bin\SwitchPeek.exe --preview out
```

## How it works

SwitchPeek polls the keyboard layout of the thread that owns the focused window (`GetGUIThreadInfo` + `GetKeyboardLayout`). When the layout changes within the same foreground window, it draws the badge into a per-pixel-alpha layered window (`UpdateLayeredWindow`) that is topmost, click-through, and never activated. The language code and native name come from the layout's language ID via `CultureInfo`.
