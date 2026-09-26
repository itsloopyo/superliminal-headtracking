using System.IO;
using BepInEx.Configuration;

namespace SuperliminalHeadTracking.Legacy
{
    /// <summary>
    /// The plugin's BepInEx Bind calls as v0.2.0 ran them, each definition's section, key, type,
    /// description and acceptable values unchanged and its default taken from
    /// <see cref="LegacyConfig"/>. Frozen for the life of the repo: it is how a player's .cfg is
    /// read, whichever earlier build wrote it.
    /// <para>
    /// It writes nothing. BepInEx's ConfigFile read the .cfg in its constructor, before whoever
    /// calls this held the file, so saving on set is turned off first and the file is read again
    /// before anything is bound. A missing file reads as the defaults.
    /// </para>
    /// </summary>
    internal static class LegacyConfigReader
    {
        /// <summary>Reads <paramref name="config"/>'s file into a new <see cref="LegacyConfig"/>.</summary>
        /// <param name="found">Whether the file existed.</param>
        public static LegacyConfig Read(ConfigFile config, out bool found)
        {
            config.SaveOnConfigSet = false;
            found = File.Exists(config.ConfigFilePath);
            if (found)
            {
                config.Reload();
            }

            var read = new LegacyConfig();

            read.EnabledOnStartup = config.Bind(
                "General", "EnabledOnStartup", read.EnabledOnStartup,
                "Whether head tracking is enabled when the game starts").Value;

            read.ShowStartupNotification = config.Bind(
                "General", "ShowStartupNotification", read.ShowStartupNotification,
                "Whether to show a notification when the plugin initializes").Value;

            read.WorldSpaceYaw = config.Bind(
                "General", "WorldSpaceYaw", read.WorldSpaceYaw,
                "Yaw mode: true = horizon-locked yaw (default), false = camera-local").Value;

            read.ShowConnectionNotifications = config.Bind(
                "UI", "ShowConnectionNotifications", read.ShowConnectionNotifications,
                "Whether to show notifications when the OpenTrack connection is lost or restored").Value;

            read.MoveCrosshair = config.Bind(
                "UI", "MoveCrosshair", read.MoveCrosshair,
                "Keep the game's crosshair on the surface the grab ray points at while "
                + "the head moves the view. Superliminal draws the crosshair fixed at "
                + "screen centre, which marks the grab point only while the view IS the "
                + "aim; turning this off leaves it there.").Value;

            read.ToggleKey = config.Bind(
                "Keybindings", "ToggleKey", read.ToggleKey,
                "Key to toggle head tracking on/off").Value;

            read.CycleTrackingModeKey = config.Bind(
                "Keybindings", "CycleTrackingModeKey", read.CycleTrackingModeKey,
                "Key to cycle tracking mode (full -> rotation only -> position only -> full)").Value;

            read.YawModeKey = config.Bind(
                "Keybindings", "YawModeKey", read.YawModeKey,
                "Key to toggle world-locked vs camera-local yaw").Value;

            read.UDPPort = config.Bind(
                "Network", "UDPPort", read.UDPPort,
                new ConfigDescription(
                    "UDP port to listen for OpenTrack data",
                    new AcceptableValueRange<int>(1024, 65535))).Value;

            read.YawSensitivity = config.Bind(
                "Sensitivity", "YawSensitivity", read.YawSensitivity,
                new ConfigDescription(
                    "Multiplier for horizontal head rotation (left/right)",
                    new AcceptableValueRange<float>(0.1f, 3.0f))).Value;

            read.PitchSensitivity = config.Bind(
                "Sensitivity", "PitchSensitivity", read.PitchSensitivity,
                new ConfigDescription(
                    "Multiplier for vertical head rotation (up/down)",
                    new AcceptableValueRange<float>(0.1f, 3.0f))).Value;

            read.RollSensitivity = config.Bind(
                "Sensitivity", "RollSensitivity", read.RollSensitivity,
                new ConfigDescription(
                    "Multiplier for head tilt (ear to shoulder)",
                    new AcceptableValueRange<float>(0.1f, 3.0f))).Value;

            read.LocalSmoothing = config.Bind(
                "Smoothing", "LocalSmoothing", read.LocalSmoothing,
                new ConfigDescription(
                    "Smoothing applied when the tracker runs on this machine (loopback). 0 = none, 1 = heavy.",
                    new AcceptableValueRange<float>(0f, 1f))).Value;

            read.RemoteSmoothing = config.Bind(
                "Smoothing", "RemoteSmoothing", read.RemoteSmoothing,
                new ConfigDescription(
                    "Smoothing applied when the tracker is a remote device on the network. 0 = none, 1 = heavy.",
                    new AcceptableValueRange<float>(0f, 1f))).Value;

            read.PositionEnabled = config.Bind(
                "Position", "PositionEnabled", read.PositionEnabled,
                "Enable positional tracking (lean in/out/side-to-side)").Value;

            read.PositionSensitivityX = config.Bind(
                "Position", "PositionSensitivityX", read.PositionSensitivityX,
                new ConfigDescription(
                    "Multiplier for lateral (left/right) position",
                    new AcceptableValueRange<float>(0f, 5.0f))).Value;

            read.PositionSensitivityY = config.Bind(
                "Position", "PositionSensitivityY", read.PositionSensitivityY,
                new ConfigDescription(
                    "Multiplier for vertical (up/down) position",
                    new AcceptableValueRange<float>(0f, 5.0f))).Value;

            read.PositionSensitivityZ = config.Bind(
                "Position", "PositionSensitivityZ", read.PositionSensitivityZ,
                new ConfigDescription(
                    "Multiplier for depth (forward/back) position",
                    new AcceptableValueRange<float>(0f, 5.0f))).Value;

            read.PositionLimitX = config.Bind(
                "Position", "PositionLimitX", read.PositionLimitX,
                new ConfigDescription(
                    "Maximum lateral displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

            read.PositionLimitY = config.Bind(
                "Position", "PositionLimitY", read.PositionLimitY,
                new ConfigDescription(
                    "Maximum vertical displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

            read.PositionLimitZ = config.Bind(
                "Position", "PositionLimitZ", read.PositionLimitZ,
                new ConfigDescription(
                    "Maximum forward displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

            read.PositionLimitZBack = config.Bind(
                "Position", "PositionLimitZBack", read.PositionLimitZBack,
                new ConfigDescription(
                    "Maximum backward displacement in meters",
                    new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

            read.TrackerPivotForward = config.Bind(
                "Position", "TrackerPivotForward", read.TrackerPivotForward,
                new ConfigDescription(
                    "Distance in meters from the neck pivot to the point your tracker "
                    + "watches, which subtracts the arc a head rotation puts into the "
                    + "position data. OFF by default, and it belongs off unless you "
                    + "have measured it for the tracker you use: the arm length is a "
                    + "property of the tracker app, not of this mod. Some apps already "
                    + "apply their own eye anchor, and for those a non-zero value here "
                    + "subtracts an arc that was never added, so a pure head turn "
                    + "produces a lean that is not there.",
                    new AcceptableValueRange<float>(0f, 0.20f))).Value;

            read.CollisionEnabled = config.Bind(
                "Collision", "CollisionEnabled", read.CollisionEnabled,
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
                + "ones, which is the right way round.").Value;

            read.CollisionMargin = config.Bind(
                "Collision", "CollisionMargin", read.CollisionMargin,
                new ConfigDescription(
                    "Distance in meters the eye is held off a surface. Raised at "
                    + "runtime if it does not clear the camera's near clip plane, "
                    + "since anything closer than that is culled and the player sees "
                    + "through the wall anyway.",
                    new AcceptableValueRange<float>(0.02f, 0.6f))).Value;

            read.CollisionReleaseSmoothing = config.Bind(
                "Collision", "CollisionReleaseSmoothing", read.CollisionReleaseSmoothing,
                new ConfigDescription(
                    "Smoothing applied as the lean opens back up after an obstruction "
                    + "clears. 0.9 is a 200ms time constant. Tightening is never smoothed.",
                    new AcceptableValueRange<float>(0f, 1f))).Value;

            read.LogAimGeometry = config.Bind(
                "Diagnostics", "LogAimGeometry", read.LogAimGeometry,
                "Log one line per second carrying the applied pose, the tracked "
                + "camera basis measured against the clean one, the lean, the aim "
                + "distance and the crosshair offset together, so a direction fault "
                + "can be settled by arithmetic rather than by playing.").Value;

            return read;
        }
    }
}
