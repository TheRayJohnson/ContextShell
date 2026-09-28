[![Build](https://github.com/TheRayJohnson/ContextShell/actions/workflows/build.yml/badge.svg)](https://github.com/TheRayJohnson/ContextShell/actions/workflows/build.yml)

# ContextShell

A context menu manager for Windows 10 and 11 File Explorer. Free, open source, MIT licensed.

ContextShell is a fork of [Nilesoft Shell](https://github.com/moudey/Shell). It keeps the Shell engine and its `.nss`
config language. It ships without a launcher, plugin marketplace, auto-updater, or telemetry. You configure it in plain text files.

<p align="center">
<img src="/screenshots/folder-back.png"><img src="/screenshots/file-manage.png"><br>
<img src="/screenshots/terminal.png"><img src="/screenshots/taskbar.png"><br>
</p>

## Features

* Lightweight and portable. Two binaries (`shell.dll`, `shell.exe`) and plain-text config.
* Fully customizable appearance: themes, colors, fonts, borders, shadows, acrylic/transparency.
* Add custom items: sub-menus, menu items, separators, multi-column menus.
* Modify, move, or remove existing items added by Windows or by third-party software.
* Works on files, folders, drives, desktop, the navigation pane (Quick Access, This PC), and the taskbar.
* Expression syntax with built-in functions and predefined variables (`sel.path`, `key.shift()`, `window.is_desktop`, …).
* Icons from glyphs, SVG, embedded resources, `.ico`, `.png` and `.bmp`.
* Replaces the Windows 11 compact menu with the full classic menu, so you don't need to click "Show more options".

## Requirements

* Windows 10 or Windows 11 (x64, x86 or ARM64)

## Install

> **Uninstall other Shell-based menus first.** Nilesoft Shell, iMA Menu, and ContextShell hook the same Explorer menu.
> Only one of them can be active at a time.

1. Download `ContextShell-<version>-x64.msi` (or the portable zip) from [Releases](https://github.com/TheRayJohnson/ContextShell/releases).
2. Run the installer. It registers the extension and restarts Explorer.

Portable / manual install from the zip, in an elevated terminal:

```powershell
.\shell.exe -register -treat -restart
```

To uninstall manually:

```powershell
.\shell.exe -unregister -restart
```

## Configure

Edit `shell.nss` in the install folder (default `C:\Program Files\ContextShell`). It imports the files in `imports\`.
Changes apply the next time you open a menu. Hold `Shift` and right-click the taskbar for quick links to the config and folder.

Example: an "Open in new window" item for every folder, including Quick Access pins:

```nss
item(type='dir' title='Open in new window' image= cmd='explorer.exe' args='"@sel.path"')
```

Config syntax docs: [docs/](docs/). Syntax is compatible with Nilesoft Shell, so its
[online docs](https://nilesoft.org/docs) apply too.

## Build

Visual Studio 2022 with the "Desktop development with C++" workload, plus the WiX v4 toolset for the installer.

```powershell
git clone --recurse-submodules https://github.com/TheRayJohnson/ContextShell
nuget restore src/Shell.sln
msbuild /m /p:Configuration=release /p:Platform=x64 src/Shell.sln
```

Output goes to `src/bin`. Every push to `main` builds x64, x86 and ARM64 on GitHub Actions.
Pushing a `v*` tag publishes a release with zips and MSIs.

## Credits and license

MIT License. See [LICENSE](LICENSE).

Based on [Nilesoft Shell](https://github.com/moudey/Shell) by Nilesoft Ltd. (MIT).
Uses [plutovg](https://github.com/sammycage/plutovg), [plutosvg](https://github.com/sammycage/plutosvg), and
[Microsoft Detours](https://github.com/microsoft/Detours).
