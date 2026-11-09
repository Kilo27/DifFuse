# DifFuse

A Windows 11 desktop app for Fender Mustang amps over USB. It replaces the discontinued Fender FUSE
with a dark, guitarix-style rack: an amp unit and four effect units with real knobs. All sound comes
from the amp's own DSP; DifFuse only edits and stores presets.

## Features

- Finds the amp on its own, shows the model and connection state, and reconnects after unplug/replug.
- Lists the amp's preset slots, shows which one is playing, and loads a slot with one click.
- Amp unit: every v1 and v2 amp model and every control, including gain 2, master volume, presence,
  bias, sag, noise gate (with threshold and depth for the Custom gate), cabinet, bright and USB gain.
- Stomp, modulation, delay and reverb units: model picker (v2 effects included), knobs labelled per
  model, on/bypass, and placement before or after the amp. A signal-chain strip shows the order.
- Knob moves go to the amp live, throttled so the USB link keeps up.
- Save the current sound to an amp slot with a name. DifFuse always asks before it replaces a slot.
- A PC preset library in banks. Each bank is a folder of FUSE-format `.fuse` files, so FUSE presets
  can be imported, exported, or simply dropped into a bank folder.

Supported amps (USB vendor 0x1ED8): Mustang I/II, III/IV/V, Bronco, Mini, Floor and the v2 models
(Mustang I/II v2, III/IV/V v2). Day-to-day testing so far has been on a Mustang I/II v2.

## Installing

Run `DifFuse-Setup-<version>.exe`: double-click it, or choose **Open file** after downloading it.
That's all. Setup asks no questions and needs no admin rights. It shows a progress bar for a few
seconds, then starts DifFuse.

Setup installs DifFuse for the current Windows user, in `%LOCALAPPDATA%\Programs\DifFuse`, and adds:

- **DifFuse** to Start > All apps and to Windows search,
- a desktop shortcut,
- an entry in **Settings > Apps > Installed apps**, for uninstalling.

Everything DifFuse needs is included, .NET too, so nothing else has to be installed. No driver is
needed either: the amp's control interface is a standard HID device. It runs on 64-bit Windows 10
and 11.

The setup file is built from this repository; see [Build the installer](#4-build-the-installer).

**"Windows protected your PC."** The installer isn't code-signed yet, so when it has been
downloaded, SmartScreen warns about it the first time. Click **More info**, then **Run anyway**.

**Pinning.** To pin DifFuse to the taskbar, start it, right-click its taskbar icon and choose
**Pin to taskbar**. To pin it to Start, right-click it in Start > All apps and choose
**Pin to Start**.

**Updating.** Run the setup for the newer version. It replaces the old one in place, and shortcuts
and pins keep working. If DifFuse is open, Setup asks to close it first.

**Uninstalling.** Open **Settings > Apps > Installed apps**, click **...** next to DifFuse and
choose **Uninstall**. This removes the program and its shortcuts but keeps your presets and
settings. To remove those too, delete `Documents\DifFuse`, `%APPDATA%\DifFuse` and, if you used
`--trace`, `%LOCALAPPDATA%\DifFuse`.

## Using it

`DifFuse.exe` takes these options. To use one every time, right-click a DifFuse shortcut >
**Properties** and add the option after the closing quote in **Target**.

| Option | What it does |
| --- | --- |
| `--simulate` | Uses a built-in simulated amp, no hardware needed. |
| `--trace` | Logs every USB packet to `%LOCALAPPDATA%\DifFuse\logs`. |
| `--library DIR` | Uses another library folder. |
| `--snapshot DIR` | Renders screenshots against the simulated amp, then exits. |

### Where things are kept

- Library: `Documents\DifFuse\Library\<bank>\<preset>.fuse`. Deleted presets and banks go to the Recycle Bin.
- Settings (window size, last bank, author name): `%APPDATA%\DifFuse\settings.json`.

### Notes

- Bypassing an effect removes it from the amp's live sound. When you save to an amp slot, a bypassed
  effect is stored as an empty slot; the library keeps it with its settings.
- FUSE's `AmpData\M2_xx.fuse` files only hold names and notes, not amp settings. The importer reports
  and skips them.
- On a PC with Smart App Control turned on, Windows blocks DifFuse and its installer because they
  aren't signed. See [Troubleshooting](#troubleshooting).

## Building from source

Run the commands below in PowerShell or Windows Terminal.

### 1. Install the tools

You need 64-bit Windows 10 or 11, Git and the .NET 10 SDK. The quickest way to get both tools is
`winget`, which comes with Windows:

```powershell
winget install --id Git.Git -e
winget install --id Microsoft.DotNet.SDK.10 -e
```

Or download them from [git-scm.com](https://git-scm.com/download/win) and
[dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) (pick the SDK, not just a
runtime). Close and reopen the terminal so it picks up the new `PATH`, then check:

```powershell
dotnet --list-sdks
```

The list should include a `10.0.x` SDK.

### 2. Get the source

The repository is private, so your GitHub account needs access to it.

```powershell
git clone https://github.com/Kilo27/DifFuse.git
cd DifFuse
```

The first time, Git opens a browser window to sign in to GitHub. All later commands run from this
`DifFuse` folder (the repository root).

### 3. Build, test and run

```powershell
dotnet build DifFuse.slnx -c Release
dotnet test DifFuse.slnx
dotnet run --project src/DifFuse.App
```

The tests run against the simulated amp, so no hardware is needed. With `dotnet run`, options go
after a `--`, for example `dotnet run --project src/DifFuse.App -- --simulate`.

### 4. Build the installer

The installer is made with [Inno Setup](https://jrsoftware.org/isinfo.php), which is free. Install
it once:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

Then, from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

The script runs the tests, publishes a self-contained x64 build of DifFuse, and packs it into
`artifacts\installer\DifFuse-Setup-<version>.exe` (about 45 MB). Add `-SkipTests` to leave out the
tests. `-ExecutionPolicy Bypass` lets this one script run even though Windows blocks PowerShell
scripts by default; it doesn't change any setting.

Before building a release, bump `<Version>` in `Directory.Build.props`. The setup's file name and
the version shown in Installed apps both come from it.

The installer is defined in [installer/DifFuse.iss](installer/DifFuse.iss). Its `AppId` is how
Windows knows that every version is the same app, so a new version replaces the old one. Never
change it.

To share the installer, send the file, or attach it to a GitHub release:

```powershell
gh release create v0.1.0 artifacts\installer\DifFuse-Setup-0.1.0.exe
```

The repository is private, so its releases are only visible to people who have access to it.

<details>
<summary>Installing without the installer</summary>

Publish a self-contained, single-file build into a `publish` folder (use `-r win-arm64` on a
Windows on Arm PC):

```powershell
dotnet publish src/DifFuse.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Then copy it to `%LOCALAPPDATA%\Programs\DifFuse` and add a Start menu shortcut:

```powershell
$dest = Join-Path $env:LOCALAPPDATA 'Programs\DifFuse'
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'DifFuse.lnk'

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path .\publish\* -Destination $dest -Recurse -Force

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
$shortcut.TargetPath = Join-Path $dest 'DifFuse.exe'
$shortcut.WorkingDirectory = $dest
$shortcut.Description = 'Editor for Fender Mustang amps'
$shortcut.Save()
```

This puts DifFuse in Start > All apps and Windows search, but not in Installed apps. To update it,
close DifFuse and run both blocks again. To remove it:

```powershell
Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'Programs\DifFuse')
Remove-Item -Force (Join-Path ([Environment]::GetFolderPath('Programs')) 'DifFuse.lnk')
```

The single-file build unpacks a few support files to `%TEMP%\.net\DifFuse` when it runs; that
folder can be deleted too. Running the installer later takes over this install: it uses the same
folder and shortcut.

</details>

### Troubleshooting

- **"An Application Control policy has blocked this file."** Smart App Control blocks unsigned apps,
  including DifFuse, its installer and builds you make yourself. It can be switched off in Windows
  Security > App & browser control > Smart App Control settings.
- **"Windows protected your PC."** SmartScreen shows this for an unsigned `.exe` that was
  downloaded. Choose **More info** > **Run anyway**. Files built on the same PC don't get this.
- **`build-installer.ps1` says ISCC.exe was not found.** Install Inno Setup (step 4), then open a
  new terminal.
- **"Running scripts is disabled on this system."** Start the script with
  `powershell -ExecutionPolicy Bypass -File ...` as shown in step 4, not as `.\installer\...`.
- **`dotnet` is not recognised, or `dotnet --list-sdks` has no 10.0 SDK.** Open a new terminal after
  installing the SDK. If it is still missing, reinstall it from the link in step 1.
- **DifFuse is not in the Start menu.** Press Win+R, type `shell:programs` and check that
  `DifFuse.lnk` is there. Windows search can take a moment to pick up a new app.
- **The amp isn't found.** Make sure the amp is on and plugged into the PC with a USB data cable,
  then try another port. Start DifFuse with `--trace` and look in `%LOCALAPPDATA%\DifFuse\logs` to
  see what was sent and received.

## Project layout

| Path | Contents |
| --- | --- |
| `src/DifFuse.Core` | Packet codec, amp/effect catalogs, protocol worker, connection manager, simulated amp, FUSE files, library. No UI. |
| `src/DifFuse.Hid` | Windows HID transport (SetupAPI + hid.dll, 65-byte reports). |
| `src/DifFuse.App` | WPF app: rack UI, custom knob control, dialogs. |
| `tests/DifFuse.Core.Tests` | Protocol, FUSE, library and reconnect tests against the simulated amp. |
| `tests/DifFuse.App.Tests` | View-model tests, including "never save to the amp without confirmation". |
| `installer` | Inno Setup script and the PowerShell script that builds the installer. |

## Credits and licence

The USB protocol layout comes from [PLUG](https://github.com/offa/plug), the Linux FUSE replacement
by offa and contributors (GPL-3.0). DifFuse's code is written from scratch in C#, but because the
protocol knowledge is taken from GPL code, a GPL-compatible licence may be needed. No licence has
been chosen yet.

Fender, Mustang and FUSE are trademarks of Fender Musical Instruments Corporation. DifFuse is not
affiliated with or endorsed by Fender.
