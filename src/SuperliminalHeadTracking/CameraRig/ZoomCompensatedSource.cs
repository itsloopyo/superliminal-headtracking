using System;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Protocol;

namespace SuperliminalHeadTracking.CameraRig
{
    /// <summary>
    /// Wraps the tracker source and applies the FOV zoom factor to the pose on its
    /// way into the pipeline.
    ///
    /// This seat is chosen so that everything downstream agrees: the interpolator,
    /// the smoothing, the camera write, the reticle basis and the diagnostics all
    /// see the pose the camera was actually driven with. A compensation applied
    /// later - at the camera write alone - would leave the reticle projecting from
    /// a pose nobody is looking through, and the doctrine's rule that a log line
    /// must carry the APPLIED pose could not be satisfied.
    ///
    /// Yaw, pitch and position scale; roll does not. Roll rotates the image about
    /// the view axis, and ten degrees of head roll rolls the picture ten degrees at
    /// every field of view there is, so scaling it would flatten a tilt the player
    /// is holding and buy nothing.
    /// </summary>
    public class ZoomCompensatedSource : ITrackingDataSource
    {
        private readonly ITrackingDataSource _inner;
        private readonly Func<float> _zoomFactor;

        public ZoomCompensatedSource(ITrackingDataSource inner, Func<float> zoomFactor)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            if (zoomFactor == null) throw new ArgumentNullException("zoomFactor");
            _inner = inner;
            _zoomFactor = zoomFactor;
        }

        public bool IsReceiving { get { return _inner.IsReceiving; } }
        public bool IsRemoteConnection { get { return _inner.IsRemoteConnection; } }
        public bool IsFailed { get { return _inner.IsFailed; } }

        public bool IsDataFresh(int maxAgeMs = OpenTrackReceiver.DefaultMaxDataAgeMs)
        {
            return _inner.IsDataFresh(maxAgeMs);
        }

        public TrackingPose GetLatestPose()
        {
            TrackingPose pose = _inner.GetLatestPose();
            float factor = _zoomFactor();
            if (factor == 1f) return pose;

            return new TrackingPose(
                ZoomCompensation.ScaleAngle(pose.Yaw, factor),
                ZoomCompensation.ScaleAngle(pose.Pitch, factor),
                pose.Roll,
                pose.TimestampTicks);
        }

        public PositionData GetLatestPosition()
        {
            PositionData position = _inner.GetLatestPosition();
            float factor = _zoomFactor();
            if (factor == 1f) return position;

            // A head offset d seen at depth D lands at d / (2 * D * tan(fov/2)) of
            // the frame, so a lean scales linearly and exactly.
            return new PositionData(
                position.X * factor, position.Y * factor, position.Z * factor,
                position.TimestampTicks);
        }

        /// <summary>
        /// Deliberately NOT scaled. The raw rotation is the unprocessed tracker
        /// reading by definition, and its only callers are diagnostics that want to
        /// see what arrived rather than what was applied.
        /// </summary>
        public void GetRawRotation(out float yaw, out float pitch, out float roll)
        {
            _inner.GetRawRotation(out yaw, out pitch, out roll);
        }

        public bool TryConsumeRecenterRequest()
        {
            return _inner.TryConsumeRecenterRequest();
        }

        public void Recenter()
        {
            _inner.Recenter();
        }
    }
}
