using System;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;

namespace SuperliminalHeadTracking.CameraRig
{
    public sealed class LeanCollisionQuery
    {
        // Caps the overreach at four times the standoff for grazing approaches.
        private const float MinApproachCosine = 0.25f;
        private readonly LineCast _cast;

        public float Margin { get; set; }
        public float NearClipPlane { get; set; }
        public LeanQuery Query { get; }

        public LeanCollisionQuery(LineCast cast)
        {
            _cast = cast;
            Query = QueryObstruction;
        }

        private LeanObstruction QueryObstruction(Vec3 eye, Vec3 direction, float distance)
        {
            // The query includes the near-plane clearance, so core's Skin must be zero.
            float standoff = Math.Max(Margin, NearClipPlane * 1.5f);
            LineHit hit = _cast(eye, direction, distance + standoff / MinApproachCosine);
            if (!hit.Queried) return LeanObstruction.Failed;
            if (!hit.Hit) return LeanObstruction.Clear;

            float approach = Math.Max(MinApproachCosine, -Vec3.Dot(direction, hit.Normal));
            return LeanObstruction.Hit(Math.Max(0f, hit.Distance - standoff / approach));
        }
    }
}
