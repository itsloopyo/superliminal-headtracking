# Superliminal Head Tracking

![Superliminal running with this mod](https://raw.githubusercontent.com/itsloopyo/superliminal-headtracking/main/assets/readme-clip.gif)

An unofficial head tracking mod for Superliminal that moves the view with your head while your mouse or controller keeps aiming, driven by a webcam, phone, or any OpenTrack compatible tracker, with no VR headset required.

## Features

- **Decoupled look and aim** - your head moves the view, your mouse or controller still points the grab ray
- **6DOF positional tracking** - lean, peek and duck with head position
- **Works with any OpenTrack compatible tracker** - free options available for PC, iOS and Android
- **Resizing stays true to the mouse** - an object grows to the same size whether your head is turned or still

## Requirements

- A purchased copy of [Superliminal on Steam](https://store.steampowered.com/app/1049410/Superliminal/).
- A tracking source: [OpenTrack](https://github.com/opentrack/opentrack/releases) with a webcam, a phone app that sends the OpenTrack UDP protocol, or any other OpenTrack compatible tracker.
- 64-bit Windows 10 or 11.

## Installation

### Lopari

Download [Lopari](https://lopari.app), choose **Superliminal**, and click
**Play with head tracking**.

### Standalone Installer

1. Download the installer ZIP from the [Releases page](https://github.com/itsloopyo/superliminal-headtracking/releases).
2. Extract it anywhere.
3. Double-click `install.cmd`. It finds Superliminal, installs BepInEx 5 if the game does not already have it, and copies the mod DLLs into place.
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

The folder is the one holding `SuperliminalSteam.exe`.

### Manual Installation

To place the files by hand:

1. Download [BepInEx 5.4.23.5, x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) (`BepInEx_win_x64_5.4.23.5.zip`) and extract it into the Superliminal folder, so `winhttp.dll`, `doorstop_config.ini` and a `BepInEx` folder sit next to `SuperliminalSteam.exe`.
2. Run the game once and close it, so BepInEx creates its folder structure.
3. Copy `SuperliminalHeadTracking.dll`, `CameraUnlock.Core.dll` and `CameraUnlock.Core.Unity.dll` into `BepInEx/plugins/`.

The Nexus ZIP is already laid out this way: extract it over the game folder and the three DLLs land in `BepInEx/plugins/`. It does not contain BepInEx itself, so step 1 still applies.

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

All three keys are rebindable in the `Keybindings` section of the config file. The chords are always registered alongside them.

## Configuration

Settings live in `BepInEx/config/com.cameraunlock.superliminal.headtracking.cfg`, written the first time the game runs with the mod installed. Edit it with the game closed.

```ini
[General]
## Head tracking is active as soon as the game starts
EnabledOnStartup = true
ShowStartupNotification = true
## Yaw mode: true = horizon-locked yaw, false = camera-local
WorldSpaceYaw = true

[UI]
ShowConnectionNotifications = true
## Keep the game's crosshair on the surface the grab ray points at while the
## head moves the view. Superliminal draws it fixed at screen center, which
## marks the grab point only while the view IS the aim
MoveCrosshair = true

[Keybindings]
ToggleKey = End
CycleTrackingModeKey = PageUp
YawModeKey = PageDown

[Network]
## OpenTrack UDP port
UDPPort = 4242

[Sensitivity]
YawSensitivity = 1
PitchSensitivity = 1
RollSensitivity = 1

[Smoothing]
## Applied to a tracker on this machine sending to 127.0.0.1. 0 = none, 1 = heavy
LocalSmoothing = 0
## Applied to a tracker reaching the game over the network, a phone on WiFi included
RemoteSmoothing = 0.15

[Position]
## Positional lean, peek and duck
PositionEnabled = true
PositionSensitivityX = 1
PositionSensitivityY = 1
PositionSensitivityZ = 1
## Travel limits in meters. Z is asymmetric: more room to lean in than back
PositionLimitX = 0.3
PositionLimitY = 0.2
PositionLimitZ = 0.4
PositionLimitZBack = 0.1
## Distance from your neck pivot to the point your tracker watches. Leave at 0
## unless you have measured it: several apps already apply their own eye anchor
TrackerPivotForward = 0

[Collision]
## Cut a lean back to whatever the level leaves room for, so the view never
## ends up inside a wall. No effect in rotation-only mode
CollisionEnabled = true
## Distance in meters the eye is held off a surface
CollisionMargin = 0.12
## How fast the lean opens back up once an obstruction clears. 0.9 is 200ms
CollisionReleaseSmoothing = 0.9

[Diagnostics]
## One line per second carrying the applied pose, lean, aim distance and
## crosshair offset. Off unless you are chasing a direction fault
LogAimGeometry = false
```

A missing entry falls back to its default, so a config file written by an older version keeps working.

## Troubleshooting

**Mod not loading:**

- Check that `winhttp.dll` and `doorstop_config.ini` sit next to `SuperliminalSteam.exe`, and that the three mod DLLs are in `BepInEx/plugins/`.
- Make sure you installed the x64 build of BepInEx 5. The x86 build will not load, and BepInEx 6 has a different layout this mod does not expect.
- Open `BepInEx/LogOutput.log` and look for the `SuperliminalHeadTracking` startup line. If the mod stayed dormant it says so and why.

**No tracking response:**

- Confirm OpenTrack is started and its output is `UDP over network` on `127.0.0.1:4242`, matching `UDPPort` in the config.
- Tracking runs during free-look gameplay only, so menus, loading, level select and scripted camera moves are left alone, as is the whole of multiplayer from the moment the game connects.
- If your tracker is on another device, allow the game through Windows Firewall on the private network.

**Jittery or unstable tracking:**

- Raise `RemoteSmoothing` for a phone or network tracker, or `LocalSmoothing` for a tracker on this PC.
- If the feed comes straight from a phone app, route it through OpenTrack instead so its filters can clean it up first.
- Poor lighting starves a webcam tracker. Light your face from the front, not from behind.

**Wrong rotation axis, or yaw feels wrong looking up and down:**

- Press `Page Down` to switch between world-locked and camera-local yaw. World-locked is horizon-stable, camera-local follows the camera's current up-axis.
- Axis directions and response curves belong to your tracker. Fix an inverted axis in OpenTrack or your phone app, so one profile behaves the same in every game.

**The game window moved on its own:**

- Running windowed, the mod centers the window on the work area of the monitor it is already on, once at startup and again whenever the resolution or the fullscreen setting changes. A window that is already centered, and one that fills the work area, are left alone. Drag it where you like and it stays there until you next change the resolution.

## Updating

Download the new release and run `install.cmd` again. Your config is preserved.

## Uninstalling

Run `uninstall.cmd`. This removes the mod DLLs. BepInEx is only removed if the installer put it there. Use `uninstall.cmd /force` to remove it anyway.

## Building from Source

Prerequisites: [pixi](https://pixi.sh) and the .NET SDK. The build needs no game install, it compiles against reference stubs.

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
