using CameraUnlock.Core.Config;

namespace SuperliminalHeadTracking.Config
{
    /// <summary>
    /// Everything the mod reads from BepInEx\config\CameraUnlock.ini. Unity-free, so the test
    /// project compiles it and holds the committed file to it.
    /// </summary>
    internal sealed class SuperliminalConfig : HeadTrackingConfigData
    {
        /// <summary>The game's name as data/games.json spells it.</summary>
        public const string DisplayName = "Superliminal";

        public SuperliminalConfig()
        {
            CollisionMargin = 0.12f;
        }

        public bool ShowStartupNotification { get; set; } = true;

        public bool ShowConnectionNotifications { get; set; } = true;

        public bool LogAimGeometry { get; set; }

        public static ConfigTable<SuperliminalConfig> Table()
        {
            return HeadTrackingConfigTable.Create<SuperliminalConfig>(
                    ConfigConcepts.UdpPort,
                    ConfigConcepts.EnableOnStartup,
                    ConfigConcepts.WorldSpaceYaw,
                    ConfigConcepts.RotationEnabled,
                    ConfigConcepts.LocalSmoothing,
                    ConfigConcepts.RemoteSmoothing,
                    ConfigConcepts.PositionEnabled,
                    ConfigConcepts.PositionLimitX,
                    ConfigConcepts.PositionLimitY,
                    ConfigConcepts.PositionLimitYDown,
                    ConfigConcepts.PositionLimitZ,
                    ConfigConcepts.PositionLimitZBack,
                    ConfigConcepts.CollisionEnabled,
                    ConfigConcepts.CollisionMargin,
                    ConfigConcepts.CollisionReleaseSmoothing,
                    ConfigConcepts.TrackerPivotForward,
                    ConfigConcepts.ToggleKey,
                    ConfigConcepts.CycleTrackingModeKey,
                    ConfigConcepts.YawModeKey)
                .Select(ConfigConcepts.WorldSpaceYaw).Writable()
                .Select(ConfigConcepts.RotationEnabled).Writable()
                .Select(ConfigConcepts.PositionEnabled).Writable()
                .Select(ConfigConcepts.CollisionMargin)
                .Comment("How far, in metres, the view is held off a wall when you lean into it.\n" +
                         "The mod holds it at least 1.5 times the camera's near clip distance.")
                .Select(ConfigConcepts.TrackerPivotForward)
                .Comment("Metres from the pivot of your neck forward to the point the tracker follows.\n" +
                         "Used to remove the lean that turning your head adds. 0 turns it off.")
                .Local("Notifications", "ShowStartupNotification", c => c.ShowStartupNotification,
                    (c, v) => c.ShowStartupNotification = v, new BoolCodec(),
                    "true: show whether head tracking is on, and its hotkeys, when the game starts.")
                .Local("Notifications", "ShowConnectionNotifications", c => c.ShowConnectionNotifications,
                    (c, v) => c.ShowConnectionNotifications = v, new BoolCodec(),
                    "true: show a message when tracker data starts or stops arriving.")
                .Local("Diagnostics", "LogAimGeometry", c => c.LogAimGeometry, (c, v) => c.LogAimGeometry = v,
                    new BoolCodec(),
                    "true: log the applied pose, lean, aim distance and crosshair position once a second.");
        }
    }
}
