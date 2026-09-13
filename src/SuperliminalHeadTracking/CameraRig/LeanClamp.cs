using CameraUnlock.Core.Math;
using UnityEngine;

namespace SuperliminalHeadTracking.CameraRig
{
    /// <summary>
    /// Cuts a positional lean back to whatever the level leaves room for, so the
    /// rendered eye never ends up inside geometry. Rotation cannot cause this -
    /// rotation does not move the eye - so nothing here runs in rotation-only mode.
    ///
    /// Core owns this policy in C++ (cameraunlock::camera::LeanClamp) but has no C#
    /// equivalent, so the two halves are built here: this type is the policy, and
    /// the sweep it calls is the query.
    ///
    /// A zero-extent line, not a swept sphere. Superliminal's camera sits inside the
    /// player's own CharacterController capsule, and any sweep with a volume reports
    /// an immediate overlap from there and refuses the lean in every direction
    /// including away from the wall. The standoff is therefore carried here rather
    /// than being a sweep radius, and the trace has to overreach the lean to see the
    /// surface it is about to come to rest against.
    /// </summary>
    public class LeanClamp
    {
        // Floors the 1/cos overreach. A lean sliding along a wall approaches the
        // surface at a grazing angle where 1/cos diverges; 0.25 caps the trace at
        // four times the standoff rather than sending it across the map.
        private const float MinApproachCosine = 0.25f;

        private const float MinLeanToClamp = 1e-4f;

        private int _mask;
        private float _allowance = 1f;

        /// <summary>Standoff held off any surface, in meters.</summary>
        public float Margin { get; set; }

        /// <summary>
        /// Smoothing applied when the allowance GROWS, on the fleet's 0-1 scale
        /// (0.9 is a 200ms time constant). Tightening is never smoothed: easing into
        /// a smaller allowance leaves the eye inside the geometry for the duration of
        /// the ease, which is the whole bug. Easing back out stops the view popping
        /// when a doorframe clears between two frames.
        /// </summary>
        public float ReleaseSmoothing { get; set; }

        /// <summary>True while the last evaluated lean was cut back by geometry.</summary>
        public bool InContact { get; private set; }

        /// <summary>
        /// True when the sweep could not run. A clamp that has quietly stopped
        /// clamping looks exactly like one that never engaged, so this is reported
        /// separately from InContact rather than passing the lean through in silence.
        /// </summary>
        public bool LastQueryFailed { get; private set; }

        public bool HasMask { get { return _mask != 0; } }

        /// <summary>The physics mask the sweep runs with.</summary>
        public int Mask { get { return _mask; } }

        public LeanClamp(int layerMask)
        {
            _mask = layerMask;
            Margin = 0.15f;
            ReleaseSmoothing = 0.9f;
        }

        /// <summary>
        /// Sets the mask once the game's own is readable. Superliminal's is taken off
        /// the live ResizeScript, which only exists inside a loaded level, so the
        /// clamp starts with no mask and reports LastQueryFailed until it has one.
        /// </summary>
        public void SetMask(int layerMask)
        {
            _mask = layerMask;
        }

        /// <summary>
        /// Drops the allowance back to fully open. Call on any camera cut and on any
        /// frame that applies no lean at all, or the allowance carries the previous
        /// room's wall into the next one.
        /// </summary>
        public void Reset()
        {
            _allowance = 1f;
            InContact = false;

            // Not cleared unconditionally. The caller resets on every frame it applies
            // no lean, which is also the path a missing mask takes - so clearing here
            // would make "the sweep runs and the room is open" and "there is no sweep"
            // log the same line, and those need different fixes.
            LastQueryFailed = _mask == 0;
        }

        /// <summary>
        /// Fraction of the requested lean that fits, in [0, 1].
        /// </summary>
        /// <param name="cleanEye">Where the game itself put the camera this frame.</param>
        /// <param name="requestedOffset">World-space lean the tracker is asking for.</param>
        /// <param name="nearClipPlane">The camera's near clip distance.</param>
        /// <param name="deltaTime">Frame time, so the release is frame-rate independent.</param>
        public float Evaluate(Vector3 cleanEye, Vector3 requestedOffset, float nearClipPlane,
            float deltaTime)
        {
            InContact = false;
            LastQueryFailed = false;

            if (_mask == 0)
            {
                LastQueryFailed = true;
                _allowance = 1f;
                return 1f;
            }

            float requested = requestedOffset.magnitude;
            if (requested < MinLeanToClamp)
            {
                _allowance = 1f;
                return 1f;
            }

            // Geometry nearer the eye than the near plane is culled, so a wall held
            // at less than the near distance is still not drawn and the player still
            // sees through it. The standoff has to clear it with room to spare.
            float standoff = Mathf.Max(Margin, nearClipPlane * 1.5f);

            Vector3 direction = requestedOffset / requested;
            float traceDistance = requested + standoff / MinApproachCosine;

            RaycastHit hit;
            float target;
            if (Physics.Raycast(cleanEye, direction, out hit, traceDistance, _mask,
                    QueryTriggerInteraction.Ignore))
            {
                // Standoff measured along the surface normal, so a lean approaching a
                // wall at an angle is still held the full distance off its face.
                float approach = Mathf.Max(MinApproachCosine, Vector3.Dot(direction, -hit.normal));
                float allowedDistance = hit.distance - standoff / approach;
                target = Mathf.Clamp01(allowedDistance / requested);

                InContact = target < 1f;
            }
            else
            {
                target = 1f;
            }

            _allowance = target < _allowance
                ? target
                : SmoothingUtils.Smooth(_allowance, target, ReleaseSmoothing, deltaTime);

            return _allowance;
        }
    }
}
