using System;
using BepInEx;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Math;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using CameraUnlock.Core.Tracking;
using CameraUnlock.Core.Unity.Extensions;
using CameraUnlock.Core.Unity.UI;
using HarmonyLib;
using SuperliminalHeadTracking.Aim;
using SuperliminalHeadTracking.CameraRig;
using SuperliminalHeadTracking.Config;
using SuperliminalHeadTracking.Game;
using SuperliminalHeadTracking.Legacy;
using UnityEngine;

namespace SuperliminalHeadTracking.Core
{
    /// <summary>
    /// Head tracking for Superliminal.
    ///
    /// The tracked pose exists only while the frame is being drawn. Everything the
    /// game reads the camera for - the grab ray, the perceived-size projection that
    /// the whole game is built on, physics, the interaction sweep - runs in Update,
    /// FixedUpdate or LateUpdate against the clean transform, so a resize measured
    /// with the head turned is identical to one measured with it still.
    /// </summary>
#if IL2CPP
    public class HeadTrackingPlugin
#else
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class HeadTrackingPlugin : BaseUnityPlugin
#endif
    {
#if IL2CPP
        private BepInEx.Logging.ManualLogSource Logger { get { return Il2Cpp.Plugin.Logger; } }
        private BepInEx.Configuration.ConfigFile Config { get { return Il2Cpp.Plugin.Settings; } }
#endif
        public const string PluginGUID = "com.cameraunlock.superliminal.headtracking";
        public const string PluginName = "Superliminal Head Tracking";
        public const string PluginVersion = "0.2.0";

        private const float StartupNotificationSeconds = 4f;
        private const float StatusNotificationSeconds = 1.5f;
        private const float AimGeometryLogInterval = 1f;

        // How long the aim mask has to stay unreadable before it is worth saying so.
        // ResizeScript fills it in Start, so a level load legitimately reads 0 first.
        private const float NoAimMaskGraceSeconds = 5f;

        // Superliminal's own default for the "FOV" pref, and its slider floor.
        private const float DefaultFov = 70f;

        public static HeadTrackingPlugin Instance { get; private set; }
        public bool TrackingEnabled { get; private set; }

        private ModConfig _config;
        private OpenTrackReceiver _receiver;
        private ZoomCompensatedSource _trackingSource;
        private TrackingProcessor _processor;
        private PoseInterpolator _interpolator;
        private PositionProcessor _positionProcessor;
        private PositionInterpolator _positionInterpolator;
        private TrackingPipeline _pipeline;
        private CameraRig.CameraRig _rig;
        private GameStateDetector _gameStateDetector;
        private InputHandler _inputHandler;
        private NotificationUI _notificationUI;
        private CrosshairBinding _crosshair;
        private WindowPlacement _windowPlacement;
        private AimTrace _aimTrace;
        private ReticleController _reticle;
        private LeanClamp _leanClamp;
        private Harmony _harmony;

        private bool _initialized;
        private bool _wasReceiving;
        private bool _portalScopeInstalled;
        private bool _loggedMasks;
        private bool _loggedNoAimMask;
        private float _noAimMaskSince = -1f;
        private bool _loggedCrosshair;
        private bool _loggedZoomBasis;
        private TrackingMode _trackingMode;
        private float _nextAimGeometryLogTime;
        private float _zoomFactor = 1f;

        // The camera the masks were taken off. Re-resolving costs a GetComponent and
        // two reflected field reads, and the masks only change when the player rig is
        // rebuilt - which is exactly when the camera reference changes.
        private Camera _maskedCamera;

        private bool _cachedIsRemoteConnection;
        private bool _hasCachedConnectionLocality;

        // Bound once. The resolve retry below runs every frame until the game's types
        // appear, and a lambda over Logger captures this, so building them at the call
        // site would allocate a closure and two delegates on every frame of the boot
        // sequence and every loading screen.
        private Action<string> _logInfo;
        private Action<string> _logError;

        // The player's own FOV setting, which is the basis the zoom factor is measured
        // against. It only moves on the options screen, so it is re-read whenever the
        // game is not in gameplay and held while it is, rather than marshalling a
        // PlayerPrefs string lookup on every rendered frame.
        private float _baseFov = DefaultFov;

        // The clamp's own copy of the allowance it last handed the pipeline, used to
        // recover the UNCLAMPED lean the tracker asked for. See TrackingPipeline.
        private float _lastLeanAllowance = 1f;
        private Vector3 _requestedLean;

        internal void Awake()
        {
            Instance = this;
            _logInfo = Logger.LogInfo;
            _logError = Logger.LogError;
            Logger.LogInfo(PluginName + " v" + PluginVersion + " initializing...");

            _config = LegacyConfigMap.ToRuntime(LegacyConfigReader.Read(Config, out _));
            // The reader writes nothing; this is the write BepInEx's Bind made on every start,
            // which creates the .cfg on the first one.
            Config.SaveOnConfigSet = true;
            Config.Save();

            GameReflection.Initialize(_logInfo, _logError);

            BuildPipeline();
            BuildRig();
            BuildAim();
            BuildGameStateDetector();
            BuildInput();

            _notificationUI = new NotificationUI();
            _windowPlacement = new WindowPlacement(_logInfo);
            _harmony = new Harmony(PluginGUID);

            // False means the bind failed and the receiver is polling for the port on
            // its own thread, having already logged the socket's error and the retry
            // interval. Announcing the port as listening underneath that contradicts
            // it, and the contradiction is what a user reads when they come to the log
            // asking why there is no tracking.
            bool listening = _receiver.Start(_config.UdpPort);
            TrackingEnabled = _config.EnabledOnStartup;
            _initialized = true;

            Logger.LogInfo(PluginName + " initialized. Tracking "
                           + (TrackingEnabled ? "enabled" : "disabled"));
            if (listening)
            {
                Logger.LogInfo("Listening on UDP port " + _config.UdpPort);
            }

            if (_config.ShowStartupNotification)
            {
                string status = TrackingEnabled ? "Head Tracking: ON" : "Head Tracking: OFF";
                _notificationUI.ShowNotification(status + "\n" + BuildHotkeyInfo(),
                    StartupNotificationSeconds);
            }
        }

        private void BuildPipeline()
        {
            _receiver = new OpenTrackReceiver();
            _receiver.Log = _logInfo;
            _trackingSource = new ZoomCompensatedSource(_receiver, () => _zoomFactor);

            _processor = new TrackingProcessor
            {
                LocalSmoothing = _config.LocalSmoothing,
                RemoteSmoothing = _config.RemoteSmoothing,
                // Pitch alone is turned over. The composition is the transform-space
                // twin of core's ViewMatrixModifier.ApplyHeadRotationDecomposed -
                // world yaw about Vector3.up, then Quaternion.Euler(pitch, 0, roll)
                // applied locally - where a positive Euler pitch rotates about the
                // camera's own +X and pitches the nose DOWN, while the trackers send
                // positive pitch for a head looking up.
                //
                // Yaw and roll are NOT negated here, against the fleet's usual vote.
                // Negating them was tried in game and turned the view the wrong way on
                // both axes.
                Sensitivity = new SensitivitySettings(
                    _config.YawSensitivity,
                    _config.PitchSensitivity,
                    _config.RollSensitivity,
                    invertYaw: false,
                    invertPitch: true,
                    invertRoll: false),
                Deadzone = DeadzoneSettings.None
            };
            _interpolator = new PoseInterpolator();

            // No axis is inverted here, and that is deliberate. x and z are both
            // mirrored, but HeadPose.EngineOffset does it at the point it writes the
            // camera, which is after the processor's asymmetric clamp. Setting InvertZ
            // instead would land ahead of that clamp and hand the forward lean the
            // 0.10m backward budget.
            PositionSettings positionSettings = PositionSettings.Symmetric(
                _config.PositionSensitivityX,
                _config.PositionSensitivityY,
                _config.PositionSensitivityZ,
                _config.PositionLimitX,
                _config.PositionLimitY,
                _config.PositionLimitZ,
                _config.PositionLimitZBack,
                _config.LocalSmoothing,
                _config.RemoteSmoothing,
                invertX: false, invertY: false, invertZ: false);

            _positionProcessor = new PositionProcessor
            {
                Settings = positionSettings,
                TrackerPivotForward = _config.TrackerPivotForward
            };
            _positionInterpolator = new PositionInterpolator();

            _pipeline = new TrackingPipeline(_trackingSource, _processor, _interpolator,
                _positionProcessor, _positionInterpolator);

            SetTrackingMode(_config.PositionEnabled
                ? TrackingMode.RotationAndPosition
                : TrackingMode.RotationOnly);
        }

        private void BuildRig()
        {
            _rig = new CameraRig.CameraRig(() => GameReflection.PlayerCamera);
            _rig.WorldSpaceYaw = _config.WorldSpaceYaw;
            _rig.Applied += OnCameraApplied;
            _rig.Enable();
        }

        private void BuildAim()
        {
            _crosshair = new CrosshairBinding();
            _aimTrace = new AimTrace();
            _reticle = new ReticleController(_crosshair, _aimTrace, _logError);
            _leanClamp = new LeanClamp(0)
            {
                Margin = _config.CollisionMargin,
                ReleaseSmoothing = _config.CollisionReleaseSmoothing
            };
        }

        private void BuildGameStateDetector()
        {
            _gameStateDetector = new GameStateDetector();
            _gameStateDetector.StateChanged += OnGameStateChanged;
        }

        private void BuildInput()
        {
            _inputHandler = new InputHandler(_config);
            _inputHandler.OnTogglePressed += HandleToggle;
            _inputHandler.OnCycleTrackingModePressed += HandleCycleTrackingMode;
            _inputHandler.OnToggleYawModePressed += HandleToggleYawMode;
        }

        private string BuildHotkeyInfo()
        {
            return "[" + _inputHandler.ToggleKey + "/Ctrl+Shift+" + ChordHotkeys.ToggleLetter + "] Toggle, "
                 + "[" + _inputHandler.CycleTrackingModeKey + "/Ctrl+Shift+" + ChordHotkeys.PositionLetter + "] Cycle Mode, "
                 + "[" + _inputHandler.YawModeKey + "/Ctrl+Shift+" + ChordHotkeys.FourthToggleLetter + "] Yaw";
        }

        internal void Update()
        {
            if (!_initialized) return;

            // The game types only exist once Assembly-CSharp has loaded its scene, so
            // the first resolve attempt in Awake can legitimately come up empty. A
            // dormant resolve is a different answer: GameManager was there and did not
            // carry what the mod reads, which no later frame changes, so the retry
            // stops rather than re-logging the same diagnosis every frame.
            if (!GameReflection.Resolved && !GameReflection.Dormant)
                GameReflection.Initialize(_logInfo, _logError);

            EnsurePortalScope();
            EnsureMasks();

            _inputHandler.CheckInput();
            _windowPlacement.Update();
            _gameStateDetector.Update();
            _notificationUI.Update();
            MonitorConnectionState();
            MonitorConnectionLocality();

            AdvanceTracking();
        }

        /// <summary>
        /// Advances the pipeline and hands the frame's pose to the rig.
        ///
        /// In Update rather than LateUpdate, and that is the whole reason it is a
        /// method of its own. The portal scope opens inside
        /// PortalManager.LateUpdate, and every Update runs before every LateUpdate,
        /// so this is the only placement that guarantees the portal graph is culled
        /// and its cameras placed with THIS frame's pose rather than the last one's.
        /// Nothing here reads the camera except the lean sweep, and the pose itself
        /// is pure tracker data - the composition against the camera happens later,
        /// at the moment the rig writes it.
        /// </summary>
        private void AdvanceTracking()
        {
            UpdateZoomFactor();

            bool shouldTrack = TrackingEnabled && _gameStateDetector.IsGameplayActive;
            ApplyLeanClamp(shouldTrack);

            HeadPose pose = _pipeline.ProcessFrame(shouldTrack);

            if (_pipeline.IsApplying)
            {
                _rig.SetPose(pose);
            }
            else
            {
                _rig.ClearPose();
                _reticle.Clear();
            }
        }

        /// <summary>
        /// Installs the portal scope on the first frame PortalManager's type is
        /// available. Deferred rather than done in Awake because Assembly-CSharp is
        /// not loaded when a BepInEx plugin wakes.
        /// </summary>
        private void EnsurePortalScope()
        {
            if (_portalScopeInstalled || !GameReflection.Resolved) return;
            _portalScopeInstalled = true;
            if (!PortalScopePatch.TryApply(_harmony, _rig, _logInfo))
                _logError("Portal scope not installed - portals will be culled and their"
                          + " cameras placed from the untracked camera, so expect portals"
                          + " to drop out and to show the wrong slice of the room while"
                          + " the head is turned.");
        }

        /// <summary>
        /// Takes the aim and collision masks off the live ResizeScript. Both are the
        /// game's own answers - what its grab ray stops on, and what it treats as
        /// solid while holding something - so neither is a list of layer names this
        /// mod invented, and a patch that moves them is followed without anyone
        /// reading the binary again. Re-read only when the player camera changes,
        /// which is when Superliminal rebuilds the player rig on a level load.
        /// </summary>
        private void EnsureMasks()
        {
            Camera cam = GameReflection.PlayerCamera;
            if (cam == _maskedCamera) return;

            Component resize = GameReflection.ResizeScript;
            if (resize == null)
            {
                // Cleared so the grace period below only ever measures frames on which
                // the mask was actually asked for.
                _noAimMaskSince = -1f;
                return;
            }

            int aimMask = GameReflection.GrabRayLayerMask(resize);
            if (aimMask == 0)
            {
                // Reported rather than returned into silence, but only once the
                // condition has PERSISTED. ResizeScript builds the mask in Start, and
                // FindObjectOfType reaches the component before Start has run, so a
                // healthy session legitimately reads 0 for a frame or two on a level
                // load - and an error logged there would never be retracted.
                if (_noAimMaskSince < 0f) _noAimMaskSince = Time.realtimeSinceStartup;
                if (!_loggedNoAimMask
                    && Time.realtimeSinceStartup - _noAimMaskSince >= NoAimMaskGraceSeconds)
                {
                    _loggedNoAimMask = true;
                    _logError("ResizeScript._layerMaskIgnorePlayer did not read as a"
                              + " usable layer mask, so the crosshair cannot be placed"
                              + " on what the grab ray hits.");
                }
                return;
            }

            _noAimMaskSince = -1f;

            int solidMask = SolidGeometryMask.Build(GameReflection.SolidWorldLayerMask(resize));

            _maskedCamera = cam;

            if (_aimTrace.Mask == aimMask && _leanClamp.Mask == solidMask) return;

            _aimTrace.SetMask(aimMask);
            _leanClamp.SetMask(solidMask);

            if (_loggedMasks) return;
            _loggedMasks = true;
            Logger.LogInfo("Aim mask (ResizeScript._layerMaskIgnorePlayer): "
                           + GameReflection.DescribeLayerMask(aimMask));
            Logger.LogInfo("Collision mask (ResizeScript._layerMaskIgnoreGrabbed"
                           + DescribeNarrowing(SolidGeometryMask.LastNarrowing)
                           + "): " + GameReflection.DescribeLayerMask(solidMask));
        }

        private static string DescribeNarrowing(SolidGeometryMask.Narrowing narrowing)
        {
            switch (narrowing)
            {
                case SolidGeometryMask.Narrowing.Narrowed:
                    return " intersected with the layers the Player capsule collides with";
                case SolidGeometryMask.Narrowing.MatrixPermitsEveryLayer:
                    return ", NOT narrowed - the layer collision matrix lets the Player"
                           + " layer collide with every layer, so it rules nothing out";
                case SolidGeometryMask.Narrowing.NoGrabMask:
                    return " did not read, so the lean collision clamp has no mask at all";
                case SolidGeometryMask.Narrowing.NoPlayerLayer:
                    return ", NOT narrowed - there is no Player layer to narrow it against";
                default:
                    return ", NOT narrowed - the layer collision matrix was unreadable";
            }
        }

        private void MonitorConnectionLocality()
        {
            bool isRemoteConnection = _receiver.IsRemoteConnection;
            if (_hasCachedConnectionLocality && isRemoteConnection == _cachedIsRemoteConnection)
                return;

            _cachedIsRemoteConnection = isRemoteConnection;
            _hasCachedConnectionLocality = true;

            // Off the processor's own values, not off the config entries. The two are
            // the same until someone edits the config while the game is running, and
            // then the entries carry the new number while the processor keeps the one
            // it was built with - so reading the config here would print a smoothing
            // value that is not the one being applied.
            float effective = SmoothingUtils.GetEffectiveSmoothing(
                _processor.LocalSmoothing, _processor.RemoteSmoothing, isRemoteConnection);
            Logger.LogInfo("Tracker source is " + (isRemoteConnection ? "remote" : "local")
                           + ", smoothing=" + effective.ToString("F2"));
        }

        /// <summary>
        /// Sweeps from the clean eye toward where the head wants to go and scales the
        /// whole position offset to whatever fits. Runs before ProcessFrame so the
        /// clamp lands on the offset before it is applied, not after the eye is
        /// already inside the wall.
        /// </summary>
        private void ApplyLeanClamp(bool shouldTrack)
        {
            bool active = shouldTrack
                          && _config.CollisionEnabled
                          && _leanClamp.HasMask
                          && _pipeline.PositionEnabled;

            if (!active)
            {
                // Unconditionally, not only when something is left to undo. Both are
                // O(1) and idempotent, and the short-circuit meant the clamp was never
                // reset at all in the one state that most needs reporting: a mask that
                // never resolved leaves the allowance at 1 and the request at zero from
                // the first frame, so LastQueryFailed stayed false and the diagnostics
                // line could not tell "the sweep is not running" from "the sweep runs
                // and the room is open".
                ResetLean();
                return;
            }

            Camera cam = GameReflection.PlayerCamera;
            if (cam == null) return;

            // The camera pose is as of the last frame's LateUpdate, since this runs in
            // Update. A frame stale at worst, over a query spanning 0.3m, while a
            // walking player covers under 0.1m in that time.
            //
            // The sweep is asked about the UNCLAMPED lean, recovered by undoing the
            // scale the clamp itself applied. Sweeping along the applied offset
            // instead produces a limit cycle the moment the clamp refuses a lean
            // outright: the offset goes to zero, a zero offset has no direction, the
            // allowance reopens, the lean comes back, and the eye chatters in and out
            // of the wall. Below the threshold the direction is unrecoverable, so the
            // last known request is held - the head is being refused, not centred.
            if (_lastLeanAllowance > 0.05f)
            {
                _requestedLean = _pipeline.LastAppliedPose.EngineOffset / _lastLeanAllowance;
            }

            if (_requestedLean.sqrMagnitude < 1e-8f)
            {
                if (_lastLeanAllowance != 1f) ResetLean();
                return;
            }

            Transform camTr = cam.transform;
            Vector3 requestedWorld = camTr.rotation * _requestedLean;
            float allowance = _leanClamp.Evaluate(
                camTr.position, requestedWorld, cam.nearClipPlane, Time.deltaTime);

            if (allowance == _lastLeanAllowance) return;
            _lastLeanAllowance = allowance;
            _pipeline.LeanAllowance = allowance;
        }

        /// <summary>
        /// Puts the lean allowance back to fully open, on the clamp and on the
        /// pipeline alike. Every path that stops applying a lean goes through here, so
        /// none of them can restore three of the four and forget the fourth.
        /// </summary>
        private void ResetLean()
        {
            _leanClamp.Reset();
            _pipeline.LeanAllowance = 1f;
            _lastLeanAllowance = 1f;
            _requestedLean = Vector3.zero;
        }

        /// <summary>
        /// The crosshair is placed from here, inside the rig's applied window, so it
        /// is projected through the same camera the frame is drawn through.
        /// </summary>
        private void OnCameraApplied(AppliedFrame frame)
        {
            _reticle.MeasureEngineDelta = _config.LogAimGeometry;

            if (_config.MoveCrosshair)
            {
                _reticle.OnApplied(frame);

                if (!_loggedCrosshair && _reticle.IsActive)
                {
                    _loggedCrosshair = true;
                    Logger.LogInfo("Crosshair bound: " + _crosshair.Describe());
                }
            }
            else
            {
                _reticle.Clear();
            }

            // Outside the crosshair branch on purpose: the diagnostics line reports
            // the camera basis, which exists whether or not the crosshair is being
            // moved, and an axis check must not depend on a UI setting.
            LogAimGeometry(frame);
        }

        /// <summary>
        /// Re-reads the rendered FOV against the player's own setting every frame.
        /// Superliminal widens the camera toward 100 degrees through
        /// FoVEnterColliderLerp and rewrites it continuously in the dolly-zoom set
        /// pieces; without this the head would move the picture by a different amount
        /// in each of them.
        /// </summary>
        private void UpdateZoomFactor()
        {
            Camera cam = GameReflection.PlayerCamera;
            if (cam == null)
            {
                _zoomFactor = 1f;
                return;
            }

            float live = cam.fieldOfView;

            // The same number ApplyQualitySettingsToCamera assigns into
            // Camera.fieldOfView, so base and live are the same vertical angle by
            // construction. Superliminal clamps its own slider to 70..90.
            //
            // Only the options screen writes it, and the options screen is not
            // gameplay, so it is re-read while the mod is idle and held while it is
            // driving the camera.
            if (_gameStateDetector.State != GameState.Gameplay)
                _baseFov = PlayerPrefs.GetFloat("FOV", DefaultFov);

            _zoomFactor = ZoomCompensation.Factor(live, _baseFov);

            // Logged off the camera rather than off a tracker sample, so the basis is
            // visible without a tracker connected. Held until ordinary gameplay,
            // because the gate on this line is that it reads factor=1.0000 - a line
            // written during a dolly set piece says something else and proves nothing.
            if (!_loggedZoomBasis && _gameStateDetector.State == GameState.Gameplay)
            {
                _loggedZoomBasis = true;
                Logger.LogInfo(string.Format(
                    "ZOOMBASIS live={0:F2}deg vertical base={1:F2}deg vertical "
                    + "aspect={2:F4} factor={3:F4}",
                    live, _baseFov, cam.aspect, _zoomFactor));
            }
        }

        /// <summary>
        /// Applied pose, the tracked basis measured against the clean one, the lean,
        /// the aim distance and the crosshair offset - on one line and on one frame.
        /// Reading the distance off one log line and the offset off another makes a
        /// distance fault and a sign fault read alike.
        ///
        /// The six dot products are the part that cannot be reasoned out and has to be
        /// measured: turnR/turnU positive mean the view went right/up, tiltR positive
        /// means the top of the view went right, and leanR/leanU/leanF are the same
        /// three questions for the eye. Send a known single-axis pose and the signs
        /// settle by arithmetic.
        /// </summary>
        private void LogAimGeometry(AppliedFrame frame)
        {
            if (!_config.LogAimGeometry) return;
            if (Time.realtimeSinceStartup < _nextAimGeometryLogTime) return;
            _nextAimGeometryLogTime = Time.realtimeSinceStartup + AimGeometryLogInterval;

            HeadPose pose = _rig.Pose;
            ReticlePlacement placement = _reticle.LastPlacement;

            Vector3 cleanRight = frame.CleanRotation * Vector3.right;
            Vector3 cleanUp = frame.CleanRotation * Vector3.up;
            Vector3 cleanForward = frame.CleanForward;
            Vector3 eyeDelta = frame.TrackedEye - frame.CleanEye;

            Logger.LogInfo(string.Format(
                "AIMGEO rot=({0:F2},{1:F2},{2:F2}) lean=({3:F3},{4:F3},{5:F3}) "
                + "hit={6} dist={7:F2} ndc=({8:F4},{9:F4}) valid={10} enginedelta={11:F4} "
                + "crosshair=({24:F1},{25:F1})canvas "
                + "leanAllow={12:F2} contact={13} queryFail={14} nearclip={15:F3} "
                + "fov={16:F1} zoom={17:F4} "
                + "applied[turnR={18:F4} turnU={19:F4} tiltR={20:F4} "
                + "leanR={21:F4} leanU={22:F4} leanF={23:F4}]",
                pose.Yaw, pose.Pitch, pose.Roll,
                pose.Position.X, pose.Position.Y, pose.Position.Z,
                placement.Hit, placement.Distance,
                placement.Ndc.x, placement.Ndc.y, placement.Valid, placement.EngineDelta,
                _lastLeanAllowance, _leanClamp.InContact, _leanClamp.LastQueryFailed,
                frame.Camera.nearClipPlane, frame.Camera.fieldOfView, _zoomFactor,
                Vector3.Dot(frame.TrackedForward, cleanRight),
                Vector3.Dot(frame.TrackedForward, cleanUp),
                Vector3.Dot(frame.TrackedUp, cleanRight),
                Vector3.Dot(eyeDelta, cleanRight),
                Vector3.Dot(eyeDelta, cleanUp),
                Vector3.Dot(eyeDelta, cleanForward),
                _crosshair.AppliedOffset.x, _crosshair.AppliedOffset.y));
        }

        internal void OnGUI()
        {
            if (_notificationUI != null) _notificationUI.Draw();
        }

        internal void OnDestroy()
        {
            Logger.LogInfo(PluginName + " shutting down...");

            if (_inputHandler != null)
            {
                _inputHandler.OnTogglePressed -= HandleToggle;
                _inputHandler.OnCycleTrackingModePressed -= HandleCycleTrackingMode;
                _inputHandler.OnToggleYawModePressed -= HandleToggleYawMode;
            }
            if (_gameStateDetector != null)
                _gameStateDetector.StateChanged -= OnGameStateChanged;

            if (_rig != null)
            {
                _rig.Applied -= OnCameraApplied;
                _rig.Disable();
            }
            if (_crosshair != null) _crosshair.Release();
            if (_harmony != null) _harmony.UnpatchSelf();
            PortalScopePatch.Release();
            if (_receiver != null) _receiver.Dispose();

            Instance = null;
        }

        private void MonitorConnectionState()
        {
            bool isReceiving = _receiver.IsReceiving;
            if (isReceiving == _wasReceiving) return;

            Logger.LogInfo(isReceiving
                ? "OpenTrack connection established"
                : "OpenTrack connection lost");

            if (_config.ShowConnectionNotifications)
            {
                if (isReceiving) _notificationUI.ShowConnectionEstablished();
                else _notificationUI.ShowConnectionLost();
            }
            _wasReceiving = isReceiving;
        }

        private void HandleToggle()
        {
            TrackingEnabled = !TrackingEnabled;
            if (TrackingEnabled)
            {
                _pipeline.OnTrackingEnabled();
                _notificationUI.ShowTrackingEnabled();
                Logger.LogInfo("Head tracking enabled");
            }
            else
            {
                _pipeline.OnTrackingDisabled();
                _notificationUI.ShowTrackingDisabled();
                Logger.LogInfo("Head tracking disabled");
            }
        }

        private void HandleCycleTrackingMode()
        {
            SetTrackingMode((TrackingMode)(((int)_trackingMode + 1) % 3));

            string label = "Tracking: " + _trackingMode.Description();
            _notificationUI.ShowNotification(label, NotificationType.Info, StatusNotificationSeconds);
            Logger.LogInfo(label);
        }

        private void SetTrackingMode(TrackingMode mode)
        {
            _trackingMode = mode;
            _pipeline.RotationEnabled = mode != TrackingMode.PositionOnly;
            _pipeline.PositionEnabled = mode != TrackingMode.RotationOnly;
        }

        private void HandleToggleYawMode()
        {
            _rig.WorldSpaceYaw = !_rig.WorldSpaceYaw;
            _notificationUI.ShowNotification(
                _rig.WorldSpaceYaw ? "Yaw: World-locked" : "Yaw: Camera-local",
                NotificationType.Info,
                StatusNotificationSeconds);
            Logger.LogInfo("Yaw mode: " + (_rig.WorldSpaceYaw ? "world-locked" : "camera-local"));
        }

        private void OnGameStateChanged(GameState newState)
        {
            Logger.LogInfo("Game state: " + newState);

            if (newState == GameState.Gameplay && TrackingEnabled)
            {
                _pipeline.OnTrackingEnabled();
                return;
            }

            // A camera cut, not a fade: leaving gameplay means a level load, a portal
            // teleport, a menu or a multiplayer session, and easing out of the
            // previous room's pose would read as a drift.
            _pipeline.ResetState();
            _rig.ClearPose();
            _reticle.Clear();
            _crosshair.Release();
            _loggedCrosshair = false;
            ResetLean();
        }
    }
}
