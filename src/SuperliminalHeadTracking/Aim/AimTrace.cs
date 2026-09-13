using SuperliminalHeadTracking.Game;
using UnityEngine;

namespace SuperliminalHeadTracking.Aim
{
    /// <summary>Result of one cast along the clean camera forward.</summary>
    public struct AimResult
    {
        /// <summary>False when the cast could not run at all - no usable mask.</summary>
        public bool Valid;

        /// <summary>True when a surface the game would treat as the aim target was found.</summary>
        public bool Hit;

        /// <summary>World position of the contact. Only meaningful when Hit.</summary>
        public Vector3 Point;

        /// <summary>Distance from the clean eye to the contact, along the aim direction.</summary>
        public float Distance;
    }

    /// <summary>
    /// Finds the surface the crosshair marks, by casting the same ray Superliminal
    /// casts to decide what the player is pointing at.
    ///
    /// The depth is the entire size of the parallax correction, so which contact
    /// counts is the decision that matters. It is not decided here by a list of layer
    /// names: the mask is read off the live ResizeScript component, which is the
    /// game's own answer to what a look-ray stops on, and it follows a patch that
    /// changes it without anyone re-reading the binary.
    /// </summary>
    public class AimTrace
    {
        private const float MaxDistance = 1000f;

        // The game's own step off the far portal surface before it re-casts, so a
        // ray that arrives exactly on the plane does not immediately re-hit it.
        private const float PortalStepOff = 0.1f;

        private int _mask;

        /// <summary>The mask the last cast used.</summary>
        public int Mask { get { return _mask; } }

        public bool HasMask { get { return _mask != 0; } }

        public void SetMask(int mask)
        {
            _mask = mask;
        }

        /// <summary>
        /// Casts from the clean eye along the clean aim direction. Triggers are
        /// ignored: Superliminal's levels are dense with MOST trigger volumes, and
        /// nothing the grab ray passes through should set the crosshair's depth.
        /// </summary>
        public AimResult Cast(Vector3 cleanEye, Vector3 aimDirection)
        {
            AimResult result = default(AimResult);
            if (_mask == 0) return result;

            result.Valid = true;

            RaycastHit hit;
            if (!Physics.Raycast(cleanEye, aimDirection, out hit, MaxDistance, _mask,
                    QueryTriggerInteraction.Ignore))
            {
                return result;
            }

            // Measured from the contact's own world position projected onto the aim
            // direction. That is a distance by construction, which hit.distance only
            // happens to be while this is a zero-extent ray.
            float distance = Vector3.Dot(hit.point - cleanEye, aimDirection);

            // A portal carries a solid, non-trigger collider on the PortalHitTest
            // layer, sitting in the mask ResizeScript casts with - so a ray aimed
            // through an open doorway stops ON the doorway rather than on the wall the
            // player can see through it. The depth is the whole size of the parallax
            // correction, so stopping there puts the crosshair off the thing it
            // claims to mark by lean * (1/d_portal - 1/d_seen).
            //
            // Superliminal's own getCurrentRayResizeDistance does not stop there: it
            // transforms the ray to the far side, steps off the surface and re-casts.
            // This follows it, one hop deep because that is how deep the game goes,
            // with one deliberate difference - the game sums the far leg undivided
            // because it is building a resize RATIO, while what is wanted here is a
            // depth to project, so the far leg is converted to near-side units below.
            //
            // The far surface is drawn where the straight-line continuation in this
            // frame would put it: PlacePortalCamera positions the portal camera with
            // the same ThisSide-to-OtherSide transform, off the pose the portal scope
            // has already written, so the point recovered here projects through the
            // rendered camera to the pixel the far surface is drawn on.
            Ray onward;
            float farSideScale;
            if (GameReflection.TryTransformRayThroughPortal(hit.collider, hit.point, aimDirection,
                    out onward, out farSideScale))
            {
                Vector3 origin = onward.origin + onward.direction * PortalStepOff;
                RaycastHit far;
                if (!Physics.Raycast(origin, onward.direction, out far, MaxDistance, _mask,
                        QueryTriggerInteraction.Ignore))
                {
                    // Nothing on the far side, which the game treats as an infinite
                    // distance. Here that is a miss, so the crosshair marks the aim
                    // direction rather than the portal plane.
                    return result;
                }

                // Divided by the portal's from-to scale so the far leg is expressed
                // in near-side units, which is what the rest of this distance is in.
                distance += (PortalStepOff + far.distance) / farSideScale;
            }

            result.Hit = true;
            result.Distance = distance;
            result.Point = cleanEye + aimDirection * distance;
            return result;
        }
    }
}
