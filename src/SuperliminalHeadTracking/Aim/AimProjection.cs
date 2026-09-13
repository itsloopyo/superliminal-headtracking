using UnityEngine;

namespace SuperliminalHeadTracking.Aim
{
    /// <summary>
    /// Projects a world point into the frame that is about to be drawn, in normalized
    /// device coordinates: -1..+1 across the frustum, +x right, +y up.
    ///
    /// Called from inside the rig's applied window, so <c>cam.worldToCameraMatrix</c>
    /// is the engine's own matrix derived from the tracked transform and
    /// <c>cam.projectionMatrix</c> is the projection this frame renders with. There
    /// is no per-axis Euler formula here to drift on a combined pose, and no second
    /// derivation of the composition that could disagree with the camera write - the
    /// engine's matrices are the derivation.
    /// </summary>
    public static class AimProjection
    {
        // A point approaching the plane of the camera projects to infinity. The guard
        // is on the magnitude, not just the sign - an NDC of 1e30 is a NaN on its way
        // into a RectTransform.
        private const float ClipEpsilon = 1e-4f;

        /// <summary>Projects a world POINT. This is what a reticle marks.</summary>
        public static bool TryProjectPoint(Camera cam, Vector3 worldPoint, out Vector2 ndc)
        {
            return Project(cam, new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1f), out ndc);
        }

        /// <summary>
        /// Projects a world DIRECTION, which is where a point at infinity lands. Used
        /// on a definite no-hit: the aim has no surface to mark, so the reticle marks
        /// the direction instead. Never a substituted fixed distance.
        /// </summary>
        public static bool TryProjectDirection(Camera cam, Vector3 worldDirection, out Vector2 ndc)
        {
            return Project(cam, new Vector4(worldDirection.x, worldDirection.y, worldDirection.z, 0f), out ndc);
        }

        private static bool Project(Camera cam, Vector4 homogeneous, out Vector2 ndc)
        {
            ndc = Vector2.zero;
            if (cam == null) return false;

            Matrix4x4 viewProjection = cam.projectionMatrix * cam.worldToCameraMatrix;
            Vector4 clip = viewProjection * homogeneous;

            // Unity hands back the OpenGL-convention projection from this property
            // whatever the graphics API underneath is, and there clip.w == -z_view.
            // View space looks down -z, so w is positive in front of the camera.
            if (clip.w < ClipEpsilon) return false;

            ndc = new Vector2(clip.x / clip.w, clip.y / clip.w);
            return true;
        }

        /// <summary>
        /// The engine's own projection of the same point, in NDC. Logged against this
        /// mod's answer once a second under diagnostics: they agree only if the
        /// tracked transform is what the engine is projecting through, which is also
        /// what makes Superliminal's own world-anchored UI follow the view for free.
        /// </summary>
        public static bool TryEngineProjectPoint(Camera cam, Vector3 worldPoint, out Vector2 ndc)
        {
            ndc = Vector2.zero;
            if (cam == null) return false;

            Vector3 screen = cam.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f || Screen.width <= 0 || Screen.height <= 0) return false;

            ndc = new Vector2(
                screen.x / Screen.width * 2f - 1f,
                screen.y / Screen.height * 2f - 1f);
            return true;
        }
    }
}
