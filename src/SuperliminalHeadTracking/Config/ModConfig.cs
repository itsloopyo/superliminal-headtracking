using UnityEngine;

namespace SuperliminalHeadTracking.Config
{
    /// <summary>The settings the plugin runs on.</summary>
    internal sealed class ModConfig
    {
        public bool EnabledOnStartup { get; set; }
        public bool ShowStartupNotification { get; set; }
        public bool WorldSpaceYaw { get; set; }

        public bool ShowConnectionNotifications { get; set; }
        public bool MoveCrosshair { get; set; }

        public KeyCode ToggleKey { get; set; }
        public KeyCode CycleTrackingModeKey { get; set; }
        public KeyCode YawModeKey { get; set; }

        public int UdpPort { get; set; }

        public float YawSensitivity { get; set; }
        public float PitchSensitivity { get; set; }
        public float RollSensitivity { get; set; }

        public float LocalSmoothing { get; set; }
        public float RemoteSmoothing { get; set; }

        public bool PositionEnabled { get; set; }
        public float PositionSensitivityX { get; set; }
        public float PositionSensitivityY { get; set; }
        public float PositionSensitivityZ { get; set; }
        public float PositionLimitX { get; set; }
        public float PositionLimitY { get; set; }
        public float PositionLimitZ { get; set; }
        public float PositionLimitZBack { get; set; }
        public float TrackerPivotForward { get; set; }

        public bool CollisionEnabled { get; set; }
        public float CollisionMargin { get; set; }
        public float CollisionReleaseSmoothing { get; set; }

        public bool LogAimGeometry { get; set; }
    }
}
