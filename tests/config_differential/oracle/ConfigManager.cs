using BepInEx.Configuration;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Math;
using UnityEngine;

namespace SuperliminalHeadTracking.Config
{
    public class ConfigManager
    {
        // General
        public ConfigEntry<bool> EnabledOnStartup { get; private set; }
        public ConfigEntry<bool> ShowStartupNotification { get; private set; }
        public ConfigEntry<bool> WorldSpaceYaw { get; private set; }

        // UI
        public ConfigEntry<bool> ShowConnectionNotifications { get; private set; }
        public ConfigEntry<bool> MoveCrosshair { get; private set; }

        // Keybindings
        public ConfigEntry<KeyCode> ToggleKey { get; private set; }
        public ConfigEntry<KeyCode> CycleTrackingModeKey { get; private set; }
        public ConfigEntry<KeyCode> YawModeKey { get; private set; }

        // Network
        public ConfigEntry<int> UDPPort { get; private set; }

        // Sensitivity
        public ConfigEntry<float> YawSensitivity { get; private set; }
        public ConfigEntry<float> PitchSensitivity { get; private set; }
        public ConfigEntry<float> RollSensitivity { get; private set; }

        // Smoothing
        public ConfigEntry<float> LocalSmoothing { get; private set; }
        public ConfigEntry<float> RemoteSmoothing { get; private set; }

        // Position
        public ConfigEntry<bool> PositionEnabled { get; private set; }
        public ConfigEntry<float> PositionSensitivityX { get; private set; }
        public ConfigEntry<float> PositionSensitivityY { get; private set; }
        public ConfigEntry<float> PositionSensitivityZ { get; private set; }
        public ConfigEntry<float> PositionLimitX { get; private set; }
        public ConfigEntry<float> PositionLimitY { get; private set; }
        public ConfigEntry<float> PositionLimitZ { get; private set; }
        public ConfigEntry<float> PositionLimitZBack { get; private set; }
        public ConfigEntry<float> TrackerPivotForward { get; private set; }

        // Camera collision
        public ConfigEntry<bool> CollisionEnabled { get; private set; }
        public ConfigEntry<float> CollisionMargin { get; private set; }
        public ConfigEntry<float> CollisionReleaseSmoothing { get; private set; }

        // Diagnostics
        public ConfigEntry<bool> LogAimGeometry { get; private set; }

        public void Initialize(ConfigFile config)
        {
            EnabledOnStartup = config.Bind(
                "General", "EnabledOnStartup", true,
                "Whether head tracking is enabled when the game starts");

            ShowStartupNotification = config.Bind(
                "General", "ShowStartupNotification", true,
                "Whether to show a notification when the plugin initializes");

            WorldSpaceYaw = config.Bind(
                "General", "WorldSpaceYaw", true,
                "Yaw mode: true = horizon-locked yaw (default), false = camera-local");

            ShowConnectionNotifications = config.Bind(
                "UI", "ShowConnectionNotifications", true,
                "Whether to show notifications when the OpenTrack connection is lost or restored");

            MoveCrosshair = config.Bind(
                "UI", "MoveCrosshair", true,
                "Keep the game's crosshair on the surface the grab ray points at while "
                + "the head moves the view. Superliminal draws the crosshair fixed at "
                + "screen centre, which marks the grab point only while the view IS the "
                + "aim; turning this off leaves it there.");

            ToggleKey = config.Bind(
                "Keybindings", "ToggleKey", KeyCode.End,
                "Key to toggle head tracking on/off");

            CycleTrackingModeKey = config.Bind(
                "Keybindings", "CycleTrackingModeKey", KeyCode.PageUp,
                "Key to cycle tracking mode (full -> rotation only -> position only -> full)");

            YawModeKey = config.Bind(
                "Keybindings", "YawModeKey", KeyCode.PageDown,
                "Key to toggle world-locked vs camera-local yaw");

            UDPPort = config.Bind(
                "Network", "UDPPort", 4242,
                new ConfigDescription(
                    "UDP port to listen for OpenTrack data",
                    new AcceptableValueRange<int>(1024, 65535)));

            YawSensitivity = config.Bind(
                "Sensitivity", "YawSensitivity", 1.0f,
                new ConfigDescription(
                    "Multiplier for horizontal head rotation (left/right)",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            PitchSensitivity = config.Bind(
                "Sensitivity", "PitchSensitivity", 1.0f,
                new ConfigDescription(
                    "Multiplier for vertical head rotation (up/down)",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            RollSensitivity = config.Bind(
                "Sensitivity", "RollSensitivity", 1.0f,
                new ConfigDescription(
                    "Multiplier for head tilt (ear to shoulder)",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            LocalSmoothing = config.Bind(
                "Smoothing", "LocalSmoothing", SmoothingUtils.DefaultLocalSmoothing,
                new ConfigDescription(
                    "Smoothing applied when the tracker runs on this machine (loopback). 0 = none, 1 = heavy.",
                    new AcceptableValueRange<float>(0f, 1f)));

            RemoteSmoothing = config.Bind(
                "Smoothing", "RemoteSmoothing", SmoothingUtils.DefaultRemoteSmoothing,
                new ConfigDescription(
                    "Smoothing applied when the tracker is a remote device on the network. 0 = none, 1 = heavy.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PositionEnabled = config.Bind(
                "Position", "PositionEnabled", true,
                "Enable positional tracking (lean in/out/side-to-side)");

            PositionSensitivityX = config.Bind(
                "Position", "PositionSensitivityX", 1.0f,
                new ConfigDescription(
                    "Multiplier for lateral (left/right) position",
                    new AcceptableValueRange<float>(0f, 5.0f)));

            PositionSensitivityY = config.Bind(
                "Position", "PositionSensitivityY", 1.0f,
                new ConfigDescription(
                    "Multiplier for vertical (up/down) position",
                    new AcceptableValueRange<float>(0f, 5.0f)));

            PositionSensitivityZ = config.Bind(
                "Position", "PositionSensitivityZ", 1.0f,
                new ConfigDescription(
                    "Multiplier for depth (forward/back) position",
                    new AcceptableValueRange<float>(0f, 5.0f)));

            PositionLimitX = config.Bind(
                "Position", "PositionLimitX", PositionSettings.Default.LimitX,
                new ConfigDescription(
                    "Maximum lateral displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f)));

            PositionLimitY = config.Bind(
                "Position", "PositionLimitY", PositionSettings.Default.LimitY,
                new ConfigDescription(
                    "Maximum vertical displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f)));

            PositionLimitZ = config.Bind(
                "Position", "PositionLimitZ", PositionSettings.Default.LimitZ,
                new ConfigDescription(
                    "Maximum forward displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f)));

            PositionLimitZBack = config.Bind(
                "Position", "PositionLimitZBack", PositionSettings.Default.LimitZBack,
                new ConfigDescription(
                    "Maximum backward displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f)));

            TrackerPivotForward = config.Bind(
                "Position", "TrackerPivotForward", 0.0f,
                new ConfigDescription(
                    "Distance in meters from the neck pivot to the point your tracker "
                    + "watches, which subtracts the arc a head rotation puts into the "
                    + "position data. OFF by default, and it belongs off unless you "
                    + "have measured it for the tracker you use: the arm length is a "
                    + "property of the tracker app, not of this mod. Some apps already "
                    + "apply their own eye anchor, and for those a non-zero value here "
                    + "subtracts an arc that was never added, so a pure head turn "
                    + "produces a lean that is not there.",
                    new AcceptableValueRange<float>(0f, 0.20f)));

            CollisionEnabled = config.Bind(
                "Collision", "CollisionEnabled", true,
                "Cut a lean back to whatever the level leaves room for, so the view "
                + "never ends up inside a wall. No effect in rotation-only mode. The "
                + "sweep uses the same physics mask ResizeScript uses once it is "
                + "holding something, narrowed to the layers Unity's collision matrix "
                + "says the player capsule collides with, so neither the object in "
                + "your hands nor a portal plane is ever what stops the lean. "
                + "Measured in game at this margin: in a test chamber corridor with "
                + "the eye 0.46 m off a wall it cuts a 0.40 m lean to 0.29 m and holds "
                + "the eye there however hard the head leans. In open rooms it does "
                + "nothing, because the character capsule already keeps the eye about "
                + "0.83 m off anything you can walk into and a clamp needs roughly "
                + "0.5 m. So it is quiet in the large spaces and active in the tight "
                + "ones, which is the right way round.");

            CollisionMargin = config.Bind(
                "Collision", "CollisionMargin", 0.12f,
                new ConfigDescription(
                    "Distance in meters the eye is held off a surface. Raised at "
                    + "runtime if it does not clear the camera's near clip plane, "
                    + "since anything closer than that is culled and the player sees "
                    + "through the wall anyway.",
                    new AcceptableValueRange<float>(0.02f, 0.6f)));

            CollisionReleaseSmoothing = config.Bind(
                "Collision", "CollisionReleaseSmoothing", 0.9f,
                new ConfigDescription(
                    "Smoothing applied as the lean opens back up after an obstruction "
                    + "clears. 0.9 is a 200ms time constant. Tightening is never smoothed.",
                    new AcceptableValueRange<float>(0f, 1f)));

            LogAimGeometry = config.Bind(
                "Diagnostics", "LogAimGeometry", false,
                "Log one line per second carrying the applied pose, the tracked "
                + "camera basis measured against the clean one, the lean, the aim "
                + "distance and the crosshair offset together, so a direction fault "
                + "can be settled by arithmetic rather than by playing.");
        }
    }
}
