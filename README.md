# Superliminal Head Tracking

![Superliminal running with this mod](https://raw.githubusercontent.com/itsloopyo/superliminal-headtracking/main/assets/readme-clip.gif)

An unofficial head tracking mod for Superliminal that moves the view with your head while your mouse or controller keeps aiming, driven by a webcam, phone, or any OpenTrack compatible tracker, with no VR headset required.

> **Settings have moved.** This version keeps its settings in `BepInEx\config\CameraUnlock.ini`.
> The first time it starts it reads your settings from the old
> `BepInEx\config\com.cameraunlock.superliminal.headtracking.cfg` into the new file, and leaves the
> old file as it was. BepInEx's ConfigurationManager no longer lists the settings: edit
> `CameraUnlock.ini` with any text editor. [Configuration](#configuration) has the details.

## Features

- **Decoupled look and aim** - your head moves the view, your mouse or controller still points the grab ray
- **6DOF positional tracking** - lean, peek and duck with head position
- **Works with any OpenTrack compatible tracker** - free options available for PC, iOS and Android
- **Resizing stays true to the mouse** - an object grows to the same size whether your head is turned or still

## Requirements

- Superliminal on [Steam](https://store.steampowered.com/app/1049410/Superliminal/) or Xbox Game Pass. The package version tested is 1.0.6.0.
- A tracking source: [OpenTrack](https://github.com/opentrack/opentrack/releases) with a webcam, a phone app that sends the OpenTrack UDP protocol, or any other OpenTrack compatible tracker.
- 64-bit Windows 10 or 11.

## Installation

### Lopari

Download [Lopari](https://lopari.app), choose **Superliminal**, and click
**Play with head tracking**.

### Standalone Installer

1. Download the installer ZIP from the [Releases page](https://github.com/itsloopyo/superliminal-headtracking/releases).
2. Extract it anywhere.
3. Double-click `install.cmd`. It finds the installed copies and selects BepInEx 5 for Steam or BepInEx 6 for Xbox Game Pass, then installs the matching mod files.
4. Configure OpenTrack to output UDP to `127.0.0.1:4242`.
5. Launch the game.

If the installer cannot find your game, point it at the folder yourself, either by setting the environment variable:

```powershell
$env:SUPERLIMINAL_PATH = "D:\Games\Superliminal"
.\install.cmd
```

or by passing the path as an argument:

```powershell
.\install.cmd "D:\Games\Superliminal"
```

The folder holds `SuperliminalSteam.exe` on Steam or `Superliminal.exe` on Xbox Game Pass.

On Xbox Game Pass, the first launch generates support assemblies and may download Unity support libraries. Allow it to finish, then use the game's normal sign-in prompt.

### Manual Installation

Use the installer ZIP for manual installation too. Select the files for your copy:

| Copy | Loader archive inside the ZIP | Mod files |
| --- | --- | --- |
| Steam | `vendor/bepinex/BepInEx_win_x64.zip` | The three DLLs in `plugins/` |
| Xbox Game Pass | `vendor/bepinex-il2cpp/BepInEx_UnityIL2CPP_x64.zip` | The two DLLs in `plugins-il2cpp/` |

Extract the selected loader archive next to the game executable. Copy the selected mod DLLs into `BepInEx/plugins/`. Keep the two builds separate.

The Nexus ZIP contains the Steam plugin only and requires BepInEx 5. Xbox Game Pass users need the installer ZIP.

## Setting Up OpenTrack

In OpenTrack, set **Output** to `UDP over network`, then open its options and set the address to `127.0.0.1` and the port to `4242`. Pick an **Input** to match your hardware, start tracking, and center with OpenTrack's Center bind while you are sitting how you play.

Centering is done in your tracker, not in the game: OpenTrack's Center bind, SteamVR's reset, or the CENTER button in your phone app.

### VR Headset Setup

1. Connect the headset to your PC however it normally connects - a link cable, Air Link or Virtual Desktop on a Quest, DisplayPort on a tethered headset.
2. Start SteamVR and wait for the headset to report as tracking.
3. In OpenTrack, set **Input** to the SteamVR tracker.
4. Leave **Output** on `UDP over network`, `127.0.0.1`, port `4242`.

### Webcam Setup

In OpenTrack, set **Input** to `neuralnet tracker`. It tracks your face from an ordinary webcam, with no markers, clip or IR hardware. Set **Output** to `UDP over network` on `127.0.0.1:4242`.

### Phone App Setup

The mod accepts one thing: the OpenTrack UDP protocol on port `4242`. A phone tracker works here if it sends that protocol itself, or ships a PC-side companion that does. Plenty of phone trackers speak something else, so check your app against that first.

For an app that does send it, what decides the wiring is how much filtering the app does on the phone before the packet leaves it. An app that filters on-device can point straight at your PC's LAN address on port `4242`. A raw or lightly filtered feed sent direct will jitter, because the mod's smoothing is sized to take the edge off a clean signal rather than to rescue a noisy one, and that app should go through OpenTrack instead, using its `UDP over network` input so its filters and curves clean the feed up first. The test is quick: try sending direct, hold your head still, and if the view drifts or shakes, route it through OpenTrack.

I made [Headcam](https://headcam.app) so decent tracking was free for anybody with a phone already in their pocket. It filters on-device, so it can send directly. Any app that filters enough noise works the same way.

A phone on WiFi is a remote connection and gets `RemoteSmoothing`. So does a tracker running on this same PC that sends to your LAN address instead of `127.0.0.1`, because the mod classifies the transport rather than the machine. Send to `127.0.0.1` if you want `LocalSmoothing`.

## Controls

Two equivalent binding sets, use whichever your keyboard has:

| Action              | Nav-cluster | Chord           |
|---------------------|-------------|-----------------|
| Toggle tracking     | `End`       | `Ctrl+Shift+Y`  |
| Cycle tracking mode | `Page Up`   | `Ctrl+Shift+G`  |
| Toggle yaw mode     | `Page Down` | `Ctrl+Shift+H`  |

Cycling tracking mode steps through full tracking, rotation only, position only, and back to full. Toggling yaw mode switches head yaw between world-locked (the default, horizon-stable) and camera-local.

The tracking mode and the yaw mode you pick are saved to `CameraUnlock.ini` and are what the next start begins with. `End` turns head tracking on and off for this session only; whether it is on at the next start is the `EnableOnStartup` setting.

These are the default keys. Each action reads a list of keys from `CameraUnlock.ini` (`ToggleKey`, `CycleTrackingModeKey`, `YawModeKey`), and any key in the list fires it, so you can add, rebind or remove any of them, the chords included.

The game's crosshair follows your aim while head tracking moves the view. It has no setting.

## Configuration

<!-- cameraunlock:config -->
The mod reads its settings from `BepInEx\config\CameraUnlock.ini` in the game folder, and creates the file when it starts and finds none. Edit it with any text editor.

A setting set to `default` takes its value from `Defaults.ini`, which every head tracking mod that keeps its settings in `CameraUnlock.ini` reads. Head tracking mods that keep their settings in another file do not read it, and neither do earlier versions of this mod. Writing a value in place of `default` changes that setting for this game only. When the mod saves a setting that a hotkey changed in game, it writes the new value in place of `default`, so that setting no longer follows `Defaults.ini` in this game until you set it to `default` again.

`Defaults.ini` is `%AppData%\CameraUnlock\Defaults.ini` on Windows; `$XDG_CONFIG_HOME/CameraUnlock/Defaults.ini` on Linux, or `~/.config/CameraUnlock/Defaults.ini` where `XDG_CONFIG_HOME` is not set, under Wine and Proton too; and `~/Library/Application Support/CameraUnlock/Defaults.ini` on macOS. The mod's log, where it writes one, names the file it read.

When the mod starts and finds no `Defaults.ini`, it creates one holding the built-in values, unless Windows runs the game as a packaged app, or the game runs on Linux or macOS without Wine or Proton. The mod never changes `Defaults.ini` after that. Edit it with any text editor.

Earlier versions of the mod kept these settings in `com.cameraunlock.superliminal.headtracking.cfg`, in the same folder. The first time this version starts and finds no `CameraUnlock.ini`, it reads your settings from `com.cameraunlock.superliminal.headtracking.cfg` and writes them into `CameraUnlock.ini`. It never changes `com.cameraunlock.superliminal.headtracking.cfg`, and does not read it again while `CameraUnlock.ini` exists.

A setting that the defaults below set to `default` is written as `default` when the value imported for it equals its default at that start, which is the value `Defaults.ini` gives it, or the built-in value where `Defaults.ini` gives none. It then follows `Defaults.ini`. Every other setting is written with the value imported for it. `RotationEnabled` and `PositionEnabled` are one setting here, the tracking mode, so both are written as `default` or neither is.

Comments, and keys the mod never read, are not carried over. Nor are these, where your old file had them:

- Reticle settings, and a key that toggled the reticle.
- A sensitivity, scale, deadzone, response curve or axis inversion you changed from its default. Set these in your tracker instead.
- The setting for a feature that earlier versions shipped switched off while it was untested. It now follows the mod's default.

An older version of the mod reads `com.cameraunlock.superliminal.headtracking.cfg` and never reads `CameraUnlock.ini`, so a setting you change after updating is not in `com.cameraunlock.superliminal.headtracking.cfg`.

Deleting only `CameraUnlock.ini` makes the next start read `com.cameraunlock.superliminal.headtracking.cfg` again. To go back to the defaults, replace everything in `CameraUnlock.ini` with the defaults below. Every setting they set to `default` then follows `Defaults.ini`.

On Linux and macOS without Wine or Proton, this version reads its settings and saves none: it creates no `CameraUnlock.ini`, reads your settings from `com.cameraunlock.superliminal.headtracking.cfg` again at every start while there is no `CameraUnlock.ini`, and a change made in game lasts until the game closes.

BepInEx's ConfigurationManager no longer lists these settings.

The built-in value of each setting set to `default` below:

- `UdpPort=4242`
- `EnableOnStartup=true`
- `WorldSpaceYaw=true`
- `RotationEnabled=true`
- `LocalSmoothing=0.0`
- `RemoteSmoothing=0.15`
- `PositionEnabled=true`
- `PositionLimitX=0.3`
- `PositionLimitY=0.2`
- `PositionLimitYDown=0.2`
- `PositionLimitZ=0.4`
- `PositionLimitZBack=0.1`
- `CollisionEnabled=true`
- `CollisionReleaseSmoothing=0.9`
- `TrackerPivotForward=0.0`
- `ToggleKey=End, Ctrl+Shift+Y`
- `CycleTrackingModeKey=PageUp, Ctrl+Shift+G`
- `YawModeKey=PageDown, Ctrl+Shift+H`

With every setting at its default, the file reads:

```ini
; Superliminal head tracking settings.
; Comments start with ; and go on their own line. Text after a value is part of the value.
; Hotkeys are key names such as End, PageUp or Ctrl+Shift+Y. Separate several with commas; leave empty for none.
; A setting set to default takes its value from Defaults.ini, which every head tracking mod
; that keeps its settings in CameraUnlock.ini reads: %AppData%\CameraUnlock\Defaults.ini on
; Windows, $XDG_CONFIG_HOME/CameraUnlock/Defaults.ini (normally ~/.config/CameraUnlock) on
; Linux, under Wine and Proton too, and ~/Library/Application Support/CameraUnlock/Defaults.ini
; on macOS. The log names the file it read. Write a value instead of default to change that
; setting for this game only.

[CameraUnlock]
; Written by the mod. Leave this section in place.
ConfigFormat=1

[Network]
; UDP port the mod receives tracker data on (OpenTrack protocol).
UdpPort=default

[General]
; true: head tracking is on when the game starts. ToggleKey turns it on and off.
EnableOnStartup=default
; true: yaw turns around the world's up axis. false: around the camera's own up axis.
WorldSpaceYaw=default
; true: turning your head turns the view.
; Tracking mode at startup, with PositionEnabled. The mode hotkey changes both.
RotationEnabled=default

[Smoothing]
; Smoothing when the tracker runs on this PC. 0 is the least, 1 the most.
LocalSmoothing=default
; Smoothing when the tracker is another device on the network, such as a phone.
; 0 is the least, 1 the most.
RemoteSmoothing=default

[Position]
; true: moving your head moves the view.
; Tracking mode at startup, with RotationEnabled. The mode hotkey changes both.
PositionEnabled=default
; How far, in metres, leaning left or right can move the view.
PositionLimitX=default
; How far, in metres, raising your head can move the view.
PositionLimitY=default
; How far, in metres, lowering your head can move the view.
PositionLimitYDown=default
; How far, in metres, leaning forward can move the view.
PositionLimitZ=default
; How far, in metres, leaning back can move the view.
PositionLimitZBack=default
; true: leaning stops at walls instead of moving the view through them.
CollisionEnabled=default
; How far, in metres, the view is held off a wall when you lean into it.
; The mod holds it at least 1.5 times the camera's near clip distance.
CollisionMargin=0.12
; How gently the view eases back out after a wall stopped a lean.
; 0 is the quickest, 1 the slowest.
CollisionReleaseSmoothing=default
; Metres from the pivot of your neck forward to the point the tracker follows.
; Used to remove the lean that turning your head adds. 0 turns it off.
TrackerPivotForward=default

[Hotkeys]
; Turns head tracking on and off.
ToggleKey=default
; Changes the tracking mode: rotation and position, rotation only, position only.
CycleTrackingModeKey=default
; Switches yaw between the world's up axis and the camera's own (WorldSpaceYaw).
YawModeKey=default

[Notifications]
; true: show whether head tracking is on, and its hotkeys, when the game starts.
ShowStartupNotification=true
; true: show a message when tracker data starts or stops arriving.
ShowConnectionNotifications=true

[Diagnostics]
; true: log the applied pose, lean, aim distance and crosshair position once a second.
LogAimGeometry=false
```
<!-- /cameraunlock:config -->

## Troubleshooting

**Mod not loading:**

- Check that `winhttp.dll` and `doorstop_config.ini` sit next to the game executable and that the matching mod DLLs are in `BepInEx/plugins/`.
- Use BepInEx 5 for Steam and the bundled BepInEx 6 IL2CPP build for Xbox Game Pass. Both must be x64.
- Open `BepInEx/LogOutput.log` and look for the `SuperliminalHeadTracking` startup line. If the mod stayed dormant it says so and why.

**No tracking response:**

- Confirm OpenTrack is started and its output is `UDP over network` on `127.0.0.1:4242`, matching `UdpPort` in `CameraUnlock.ini`.
- Tracking runs during free-look gameplay only, so menus, loading, level select and scripted camera moves are left alone, as is the whole of multiplayer from the moment the game connects.
- If your tracker is on another device, allow the game through Windows Firewall on the private network.

**Jittery or unstable tracking:**

- Raise `RemoteSmoothing` for a phone or network tracker, or `LocalSmoothing` for a tracker on this PC.
- If the feed comes straight from a phone app, route it through OpenTrack instead so its filters can clean it up first.
- Poor lighting starves a webcam tracker. Light your face from the front, not from behind.

**Config changes do not apply:**

- Close the game, edit `BepInEx\config\CameraUnlock.ini`, then relaunch. Editing the old `.cfg` changes nothing once `CameraUnlock.ini` exists.
- Make sure nothing follows the value on the line: text after a value is part of the value. `BepInEx/LogOutput.log` names each line the mod could not read and the value it used instead.

**Wrong rotation axis, or yaw feels wrong looking up and down:**

- Press `Page Down` to switch between world-locked and camera-local yaw. World-locked is horizon-stable, camera-local follows the camera's current up-axis.
- Axis directions and response curves belong to your tracker. Fix an inverted axis in OpenTrack or your phone app, so one profile behaves the same in every game.

**The game window moved on its own:**

- Running windowed, the mod centers the window on the work area of the monitor it is already on, once at startup and again whenever the resolution or the fullscreen setting changes. A window that is already centered, and one that fills the work area, are left alone. Drag it where you like and it stays there until you next change the resolution.

## Updating

Download the new release and run `install.cmd` again. Your `CameraUnlock.ini` is preserved.

## Uninstalling

Run `uninstall.cmd`. This removes the mod DLLs and leaves `CameraUnlock.ini` and the old `.cfg` in place. BepInEx is only removed if the installer put it there. Use `uninstall.cmd /force` to remove it anyway.

## Building from Source

Prerequisites: [pixi](https://pixi.sh) and the .NET SDK. Both builds use repository stubs and published reference assemblies, with no game installation required.

```powershell
git clone --recurse-submodules https://github.com/itsloopyo/superliminal-headtracking.git
cd superliminal-headtracking
pixi run build
pixi run package
```

`pixi run package` writes the installer and Nexus ZIPs to `release/`. `pixi run install` deploys a local build straight into the game.

## Community & Support

- [Discord](https://discord.com/invite/dxyZdyFNT9) - setup help, bug reports, and new-release announcements
- [Lopari](https://lopari.app) - free Windows launcher with one-click install and launch of head-tracking mods
- [Headcam](https://headcam.app) - free app that turns your phone into a head tracker

## License

MIT License - see [LICENSE](LICENSE) for details.

## Credits

- Pillow Castle, developer and publisher of Superliminal.
- [BepInEx](https://github.com/BepInEx/BepInEx), the Unity mod loader this plugin runs on, bundled with the installer.
- [HarmonyX](https://github.com/BepInEx/HarmonyX), used to patch the game's camera update.
- [OpenTrack](https://github.com/opentrack/opentrack), whose UDP protocol this mod speaks.

## Disclaimer

This mod is not affiliated with, endorsed by, or supported by Pillow Castle. Use at your own risk.
