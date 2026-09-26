using UnityEngine;

namespace SuperliminalHeadTracking.Legacy
{
    /// <summary>
    /// The settings v0.2.0 and every earlier build read from
    /// BepInEx\config\com.cameraunlock.superliminal.headtracking.cfg, with their defaults. Frozen: a
    /// later change to the runtime settings or to core's constants must never change what an old
    /// .cfg, or one missing a key, reads as. The defaults are literals for that reason, although the
    /// reader this was frozen from took the smoothing pair and the position limits from core, which
    /// held these same values.
    /// </summary>
    internal sealed class LegacyConfig
    {
        public bool EnabledOnStartup = true;
        public bool ShowStartupNotification = true;
        public bool WorldSpaceYaw = true;

        public bool ShowConnectionNotifications = true;
        public bool MoveCrosshair = true;

        public KeyCode ToggleKey = KeyCode.End;
        public KeyCode CycleTrackingModeKey = KeyCode.PageUp;
        public KeyCode YawModeKey = KeyCode.PageDown;

        public int UDPPort = 4242;

        public float YawSensitivity = 1.0f;
        public float PitchSensitivity = 1.0f;
        public float RollSensitivity = 1.0f;

        public float LocalSmoothing = 0.0f;
        public float RemoteSmoothing = 0.15f;

        public bool PositionEnabled = true;
        public float PositionSensitivityX = 1.0f;
        public float PositionSensitivityY = 1.0f;
        public float PositionSensitivityZ = 1.0f;
        public float PositionLimitX = 0.30f;
        public float PositionLimitY = 0.20f;
        public float PositionLimitZ = 0.40f;
        public float PositionLimitZBack = 0.10f;
        public float TrackerPivotForward = 0.0f;

        public bool CollisionEnabled = true;
        public float CollisionMargin = 0.12f;
        public float CollisionReleaseSmoothing = 0.9f;

        public bool LogAimGeometry = false;
    }
}
