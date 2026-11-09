# DifFuse

A Windows 11 desktop app for Fender Mustang amps over USB. It replaces the discontinued Fender FUSE.
The sound is drawn as a route, like a metro map: guitar to speaker, one station per effect, the amp as the
interchange, and below it the amp unit and four effect units. Each control's shape matches what it does:
amounts turn, a few named positions (Sag, noise gate, Shape, Mode) are stops on a line, off/on is a switch,
and the bar at the bottom explains whatever control you point at. All sound comes from the amp's own DSP;
DifFuse only edits and stores presets.

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

## Running it

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/DifFuse.App
```

Options:

| Option | What it does |
| --- | --- |
| `--simulate` | Uses a built-in simulated amp, no hardware needed. |
| `--trace` | Logs every USB packet to `%LOCALAPPDATA%\DifFuse\logs`. |
| `--library DIR` | Uses another library folder. |
| `--snapshot DIR` | Renders screenshots against the simulated amp, then exits. |

To build a folder you can run without the SDK (needs the .NET 10 Desktop Runtime):

```bash
dotnet publish src/DifFuse.App -c Release -o publish
```

No driver is needed: the amp's control interface is a standard HID device.

### Where things are kept

- Library: `Documents\DifFuse\Library\<bank>\<preset>.fuse`. Deleted presets and banks go to the Recycle Bin.
- Settings (window size, last bank, author name): `%APPDATA%\DifFuse\settings.json`.

### Notes

- Bypassing an effect removes it from the amp's live sound. When you save to an amp slot, a bypassed
  effect is stored as an empty slot; the library keeps it with its settings.
- FUSE's `AmpData\M2_xx.fuse` files only hold names and notes, not amp settings. The importer reports
  and skips them.
- On a PC with Smart App Control turned on, Windows may block freshly built, unsigned copies of the
  app ("An Application Control policy has blocked this file").

## Building and testing

```bash
dotnet test DifFuse.slnx
```

| Project | Contents |
| --- | --- |
| `src/DifFuse.Core` | Packet codec, amp/effect catalogs, protocol worker, connection manager, simulated amp, FUSE files, library. No UI. |
| `src/DifFuse.Hid` | Windows HID transport (SetupAPI + hid.dll, 65-byte reports). |
| `src/DifFuse.App` | WPF app: route map, amp and effect units, custom knob and stop controls, dialogs. |
| `tests/DifFuse.Core.Tests` | Protocol, FUSE, library and reconnect tests against the simulated amp. |
| `tests/DifFuse.App.Tests` | View-model tests, including "never save to the amp without confirmation". |

## Credits and licence

The USB protocol layout comes from [PLUG](https://github.com/offa/plug), the Linux FUSE replacement
by offa and contributors (GPL-3.0). DifFuse's code is written from scratch in C#, but because the
protocol knowledge is taken from GPL code, a GPL-compatible licence may be needed. No licence has
been chosen yet.

Fender, Mustang and FUSE are trademarks of Fender Musical Instruments Corporation. DifFuse is not
affiliated with or endorsed by Fender.
