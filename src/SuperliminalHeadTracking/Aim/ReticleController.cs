using SuperliminalHeadTracking.CameraRig;
using SuperliminalHeadTracking.Game;
using UnityEngine;

namespace SuperliminalHeadTracking.Aim
{
    /// <summary>Where the crosshair was put this frame, and why.</summary>
    public struct ReticlePlacement
    {
        /// <summary>The crosshair was moved. False means it was left at centre.</summary>
        public bool Valid;

        /// <summary>A surface was found and the crosshair marks that POINT.</summary>
        public bool Hit;

        /// <summary>Distance to that surface, in meters.</summary>
        public float Distance;

        /// <summary>The world point the crosshair marks.</summary>
        public Vector3 AimPoint;

        /// <summary>Offset from screen centre, in normalized device coordinates.</summary>
        public Vector2 Ndc;

        /// <summary>
        /// How far this mod's projection lands from the engine's own projection of the
        /// same point, in NDC. Negative when there was nothing to compare.
        /// </summary>
        public float EngineDelta;
    }

    /// <summary>
    /// Keeps Superliminal's own crosshair on the thing the player is pointing at
    /// while the head moves the view off that line.
    ///
    /// The crosshair marks a POINT, not a direction. With the eye leaned up to 30cm
    /// off the axis the grab ray leaves from, the two project to different places,
    /// and the gap grows the closer the target - which in this game is most of the
    /// time, because the whole mechanic is played at arm's length from a wall. So the
    /// ray is cast from the CLEAN camera, the one Superliminal still aims and grabs
    /// along, and its contact is projected through the matrices this frame is drawn
    /// with. There is no fixed convergence distance, no smoothing and no carried-over
    /// depth anywhere in this path: when the aim crosses an edge the impact point
    /// genuinely jumps, and the crosshair has to jump with it.
    ///
    /// Runs from CameraRig.Applied, inside the applied window, so the camera it
    /// projects through is the one the frame is drawn through. The crosshair canvas
    /// renders on the separate GUI camera, which draws after the player camera, so
    /// the write lands on the same frame it was computed for.
    /// </summary>
    public class ReticleController
    {
        // Beyond this the aim point is outside the frame and the crosshair marks
        // nothing a player can see, so it is hidden rather than drawn somewhere off
        // the edge. The margin over 1.0 keeps it drawn while part of it is still on
        // screen. It also bounds what is ever written into a RectTransform: NDC
        // diverges as the aim point approaches the camera plane, and without this the
        // last write before the projection gives up is an arbitrarily large
        // coordinate - inert while the element is hidden, but the kind of value that
        // turns a future regression into a crosshair flung across the canvas rather
        // than one merely in the wrong place.
        private const float MaxDrawableNdc = 1.1f;

        // How long the aim mask has to stay unreadable before it is worth saying so.
        // Covers the frames between the player rig existing and ResizeScript.Start
        // having filled the mask in.
        private const float NoMaskGraceSeconds = 5f;

        private readonly CrosshairBinding _crosshair;
        private readonly AimTrace _aimTrace;
        private readonly System.Action<string> _log;

        private int _lastComputedFrame = -1;
        private bool _loggedNoMask;
        private float _noMaskSince = -1f;

        public ReticlePlacement LastPlacement { get; private set; }

        /// <summary>
        /// Whether to also project the aim point through the engine's own
        /// WorldToScreenPoint and record how far the two answers land apart.
        ///
        /// Only the diagnostics line reads that number, and the comparison is a native
        /// projection plus two screen-size reads on every rendered frame, so it is off
        /// unless something is going to look at it. EngineDelta reports -1 while it is.
        /// </summary>
        public bool MeasureEngineDelta { get; set; }

        /// <summary>The crosshair is bound and being moved.</summary>
        public bool IsActive { get { return _crosshair.IsBound; } }

        public ReticleController(CrosshairBinding crosshair, AimTrace aimTrace,
            System.Action<string> log)
        {
            _crosshair = crosshair;
            _aimTrace = aimTrace;
            _log = log;
        }

        /// <summary>
        /// Places the crosshair for the frame described by <paramref name="frame"/>.
        ///
        /// Guarded on the frame counter because the rig can raise Applied twice in one
        /// frame: TransformFrameState.Restore clears its modified flags but leaves the
        /// stored frame number alone, so a camera culled and rendered twice in a frame
        /// - which a manual Camera.Render does - passes BeginFrame again. One cast per
        /// frame is both correct and what the cost is budgeted for. (The portal scope
        /// is not a second source: it never raises Applied, because it runs
        /// mid-LateUpdate where the game may still move the camera.)
        /// </summary>
        public void OnApplied(AppliedFrame frame)
        {
            if (Time.frameCount == _lastComputedFrame) return;
            _lastComputedFrame = Time.frameCount;

            if (!_crosshair.TryBind()) return;

            // A cast that cannot run is not the same answer as one that ran and put
            // the aim point off screen. With no mask there is no depth, so there is
            // nothing to compensate with - and hiding the crosshair would take away
            // the interaction affordance this whole game is played through, which is
            // worse than leaving it where the game itself draws it. Logged once,
            // because a crosshair that has quietly stopped compensating looks exactly
            // like one that never had to.
            if (!_aimTrace.HasMask)
            {
                // Reported only once the condition has PERSISTED. ResizeScript builds
                // its masks in Start, so the mask is legitimately unreadable for the
                // frames between the player rig being instantiated and that running -
                // logging on the first occurrence would put a permanent error at the
                // top of a healthy session's log and never retract it.
                if (_noMaskSince < 0f) _noMaskSince = Time.realtimeSinceStartup;
                if (!_loggedNoMask && Time.realtimeSinceStartup - _noMaskSince >= NoMaskGraceSeconds)
                {
                    _loggedNoMask = true;
                    if (_log != null)
                        _log("Aim mask unavailable - crosshair parallax compensation is off"
                             + " and the crosshair is left where the game draws it.");
                }
                Clear();
                return;
            }

            _noMaskSince = -1f;

            ReticlePlacement placement = Compute(frame);
            LastPlacement = placement;

            if (!IsDrawable(placement))
            {
                // Hidden, not centred. Tracking IS being applied here, so the
                // crosshair at centre would claim the player is pointing at whatever
                // sits in the middle of the frame - and it would be at its most
                // convincing exactly when it is most wrong, because centre is where an
                // uncompensated crosshair lives. This covers the aim point crossing
                // behind the tracked camera at around 90 degrees of head yaw and the
                // aim point merely leaving the frame before that. Neither has an
                // honest screen position.
                _crosshair.SetVisible(false);
                return;
            }

            _crosshair.SetVisible(true);

            // NDC scaled by the canvas rather than by Screen.width/height: the
            // crosshair lives on a uGUI canvas whose reference resolution is not the
            // window's, and anchoredPosition is in canvas units.
            Vector2 half = _crosshair.CanvasHalfExtent;
            _crosshair.SetOffset(new Vector2(placement.Ndc.x * half.x, placement.Ndc.y * half.y));
        }

        /// <summary>
        /// Puts the crosshair back at centre and makes sure it is visible. Called on
        /// every frame that applies no tracking, so a pause, a cutscene or a disabled
        /// mod does not leave it parked off to one side or hidden. Centre IS the right
        /// answer here, because with no tracking applied the view is the aim.
        /// </summary>
        public void Clear()
        {
            _crosshair.SetVisible(true);
            _crosshair.ClearOffset();
            LastPlacement = default(ReticlePlacement);
        }

        private static bool IsDrawable(ReticlePlacement placement)
        {
            return placement.Valid
                   && Mathf.Abs(placement.Ndc.x) <= MaxDrawableNdc
                   && Mathf.Abs(placement.Ndc.y) <= MaxDrawableNdc;
        }

        private ReticlePlacement Compute(AppliedFrame frame)
        {
            ReticlePlacement placement = default(ReticlePlacement);
            placement.EngineDelta = -1f;

            Vector3 cleanEye = frame.CleanEye;
            Vector3 aimDirection = frame.CleanForward;

            Vector2 ndc;
            AimResult aim = _aimTrace.Cast(cleanEye, aimDirection);
            if (aim.Hit)
            {
                placement.Hit = true;
                placement.Distance = aim.Distance;
                placement.AimPoint = aim.Point;
                placement.Valid = AimProjection.TryProjectPoint(frame.Camera, aim.Point, out ndc);
                placement.Ndc = ndc;

                Vector2 engineNdc;
                if (MeasureEngineDelta && placement.Valid
                    && AimProjection.TryEngineProjectPoint(frame.Camera, aim.Point, out engineNdc))
                {
                    float dx = placement.Ndc.x - engineNdc.x;
                    float dy = placement.Ndc.y - engineNdc.y;
                    placement.EngineDelta = Mathf.Sqrt(dx * dx + dy * dy);
                }
                return placement;
            }

            // A definite no-hit is a target at infinity, so the crosshair marks the
            // aim DIRECTION for that frame. A cast that could not run at all is
            // handled before Compute is reached, so this is a real miss.
            if (!aim.Valid) return placement;

            placement.Valid = AimProjection.TryProjectDirection(frame.Camera, aimDirection, out ndc);
            placement.Ndc = ndc;
            return placement;
        }
    }
}
