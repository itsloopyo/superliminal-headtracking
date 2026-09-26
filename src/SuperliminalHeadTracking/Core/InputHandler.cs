using System;
using CameraUnlock.Core.Unity.Extensions;
using SuperliminalHeadTracking.Config;
using UnityEngine;

namespace SuperliminalHeadTracking.Core
{
    internal class InputHandler
    {
        private readonly ModConfig _config;

        public event Action OnTogglePressed;
        public event Action OnCycleTrackingModePressed;
        public event Action OnToggleYawModePressed;

        public KeyCode ToggleKey { get { return _config.ToggleKey; } }
        public KeyCode CycleTrackingModeKey { get { return _config.CycleTrackingModeKey; } }
        public KeyCode YawModeKey { get { return _config.YawModeKey; } }

        public InputHandler(ModConfig config)
        {
            _config = config;
        }

        public void CheckInput()
        {
            // Common case: nothing pressed this frame. Skip the GetKeyDown probes and
            // the config reads behind them.
            if (!Input.anyKeyDown) return;

            Dispatch(_config.ToggleKey, ChordHotkeys.ToggleLetter, OnTogglePressed);
            Dispatch(_config.CycleTrackingModeKey, ChordHotkeys.PositionLetter, OnCycleTrackingModePressed);
            Dispatch(_config.YawModeKey, ChordHotkeys.FourthToggleLetter, OnToggleYawModePressed);
        }

        private static void Dispatch(KeyCode primary, KeyCode chordLetter, Action handler)
        {
            if (ChordHotkeys.IsActionPressed(primary, chordLetter) && handler != null)
                handler();
        }
    }
}
