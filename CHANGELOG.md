# Changelog

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
