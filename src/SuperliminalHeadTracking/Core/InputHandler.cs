using System;
using CameraUnlock.Core.Input;
using CameraUnlock.Core.Unity.Extensions;
using SuperliminalHeadTracking.Config;
using UnityEngine;

namespace SuperliminalHeadTracking.Core
{
    /// <summary>
    /// Fires the mod's hotkey actions from the key lists in CameraUnlock.ini. Every binding in a
    /// list is an ordinary item, the Ctrl+Shift chords included.
    /// </summary>
    internal class InputHandler
    {
        private readonly KeyBinding[] _toggle;
        private readonly KeyBinding[] _cycleTrackingMode;
        private readonly KeyBinding[] _yawMode;

        public event Action OnTogglePressed;
        public event Action OnCycleTrackingModePressed;
        public event Action OnToggleYawModePressed;

        public InputHandler(SuperliminalConfig config)
        {
            _toggle = Parse("ToggleKey", config.ToggleKeyName);
            _cycleTrackingMode = Parse("CycleTrackingModeKey", config.CycleTrackingModeKeyName);
            _yawMode = Parse("YawModeKey", config.YawModeKeyName);
        }

        public void CheckInput()
        {
            // Common case: nothing pressed this frame. Every binding fires on a key's down-edge,
            // so a frame with none cannot trigger one.
            if (!Input.anyKeyDown) return;

            if (KeyBindingInput.IsTriggered(_toggle) && OnTogglePressed != null) OnTogglePressed();
            if (KeyBindingInput.IsTriggered(_cycleTrackingMode) && OnCycleTrackingModePressed != null) OnCycleTrackingModePressed();
            if (KeyBindingInput.IsTriggered(_yawMode) && OnToggleYawModePressed != null) OnToggleYawModePressed();
        }

        // The table's hotkey codec has read every list the file holds, and the legacy import
        // writes only key lists, so a list that does not parse is a bug.
        private static KeyBinding[] Parse(string key, string text)
        {
            KeyBinding[] bindings;
            string error;
            if (!KeyBindings.TryParse(text, out bindings, out error))
                throw new InvalidOperationException("[Hotkeys] " + key + "=" + text + ": " + error);
            return bindings;
        }
    }
}
