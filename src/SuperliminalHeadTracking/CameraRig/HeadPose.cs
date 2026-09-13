using CameraUnlock.Core.Data;
using UnityEngine;

namespace SuperliminalHeadTracking.CameraRig
{
    /// <summary>
    /// The head offset for one frame, and the single place it is turned into a camera
    /// pose. Both the camera write and the reticle projection call
    /// <see cref="Compose"/>, so there is one derivation and nothing that can disagree
    /// with itself on a combined pose.
    ///
    /// The composition is the same one
    /// <c>ViewMatrixModifier.ApplyHeadRotationDecomposed</c> encodes, written in
    /// transform space instead of view space. Superliminal needs it in transform space
    /// because its portal renderer positions every portal camera from
    /// <c>sourceCamera.transform</c> (PortalHelper.PlacePortalCamera), and its mirrors
    /// read <c>Camera.current.transform</c> - a view-matrix override leaves both of
    /// those looking the way the game pointed the camera while the frame is drawn from
    /// somewhere else.
    /// </summary>
    public struct HeadPose
    {
        public float Yaw;
        public float Pitch;
        public float Roll;
        public Vec3 Position;

        public static HeadPose Zero
        {
            get { return new HeadPose { Position = Vec3.Zero }; }
        }

        public static HeadPose Lerp(HeadPose a, HeadPose b, float t)
        {
            return new HeadPose
            {
                Yaw = Mathf.Lerp(a.Yaw, b.Yaw, t),
                Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t),
                Roll = Mathf.Lerp(a.Roll, b.Roll, t),
                Position = Vec3.Lerp(a.Position, b.Position, t)
            };
        }

        public HeadPose Scaled(float scale)
        {
            return new HeadPose
            {
                Yaw = Yaw * scale,
                Pitch = Pitch * scale,
                Roll = Roll * scale,
                Position = Position * scale
            };
        }

        /// <summary>
        /// Turns the clean camera pose into the tracked one.
        /// </summary>
        /// <param name="cleanPosition">Where the game put the camera this frame.</param>
        /// <param name="cleanRotation">Where the game pointed the camera this frame.</param>
        /// <param name="worldSpaceYaw">
        /// True applies head yaw about world up, so the horizon stays level when the
        /// player is looking up or down a stairwell. False applies it about the
        /// camera's own up.
        /// </param>
        public void Compose(Vector3 cleanPosition, Quaternion cleanRotation, bool worldSpaceYaw,
            out Vector3 trackedPosition, out Quaternion trackedRotation)
        {
            trackedRotation = worldSpaceYaw
                ? Quaternion.AngleAxis(Yaw, Vector3.up) * cleanRotation
                  * Quaternion.Euler(Pitch, 0f, Roll)
                : cleanRotation * Quaternion.Euler(Pitch, Yaw, Roll);

            trackedPosition = cleanPosition + cleanRotation * EngineOffset;
        }

        /// <summary>
        /// The lean in the camera transform's own axes: +x right, +y up, +z forward.
        ///
        /// Both flips are measured, not derived. x arrives mirrored from the tracker,
        /// which shows up in game as a lean that works and goes the wrong way. z is
        /// mirrored because the processor calls NEGATIVE z the forward lean while the
        /// camera transform's own +z is forward.
        ///
        /// Neither is done through PositionSettings.InvertX / InvertZ. Those land
        /// ahead of the processor's clamp, and for z that would hand the forward lean
        /// the tight backward budget. This is the engine boundary, after the clamp,
        /// and it is the only place the conversion happens - the lean sweep recovers
        /// its direction from here rather than writing the signs out a second time.
        /// </summary>
        public Vector3 EngineOffset
        {
            get { return new Vector3(-Position.X, Position.Y, -Position.Z); }
        }
    }
}
