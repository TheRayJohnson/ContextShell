[![Build](https://github.com/TheRayJohnson/ContextShell/actions/workflows/build.yml/badge.svg)](https://github.com/TheRayJohnson/ContextShell/actions/workflows/build.yml)

# ContextShell

A context menu manager for Windows 10 and 11 File Explorer. Free, open source, MIT licensed.

ContextShell is a fork of [Nilesoft Shell](https://github.com/moudey/Shell). It keeps the Shell engine and its `.nss`
config language, and adds a settings app, a theme editor and a proper installer. No plugin marketplace, no ads, no telemetry.

**Website and documentation: https://therayjohnson.github.io/ContextShell/**

<p align="center">
<img src="/screenshots/folder-back.png"><img src="/screenshots/file-manage.png"><br>
<img src="/screenshots/terminal.png"><img src="/screenshots/taskbar.png"><br>
</p>

## Features

* **ContextShell Settings**: pick a theme, edit every theme option with a live preview, and turn menu sections on or off.
  No more editing config files in Notepad as administrator.
* **Themes that follow Windows light and dark mode**, including Liquid Glass: a translucent, frosted menu with a bright edge.
* Add custom items: sub-menus, menu items, separators, multi-column menus.
* Modify, move, or remove existing items added by Windows or by third-party software.
* Works on files, folders, drives, desktop, the navigation pane (Quick Access, This PC), and the taskbar.
* Replaces the Windows 11 compact menu with the full menu, so you don't need to click "Show more options".
* Update notifications from GitHub Releases, verified with SHA-256 before anything runs. You can turn them off.
* Expression syntax with built-in functions and variables (`sel.path`, `key.shift()`, `sys.dark`, …).

## Requirements

* Windows 10 or Windows 11 (x64, x86 or ARM64)
* .NET Framework 4.8 for Setup and Settings. It's built into Windows 10 (1903 and later) and Windows 11.

## Install

> **Uninstall other Shell-based menus first.** Nilesoft Shell, iMA Menu, and ContextShell hook the same Explorer menu.
> Only one of them can be active at a time.

Download `ContextShell-<version>-x64-setup.exe` from [Releases](https://github.com/TheRayJohnson/ContextShell/releases)
(use `arm64` for Windows on ARM) and run it. Setup lets you choose the install folder, shortcuts and update checks,
adds ContextShell to File Explorer, and opens ContextShell Settings.

Running setup again when ContextShell is installed offers **Repair** and **Uninstall**. You can also uninstall from
Settings > Apps.

### Silent and managed installs

```powershell
.\ContextShell-v1.9.20-x64-setup.exe /quiet
```

| Option | Meaning |
|---|---|
| `/quiet` | No UI. The exit code is the Windows Installer result (0 or 3010 means success). |
| `/passive` | Progress only, no questions. |
| `/uninstall`, `/repair` | Remove or repair the current install. |
| `/log <file>` | Verbose Windows Installer log. |
| `/extract <dir>` | Save the embedded MSI, for msiexec, Intune or Group Policy. |
| `INSTALLFOLDER=<path>` | Install location. |
| `ADDSTARTMENU=0\|1`, `ADDDESKTOP=0\|1` | Start menu (default on) and desktop (default off) shortcuts. |
| `AUTOUPDATE=0\|1` | Check GitHub for updates at sign-in (default on). |

The plain MSI is in every release too and takes the same properties: `msiexec /i ContextShell-v1.9.20-x64.msi /qn AUTOUPDATE=0`.

### Portable

Extract the portable zip, then in an elevated terminal:

```powershell
.\shell.exe -register -treat -restart
```

To remove it: `.\shell.exe -unregister -restart`.

## Configure

Open **ContextShell Settings** from the Start menu, or hold `Shift` and right-click the taskbar.

* **Appearance**: choose a theme, or **Customize** one. The editor covers every option in
  [Themes](https://therayjohnson.github.io/ContextShell/docs/configuration/themes/), and any color can have a separate dark-mode value.
  Built-in themes are never overwritten: your version is saved as a new theme.
* **Menu**: turn sections like Terminal, File manage and Go To on or off, set the submenu delay, and restart Explorer.
* **Updates**: check now, or turn sign-in checks off.

The config files live in Program Files, so saving asks for administrator permission once.

### Themes

| Theme | File | Look |
|---|---|---|
| Windows | `themes/auto.nss` | Solid, follows Windows light/dark. The default. |
| Liquid Glass | `themes/liquid-glass.nss` | Translucent frosted glass with a bright edge, light and dark |
| Dark / Light | `themes/dark.nss`, `themes/light.nss` | Solid |
| Acrylic | `themes/acrylic.nss` | Frosted blur, mostly opaque |
| Classic glass | `themes/glass.nss` | Nilesoft's default. Very see-through on busy wallpapers |

Glass and acrylic effects need **Transparency effects** turned on in Windows Settings > Personalisation > Colours.

### Editing config files by hand

Everything Settings does is plain text in the install folder (default `C:\Program Files\ContextShell`):
`shell.nss` imports the files in `imports\`, and `imports\theme.nss` imports the active theme from `imports\themes\`.
Changes apply the next time you open a menu.

Example: an "Open in new window" item for every folder, including Quick Access pins:

```nss
item(type='dir' title='Open in new window' image=\uE0E8 cmd='explorer.exe' args='"@sel.path"')
```

Full syntax reference: [ContextShell docs](https://therayjohnson.github.io/ContextShell/docs/). The syntax is the same as
Nilesoft Shell, so existing Nilesoft configs work unchanged.

## Updates

When you sign in, `ContextShell.exe --check-updates --background` asks the GitHub API for the latest release, at most
about once a day. If there's a newer version, a small notification offers **Update now**, **Later** or
**Skip this version**. Updating downloads the setup EXE for your architecture, checks it against the release's
`SHA256SUMS.txt`, and runs it. Your themes and menu settings are kept. Turn this off in Settings > Updates, or install
with `AUTOUPDATE=0`.

## Build

Requirements: Visual Studio 2022 with "Desktop development with C++" (v143, including ARM64 tools if you build ARM64),
and the .NET SDK 8 or later. The WiX Toolset comes from NuGet, and .NET Framework 4.8 reference assemblies come with the SDK.

```powershell
git clone --recurse-submodules https://github.com/TheRayJohnson/ContextShell
cd ContextShell
nuget restore src/Shell.sln
msbuild /m /p:Configuration=release /p:Platform=x64 src/Shell.sln
./src/setup/build-installer.ps1 -Platform x64
```

`build-installer.ps1` builds ContextShell Settings, then the MSI (`src/bin/setup-x64.msi`), then the branded setup
(`src/bin/setup-x64.exe`), which embeds the MSI.

| Path | What |
|---|---|
| `src/dll`, `src/exe`, `src/shared` | Shell extension and manager (C++) |
| `src/setup/wix` | MSI (WiX 5): files, registry, shortcuts, upgrades |
| `src/setup/ca` | MSI custom actions: register with Explorer, restart it |
| `src/app/Setup` | Branded setup UI (WPF). Runs the MSI through the Windows Installer API |
| `src/app/Settings` | ContextShell Settings and the update checker (WPF) |
| `src/app/Common` | Shared Windows 11 styles, light/dark palettes, update client |
| `docs/`, `site/` | Documentation pages and the website builder. `python site/build.py` writes `site/_site`, which GitHub Pages serves |

To preview the UI without installing anything, run `setup-x64.exe /screenshots <dir>` or
`ContextShell.exe --screenshots <dir> --config src/bin`. Both render every page in light and dark mode to PNG.

**Code signing** is optional. Set `CS_SIGN_PFX` (a .pfx file) and `CS_SIGN_PASSWORD` before running
`build-installer.ps1`, or add `CS_SIGN_PFX_BASE64` and `CS_SIGN_PASSWORD` as repository secrets for CI. The binaries,
MSI and setup EXE are then signed with `signtool` and timestamped.

**CI** builds x64, x86 and ARM64 on every push. It installs the previous release, upgrades it, repairs and uninstalls,
and runs an msiexec install with custom properties (`src/setup/test-installer.ps1`). It also uploads UI screenshots.
Pushing a `v*` tag that matches the version in `src/setup/wix/var.wxi` publishes a release.

## Credits and license

MIT License. See [LICENSE](LICENSE).

Based on [Nilesoft Shell](https://github.com/moudey/Shell) by Nilesoft Ltd. (MIT).
Uses [plutovg](https://github.com/sammycage/plutovg), [plutosvg](https://github.com/sammycage/plutosvg), and
[Microsoft Detours](https://github.com/microsoft/Detours).
