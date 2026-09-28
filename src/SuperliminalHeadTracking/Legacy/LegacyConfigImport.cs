using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using CameraUnlock.Core.Config;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Input;
using SuperliminalHeadTracking.Config;
using UnityEngine;

namespace SuperliminalHeadTracking.Legacy
{
    /// <summary>
    /// The import the config owner runs on com.cameraunlock.superliminal.headtracking.cfg while
    /// CameraUnlock.ini is absent: <see cref="LegacyConfigReader"/> on a ConfigFile of its own over
    /// that file, then the map into <see cref="SuperliminalConfig"/>.
    /// <para>
    /// Not the plugin's Config: ConfigurationManager lists every entry bound there, and one bound
    /// by the import would sit in its window for the rest of the session doing nothing. A new
    /// ConfigFile reads the file as the one the loader builds for the plugin does.
    /// </para>
    /// </summary>
    internal static class LegacyConfigImport
    {
        /// <summary>The rotation multiplier every published build shipped on all three axes.</summary>
        public const float ShippedRotationSensitivity = 1.0f;

        /// <summary>The position multiplier every published build shipped on all three axes.</summary>
        public const float ShippedPositionSensitivity = 1.0f;

        public static LegacyImport<SuperliminalConfig> Create()
        {
            return new LegacyImport<SuperliminalConfig>(
                (input, config) => Run(new ConfigFile(input.Path, false), input, config), LegacyConfigKeys.All());
        }

        /// <param name="legacyFile">A ConfigFile over the legacy file that nothing has bound to.</param>
        public static ImportResult Run(ConfigFile legacyFile, LegacyImportInput input, SuperliminalConfig config)
        {
            if (!string.Equals(Path.GetFullPath(input.Path), Path.GetFullPath(legacyFile.ConfigFilePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("the owner hands over " + input.Path + ", and the ConfigFile reads "
                                                    + legacyFile.ConfigFilePath);
            }

            bool found;
            LegacyConfig legacy = LegacyConfigReader.Read(legacyFile, out found);
            var dropped = new List<DroppedValue>();
            var poseShaping = new List<PoseShapingValue>();
            var followsDefaultsIni = new LegacyFollowsDefaultsIni();
            Map(legacy, config, dropped, poseShaping, followsDefaultsIni);
            return found
                ? ImportResult.Imported(dropped, poseShaping, followsDefaultsIni.Concepts)
                : ImportResult.Absent(dropped, poseShaping, followsDefaultsIni.Concepts);
        }

        /// <summary>
        /// Every float the reader returns is inside its AcceptableValueRange, which BepInEx clamps
        /// NaN and infinity into, so no value reaches here that normalisation N2 would change.
        /// </summary>
        public static void Map(LegacyConfig legacy, SuperliminalConfig config, List<DroppedValue> dropped,
            List<PoseShapingValue> poseShaping, LegacyFollowsDefaultsIni followsDefaultsIni)
        {
            var shipped = new LegacyConfig();

            config.EnableOnStartup = legacy.EnabledOnStartup;
            config.ShowStartupNotification = legacy.ShowStartupNotification;
            config.WorldSpaceYaw = legacy.WorldSpaceYaw;
            config.ShowConnectionNotifications = legacy.ShowConnectionNotifications;
            config.UdpPort = legacy.UDPPort;
            config.LogAimGeometry = legacy.LogAimGeometry;

            config.ToggleKeyName = HotkeyList(legacy.ToggleKey, KeyCode.Y, "ToggleKey", dropped);
            config.CycleTrackingModeKeyName = HotkeyList(legacy.CycleTrackingModeKey, KeyCode.G, "CycleTrackingModeKey", dropped);
            config.YawModeKeyName = HotkeyList(legacy.YawModeKey, KeyCode.H, "YawModeKey", dropped);

            // The game's crosshair now always follows the aim. MoveCrosshair=true, as it shipped,
            // is what the mod does now, so only a player who turned it off loses a choice.
            if (!legacy.MoveCrosshair)
            {
                dropped.Add(new DroppedValue(DropRule.Reticle, "UI", "MoveCrosshair", "false"));
            }

            LegacyPoseShaping.Record(legacy.YawSensitivity, ShippedRotationSensitivity, "Sensitivity", "YawSensitivity", poseShaping, dropped);
            LegacyPoseShaping.Record(legacy.PitchSensitivity, ShippedRotationSensitivity, "Sensitivity", "PitchSensitivity", poseShaping, dropped);
            LegacyPoseShaping.Record(legacy.RollSensitivity, ShippedRotationSensitivity, "Sensitivity", "RollSensitivity", poseShaping, dropped);
            LegacyPoseShaping.Record(legacy.PositionSensitivityX, ShippedPositionSensitivity, "Position", "PositionSensitivityX", poseShaping, dropped);
            LegacyPoseShaping.Record(legacy.PositionSensitivityY, ShippedPositionSensitivity, "Position", "PositionSensitivityY", poseShaping, dropped);
            LegacyPoseShaping.Record(legacy.PositionSensitivityZ, ShippedPositionSensitivity, "Position", "PositionSensitivityZ", poseShaping, dropped);

            // The published builds had one position switch and a three-state cycle that always
            // started from rotation and position, so the switch set the startup mode.
            config.RotationEnabled = true;
            config.PositionEnabled = legacy.PositionEnabled;

            config.LocalSmoothing = legacy.LocalSmoothing;
            config.RemoteSmoothing = legacy.RemoteSmoothing;
            PositionSettings p = config.Position;
            // v0.2.0 built its limits with PositionSettings.Symmetric, so PositionLimitY was the
            // downward limit too.
            config.Position = new PositionSettings(
                p.SensitivityX, p.SensitivityY, p.SensitivityZ,
                legacy.PositionLimitX, legacy.PositionLimitY, legacy.PositionLimitY, legacy.PositionLimitZ, legacy.PositionLimitZBack,
                legacy.LocalSmoothing, legacy.RemoteSmoothing,
                p.InvertX, p.InvertY, p.InvertZ);

            LegacyTrackerPivot.Record(legacy.TrackerPivotForward, shipped.TrackerPivotForward, "Position", "TrackerPivotForward", dropped);

            config.CollisionEnabled = legacy.CollisionEnabled;
            config.CollisionMargin = legacy.CollisionMargin;
            config.CollisionReleaseSmoothing = legacy.CollisionReleaseSmoothing;

            followsDefaultsIni.Setting(ConfigConcepts.UdpPort, legacy.UDPPort, shipped.UDPPort);
            followsDefaultsIni.Setting(ConfigConcepts.EnableOnStartup, legacy.EnabledOnStartup, shipped.EnabledOnStartup);
            followsDefaultsIni.Setting(ConfigConcepts.WorldSpaceYaw, legacy.WorldSpaceYaw, shipped.WorldSpaceYaw);
            followsDefaultsIni.TrackingMode(legacy.PositionEnabled, shipped.PositionEnabled);
            followsDefaultsIni.Setting(ConfigConcepts.LocalSmoothing, legacy.LocalSmoothing, shipped.LocalSmoothing);
            followsDefaultsIni.Setting(ConfigConcepts.RemoteSmoothing, legacy.RemoteSmoothing, shipped.RemoteSmoothing);
            followsDefaultsIni.Setting(ConfigConcepts.PositionLimitX, legacy.PositionLimitX, shipped.PositionLimitX);
            followsDefaultsIni.Setting(ConfigConcepts.PositionLimitY, legacy.PositionLimitY, shipped.PositionLimitY);
            followsDefaultsIni.Setting(ConfigConcepts.PositionLimitYDown, legacy.PositionLimitY, shipped.PositionLimitY);
            followsDefaultsIni.Setting(ConfigConcepts.PositionLimitZ, legacy.PositionLimitZ, shipped.PositionLimitZ);
            followsDefaultsIni.Setting(ConfigConcepts.PositionLimitZBack, legacy.PositionLimitZBack, shipped.PositionLimitZBack);
            followsDefaultsIni.Setting(ConfigConcepts.CollisionEnabled, legacy.CollisionEnabled, shipped.CollisionEnabled);
            followsDefaultsIni.Setting(ConfigConcepts.CollisionReleaseSmoothing, legacy.CollisionReleaseSmoothing, shipped.CollisionReleaseSmoothing);
            followsDefaultsIni.Setting(ConfigConcepts.ToggleKey, legacy.ToggleKey, shipped.ToggleKey);
            followsDefaultsIni.Setting(ConfigConcepts.CycleTrackingModeKey, legacy.CycleTrackingModeKey, shipped.CycleTrackingModeKey);
            followsDefaultsIni.Setting(ConfigConcepts.YawModeKey, legacy.YawModeKey, shipped.YawModeKey);
        }

        /// <summary>
        /// The keys v0.2.0 fired an action on: the configured key, unless it was None, and the
        /// Ctrl+Shift chord that InputHandler checked beside it. A Ctrl, Shift or Alt key is
        /// unbound and dropped (N3), which leaves the chord. So is a key code Unity names no key for
        /// (a number in the .cfg, which BepInEx's enum parse accepts), dropped as KeyCodeOutOfRange (N1).
        /// </summary>
        public static string HotkeyList(KeyCode primary, KeyCode chordLetter, string key, ICollection<DroppedValue> dropped)
        {
            string chord = KeyBindings.Format(new[] { new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, (int)chordLetter) });
            string text = LegacyNormalisations.KeyCodeToBindings((int)primary, "Keybindings", key, dropped);
            return text.Length == 0 ? chord : text + ", " + chord;
        }
    }
}
