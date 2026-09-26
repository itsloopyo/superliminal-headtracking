using SuperliminalHeadTracking.Config;

namespace SuperliminalHeadTracking.Legacy
{
    /// <summary>Carries what <see cref="LegacyConfigReader"/> read into the settings the plugin runs on.</summary>
    internal static class LegacyConfigMap
    {
        public static ModConfig ToRuntime(LegacyConfig legacy)
        {
            return new ModConfig
            {
                EnabledOnStartup = legacy.EnabledOnStartup,
                ShowStartupNotification = legacy.ShowStartupNotification,
                WorldSpaceYaw = legacy.WorldSpaceYaw,
                ShowConnectionNotifications = legacy.ShowConnectionNotifications,
                MoveCrosshair = legacy.MoveCrosshair,
                ToggleKey = legacy.ToggleKey,
                CycleTrackingModeKey = legacy.CycleTrackingModeKey,
                YawModeKey = legacy.YawModeKey,
                UdpPort = legacy.UDPPort,
                YawSensitivity = legacy.YawSensitivity,
                PitchSensitivity = legacy.PitchSensitivity,
                RollSensitivity = legacy.RollSensitivity,
                LocalSmoothing = legacy.LocalSmoothing,
                RemoteSmoothing = legacy.RemoteSmoothing,
                PositionEnabled = legacy.PositionEnabled,
                PositionSensitivityX = legacy.PositionSensitivityX,
                PositionSensitivityY = legacy.PositionSensitivityY,
                PositionSensitivityZ = legacy.PositionSensitivityZ,
                PositionLimitX = legacy.PositionLimitX,
                PositionLimitY = legacy.PositionLimitY,
                PositionLimitZ = legacy.PositionLimitZ,
                PositionLimitZBack = legacy.PositionLimitZBack,
                TrackerPivotForward = legacy.TrackerPivotForward,
                CollisionEnabled = legacy.CollisionEnabled,
                CollisionMargin = legacy.CollisionMargin,
                CollisionReleaseSmoothing = legacy.CollisionReleaseSmoothing,
                LogAimGeometry = legacy.LogAimGeometry,
            };
        }
    }
}
