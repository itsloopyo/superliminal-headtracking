using System;
using System.Collections.Generic;
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

        public InputHandler(SuperliminalConfig config, Action<string> logWarning)
        {
            _toggle = Parse("ToggleKey", config.ToggleKeyName, logWarning);
            _cycleTrackingMode = Parse("CycleTrackingModeKey", config.CycleTrackingModeKeyName, logWarning);
            _yawMode = Parse("YawModeKey", config.YawModeKeyName, logWarning);
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

        // The table's hotkey codec has read every list the file holds, so a list that does not
        // parse reaches here only from a legacy import the owner deferred: a .cfg key code Unity
        // names no key for, which the import writes as the number. The items that parse, the
        // chord among them, are bound and the rest are named in the log.
        private static KeyBinding[] Parse(string key, string text, Action<string> logWarning)
        {
            KeyBinding[] bindings;
            string error;
            if (KeyBindings.TryParse(text, out bindings, out error)) return bindings;

            var kept = new List<KeyBinding>();
            foreach (string item in text.Split(','))
            {
                if (KeyBindings.TryParse(item, out bindings, out error)) kept.AddRange(bindings);
                else logWarning("[Hotkeys] " + key + ": " + error + ", so it is not bound this session");
            }
            return kept.ToArray();
        }
    }
}
