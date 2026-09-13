using System;
using UnityEngine;

namespace SuperliminalHeadTracking.Game
{
    /// <summary>
    /// Decides whether the frame being drawn is free-look gameplay. Polled on a fixed
    /// interval rather than every frame - every read is a reflected member access, and
    /// the states it separates last far longer than a frame.
    /// </summary>
    public class GameStateDetector
    {
        private const float PollInterval = 0.1f;
        private const float WarmupSeconds = 1.5f;

        private float _nextPollTime;
        private float _warmupEndTime = float.MaxValue;
        private Camera _warmupCamera;
        private GameState _state = GameState.Loading;

        public event Action<GameState> StateChanged;

        public GameState State { get { return _state; } }

        /// <summary>
        /// True only in gameplay and only once the warmup has elapsed. The warmup
        /// covers the frames right after a level loads, where the player rig is still
        /// being assembled and the camera snaps into place.
        /// </summary>
        public bool IsGameplayActive
        {
            get
            {
                if (_state != GameState.Gameplay) return false;
                return Time.realtimeSinceStartup >= _warmupEndTime;
            }
        }

        public void Update()
        {
            if (Time.realtimeSinceStartup < _nextPollTime) return;
            _nextPollTime = Time.realtimeSinceStartup + PollInterval;

            GameState next = Evaluate();

            // Charged when the PLAYER CAMERA changes, not on a state transition. The
            // game reaches gameplay both through a dedicated loading scene and through
            // a direct SceneManager.LoadScene that never shows one, and a new camera
            // instance is the one thing true of both. It is also false for an alt-tab
            // (this game pauses on focus loss) and for a scripted portal traversal,
            // which clear canControl without rebuilding the rig - re-arming on those
            // would blank the view for 1.5s every time.
            Camera cam = GameReflection.PlayerCamera;
            if (next == GameState.Gameplay && cam != _warmupCamera)
            {
                _warmupCamera = cam;
                _warmupEndTime = Time.realtimeSinceStartup + WarmupSeconds;
            }

            if (next == _state) return;
            _state = next;

            if (StateChanged != null) StateChanged(next);
        }

        private static GameState Evaluate()
        {
            if (!GameReflection.Resolved) return GameState.Loading;

            // Checked first, and it outranks everything below it: the multiplayer
            // answer holds for the whole session regardless of which screen the
            // player is on, and it stays true in the lobby where canControl is set
            // and the camera is live.
            if (GameReflection.IsMultiplayer) return GameState.Multiplayer;

            Camera cam = GameReflection.PlayerCamera;
            if (cam == null || !cam.isActiveAndEnabled) return GameState.Loading;

            if (!GameReflection.CanControl)
            {
                // canControl covers menus and scripted camera moves alike. A stopped
                // clock is what separates them: Superliminal's pause and escape menus
                // both zero timeScale, while a MOSTEventLockMovement cutscene runs at
                // full speed.
                return Time.timeScale <= 0f ? GameState.Menu : GameState.Cutscene;
            }

            // A backstop for anything that stops the clock without clearing
            // canControl.
            if (Time.timeScale <= 0f) return GameState.Menu;

            return GameState.Gameplay;
        }
    }
}
