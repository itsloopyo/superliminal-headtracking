# Changelog

## [Unreleased]

### Changed

- Settings move to `BepInEx\config\CameraUnlock.ini`. Earlier versions of the mod kept these settings in `com.cameraunlock.superliminal.headtracking.cfg`, in the same folder. The first time this version starts and finds no `CameraUnlock.ini`, it reads your settings from `com.cameraunlock.superliminal.headtracking.cfg` and writes them into `CameraUnlock.ini`. It never changes `com.cameraunlock.superliminal.headtracking.cfg`, and does not read it again while `CameraUnlock.ini` exists.
- A setting that the defaults the README shows set to `default` is written as `default` when the value imported for it equals its default at that start, which is the value `Defaults.ini` gives it, or the built-in value where `Defaults.ini` gives none. It then follows `Defaults.ini`. Every other setting is written with the value imported for it.
- `RotationEnabled` and `PositionEnabled` are one setting here, the tracking mode, so both are written as `default` or neither is.
- Comments, and keys the mod never read, are not carried over. Nor are these, where your old file had them:
  - A sensitivity, scale, deadzone, response curve or axis inversion you changed from its default. Set these in your tracker instead.
  - Reticle settings, and a key that toggled the reticle.
  - The setting for a feature that earlier versions shipped switched off while it was untested. It now follows the mod's default.
- An older version of the mod reads `com.cameraunlock.superliminal.headtracking.cfg` and never reads `CameraUnlock.ini`, so a setting you change after updating is not in `com.cameraunlock.superliminal.headtracking.cfg`.
- Deleting only `CameraUnlock.ini` makes the next start read `com.cameraunlock.superliminal.headtracking.cfg` again. To go back to the defaults, replace everything in `CameraUnlock.ini` with the defaults the README shows. Every setting they set to `default` then follows `Defaults.ini`.
- BepInEx's ConfigurationManager no longer lists these settings. Edit `BepInEx\config\CameraUnlock.ini` with any text editor.
- Hotkeys are written as key names, and each hotkey lists every key that triggers it, the Ctrl+Shift chord included: `ToggleKey=End, Ctrl+Shift+Y`.
- A hotkey bound to a plain key no longer fires while Ctrl and Shift are both held, so Ctrl+Shift with that key reaches only a binding that names the chord.
- On Linux and macOS without Wine or Proton, this version reads its settings and saves none: it creates no `CameraUnlock.ini`, reads your settings from `com.cameraunlock.superliminal.headtracking.cfg` again at every start while there is no `CameraUnlock.ini`, and a change made in game lasts until the game closes.
- The tracking mode (`Page Up`) and the yaw mode (`Page Down`) you pick are saved to `CameraUnlock.ini` and are what the next start begins with. Earlier versions started every session from the file's settings. `End` still changes the current session only.
- Several settings have new names or places in `CameraUnlock.ini`: `EnabledOnStartup` is `EnableOnStartup`, `UDPPort` is `UdpPort`, the `[Keybindings]` keys are under `[Hotkeys]`, the `[Collision]` keys under `[Position]`, the notification switches under `[Notifications]`, and `PositionLimitY` sets the upward limit only, with `PositionLimitYDown` for the downward one. The import carries each value to its new place, and `PositionLimitY` to both limits, as earlier versions applied it.
- Settings are read when the game starts. Earlier versions applied some changes made through ConfigurationManager while the game ran; a change to `CameraUnlock.ini` applies at the next start.

### Added

- A setting set to `default` in `CameraUnlock.ini` takes its value from `Defaults.ini`, which every head tracking mod that keeps its settings in `CameraUnlock.ini` reads. Head tracking mods that keep their settings in another file do not read it, and neither do earlier versions of this mod. Writing a value in place of `default` changes that setting for this game only. When the mod saves a setting that a hotkey changed in game, it writes the new value in place of `default`, so that setting no longer follows `Defaults.ini` in this game until you set it to `default` again.
- `Defaults.ini` is `%AppData%\CameraUnlock\Defaults.ini` on Windows; `$XDG_CONFIG_HOME/CameraUnlock/Defaults.ini` on Linux, or `~/.config/CameraUnlock/Defaults.ini` where `XDG_CONFIG_HOME` is not set, under Wine and Proton too; and `~/Library/Application Support/CameraUnlock/Defaults.ini` on macOS. The mod's log, where it writes one, names the file it read.
- When the mod starts and finds no `Defaults.ini`, it creates one holding the built-in values, unless Windows runs the game as a packaged app, or the game runs on Linux or macOS without Wine or Proton. The mod never changes `Defaults.ini` after that.

### Removed

- `MoveCrosshair`. The game's crosshair always follows the aim while head tracking moves the view; an imported `MoveCrosshair=false` is dropped and logged.
- The sensitivity, scale, deadzone, response curve and axis inversion settings (`YawSensitivity`, `PitchSensitivity`, `RollSensitivity`, `PositionSensitivityX`, `PositionSensitivityY`, `PositionSensitivityZ`). Set these in your tracker app instead.
- With these settings at their shipped defaults the camera moves as it did before.

## [0.2.0] - 2026-09-13

### Other

- Add Xbox Game Pass support via a BepInEx 6 IL2CPP build

## [0.1.0] - 2026-09-13

### Added
- Initial release.
- Head tracking driven by any OpenTrack compatible tracker over UDP port 4242, with rotation and 6DOF position applied to the view only, so the cursor and game logic keep the clean camera.
- Tracking toggle on `End` / `Ctrl+Shift+Y`, a three-state mode cycle on `Page Up` / `Ctrl+Shift+G`, and a world-locked / camera-local yaw toggle on `Page Down` / `Ctrl+Shift+H`, all rebindable in the `Keybindings` config section.
- Field of view compensation that scales head yaw, pitch and lean to the field of view the game is rendering, so the dolly-zoom rooms move the view by the same amount as ordinary play.
- Crosshair follows the aim point, so the cursor stays where interaction lands while the head moves the view.
- Outlines on selected and held objects follow the object, rather than staying where it sat before the head moved.
- Lean clamped against level geometry, so leaning into a surface does not put the view through it.
- Window centring on the current monitor's work area when the game runs windowed, applied at startup and whenever the resolution or fullscreen setting changes.
