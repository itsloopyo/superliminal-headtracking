using CameraUnlock.Core.Data;
using CameraUnlock.Core.Math;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using SuperliminalHeadTracking.CameraRig;
using UnityEngine;

namespace SuperliminalHeadTracking.Core
{
    /// <summary>
    /// Runs the shared pipeline - receiver, interpolator, processor - and produces one
    /// <see cref="HeadPose"/> per frame for the camera rig to draw with.
    ///
    /// This is the transform-mode counterpart of core's ViewMatrixTrackingController.
    /// It is not that class with the write removed: that controller only exists to
    /// write worldToCameraMatrix, and its transitions are applied inside its own
    /// render hook, so a caller that reads its LastTracking* values gets the held
    /// pose during a fade-out rather than the faded one, and the view snaps instead of
    /// easing when the player pauses.
    ///
    /// Nothing here keeps a centre. Every tracker in use centres itself, and a second
    /// centre in series with the tracker's own drifts apart from it - see the
    /// doctrine's Centering section. The pose that arrives is the pose that is applied.
    /// </summary>
    public class TrackingPipeline
    {
        private const float TransitionInDuration = 0.5f;
        private const float TransitionOutDuration = 0.3f;

        private readonly ITrackingDataSource _source;
        private readonly TrackingProcessor _processor;
        private readonly PoseInterpolator _interpolator;
        private readonly PositionProcessor _positionProcessor;
        private readonly PositionInterpolator _positionInterpolator;

        private HeadPose _lastAppliedPose = HeadPose.Zero;

        private bool _wasApplying;
        private bool _isTransitioningIn;
        private float _transitionInProgress;
        private bool _isTransitioningOut;
        private float _transitionOutProgress;

        // 6DOF stays off until the tracker delivers a non-zero position sample, so a
        // rotation-only tracker never gets a position offset.
        private bool _detected6DOF;

        public bool RotationEnabled { get; set; }
        public bool PositionEnabled { get; set; }

        /// <summary>
        /// The fraction of the requested lean the level leaves room for, 0 to 1, set
        /// by the lean clamp.
        ///
        /// Applied to the processor's OUTPUT rather than folded into its settings. The
        /// settings route puts the allowance upstream of the position smoothing, where
        /// tightening it only takes effect as fast as the smoothing eases - and while
        /// the lean sits below the scaled limit the re-clamp never bites at all, so a
        /// doorframe edge that halves the allowance between two frames would leave the
        /// eye up to that much past the wall for the length of the ease. Scaling here
        /// is exactly the doctrine's "tighten instantly, release slowly": the tighten
        /// lands whole on the frame it is decided, and the release pacing stays where
        /// it belongs, inside the clamp.
        /// </summary>
        public float LeanAllowance { get; set; }

        /// <summary>Whether a pose is reaching the camera this frame.</summary>
        public bool IsApplying { get; private set; }

        /// <summary>The pose handed to the rig on the last frame that applied one.</summary>
        public HeadPose LastAppliedPose { get { return _lastAppliedPose; } }

        public bool IsRemoteConnection { get; private set; }

        public TrackingPipeline(ITrackingDataSource source, TrackingProcessor processor,
            PoseInterpolator interpolator, PositionProcessor positionProcessor,
            PositionInterpolator positionInterpolator)
        {
            _source = source;
            _processor = processor;
            _interpolator = interpolator;
            _positionProcessor = positionProcessor;
            _positionInterpolator = positionInterpolator;
            RotationEnabled = true;
            PositionEnabled = true;
            LeanAllowance = 1f;
        }

        /// <summary>
        /// Advances one frame. Returns the pose to draw with, or null-equivalent
        /// (<see cref="IsApplying"/> false) when nothing should be applied.
        /// </summary>
        public HeadPose ProcessFrame(bool enabled)
        {
            if (enabled && _source.IsReceiving)
            {
                _isTransitioningOut = false;

                // Re-read every frame, before either processor runs: the smoothing
                // parameter is selected per connection, so a switch from a local
                // tracker to a phone on WiFi takes effect without a restart.
                IsRemoteConnection = _source.IsRemoteConnection;
                _processor.IsRemoteConnection = IsRemoteConnection;
                _positionProcessor.IsRemoteConnection = IsRemoteConnection;

                if (!_wasApplying) BeginSession();

                // Drained rather than acted on. Superliminal's mod keeps no centre, so
                // a tracker-app CENTER press has nothing to do here - but leaving the
                // request latched would let it fire at some arbitrary later moment.
                _source.TryConsumeRecenterRequest();

                float scale = AdvanceTransitionIn();

                TrackingPose raw = _source.GetLatestPose();
                TrackingPose interpolated = _interpolator.Update(raw, Time.deltaTime);
                TrackingPose processed = _processor.Process(interpolated, Time.deltaTime);

                _lastAppliedPose = new HeadPose
                {
                    Yaw = RotationEnabled ? processed.Yaw : 0f,
                    Pitch = RotationEnabled ? processed.Pitch : 0f,
                    Roll = RotationEnabled ? processed.Roll : 0f,
                    Position = ComputePosition()
                }.Scaled(scale);

                _wasApplying = true;
                IsApplying = true;
                return _lastAppliedPose;
            }

            _source.TryConsumeRecenterRequest();

            if (_isTransitioningOut)
            {
                AdvanceTransitionOut();
            }
            else if (_wasApplying)
            {
                _isTransitioningOut = true;
                _transitionOutProgress = 0f;
                AdvanceTransitionOut();
            }

            if (_isTransitioningOut)
            {
                IsApplying = true;
                return HeadPose.Lerp(_lastAppliedPose, HeadPose.Zero, _transitionOutProgress);
            }

            IsApplying = false;
            return HeadPose.Zero;
        }

        /// <summary>
        /// Re-arms the fade-in. Called when the player enables tracking.
        ///
        /// Clearing _wasApplying is what actually re-arms it: ProcessFrame begins a
        /// session only on the transition out of not-applying, and a fade-OUT keeps
        /// _wasApplying set until it completes. Without this, toggling off and back on
        /// inside the 0.3s fade-out resumes at full scale on the next frame, and since
        /// the resets below drop the smoothed value the view snaps to the whole
        /// tracker pose in one frame rather than easing back in. BeginSession does
        /// both resets itself, so they stay here only for the already-idle case.
        /// </summary>
        public void OnTrackingEnabled()
        {
            ResetSmoothing();
            ResetInterpolators();
            _isTransitioningOut = false;
            _wasApplying = false;
        }

        public void OnTrackingDisabled()
        {
            if (_wasApplying)
            {
                _isTransitioningOut = true;
                _transitionOutProgress = 0f;
            }
        }

        /// <summary>
        /// Drops everything, with no fade. For a camera cut - a level load, a portal
        /// teleport, the game switching player rigs - where easing out of the previous
        /// room's pose would be visible as a drift.
        /// </summary>
        public void ResetState()
        {
            _isTransitioningIn = false;
            _isTransitioningOut = false;
            _transitionInProgress = 0f;
            _transitionOutProgress = 0f;
            _wasApplying = false;
            IsApplying = false;
            _detected6DOF = false;
            _lastAppliedPose = HeadPose.Zero;
            ResetSmoothing();
            ResetInterpolators();
        }

        private Vec3 ComputePosition()
        {
            if (!PositionEnabled)
            {
                _detected6DOF = false;
                return Vec3.Zero;
            }

            PositionData rawPos = _source.GetLatestPosition();
            if (!_detected6DOF && (rawPos.X != 0f || rawPos.Y != 0f || rawPos.Z != 0f))
                _detected6DOF = true;

            if (!_detected6DOF) return Vec3.Zero;

            PositionData interpolated = _positionInterpolator.Update(rawPos, Time.deltaTime);

            // Taken from the processor's smoothed state so the pivot compensation uses
            // the same rotation the camera is being turned by.
            float physYaw, physPitch, physRoll;
            _processor.GetSmoothedRotation(out physYaw, out physPitch, out physRoll);
            Quat4 physicalRotation = QuaternionUtils.FromYawPitchRoll(physYaw, physPitch, physRoll);

            Vec3 offset = _positionProcessor.Process(interpolated, physicalRotation, Time.deltaTime);
            return LeanAllowance == 1f ? offset : offset * LeanAllowance;
        }

        private void BeginSession()
        {
            _isTransitioningIn = true;
            _transitionInProgress = 0f;
            _detected6DOF = false;
            ResetInterpolators();
            ResetSmoothing();
        }

        private float AdvanceTransitionIn()
        {
            if (!_isTransitioningIn) return 1f;

            // Scaled time, matching the interpolator and processor that run alongside
            // it. On unscaled time at timeScale 0 this would fade in a frozen, stale
            // head offset over a paused game.
            _transitionInProgress += Time.deltaTime / TransitionInDuration;
            if (_transitionInProgress >= 1f)
            {
                _transitionInProgress = 1f;
                _isTransitioningIn = false;
            }
            return _transitionInProgress * _transitionInProgress;
        }

        private void AdvanceTransitionOut()
        {
            // Unscaled, unlike the fade in. Tracking is suppressed on pause, and the
            // pause menu zeroes timeScale - on scaled time the fade could never
            // complete and the menu would render through a view still turned by
            // whatever the head was doing when the player pressed Escape.
            _transitionOutProgress += Time.unscaledDeltaTime / TransitionOutDuration;
            if (_transitionOutProgress >= 1f)
            {
                _isTransitioningOut = false;
                _wasApplying = false;
                IsApplying = false;
            }
        }

        private void ResetInterpolators()
        {
            _interpolator.Reset();
            _positionInterpolator.Reset();
        }

        private void ResetSmoothing()
        {
            _processor.ResetSmoothing();
            _positionProcessor.ResetSmoothing();
        }
    }
}
