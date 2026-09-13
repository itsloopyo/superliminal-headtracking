using System;
using CameraUnlock.Core.Unity.Tracking;
using UnityEngine;

namespace SuperliminalHeadTracking.CameraRig
{
    /// <summary>
    /// The camera pose for the frame being drawn: what the game asked for, and what
    /// the player is actually looking through.
    /// </summary>
    public struct AppliedFrame
    {
        public Camera Camera;
        public Vector3 CleanEye;
        public Quaternion CleanRotation;
        public Vector3 TrackedEye;
        public Quaternion TrackedRotation;

        public Vector3 CleanForward { get { return CleanRotation * Vector3.forward; } }
        public Vector3 TrackedUp { get { return TrackedRotation * Vector3.up; } }
        public Vector3 TrackedForward { get { return TrackedRotation * Vector3.forward; } }
    }

    /// <summary>
    /// Owns the one place the head pose reaches Superliminal's camera, and the one
    /// place it is taken back off again.
    ///
    /// Head tracking only changes what the player sees. Every read the game makes of
    /// the camera - ResizeScript's grab ray, its perceived-size projection, the
    /// interaction sweep, physics - happens in Update, FixedUpdate or LateUpdate, and
    /// the camera is clean in all three. The tracked pose exists only between
    /// <see cref="OnPreCull"/> and <see cref="OnPostRender"/>, which is the rendering
    /// window, plus the portal scope below.
    ///
    /// The portal scope is a second, narrower window and it is not optional.
    /// PortalManager.LateUpdate walks the portal graph, culls it against
    /// <c>camera.worldToCameraMatrix</c>, and positions every portal camera from
    /// <c>sourceCamera.transform</c> (PortalHelper.PlacePortalCamera). It runs before
    /// any render callback, so without this the frame is drawn through a head-turned
    /// view while the portals in it were selected and aimed for the view the game
    /// intended: portals near the edge of the turned-toward side drop out, and the
    /// ones that survive show the wrong slice of the room. A BepInEx plugin cannot get
    /// in front of that with [DefaultExecutionOrder] - the attribute is baked into a
    /// build's script execution order and an assembly loaded after the fact is never
    /// in that table - so the scope is opened by a Harmony prefix on the method itself
    /// and closed by its postfix.
    /// </summary>
    public sealed class CameraRig
    {
        private TransformFrameState _frameState = new TransformFrameState();
        private readonly Func<Camera> _cameraResolver;

        private Camera _appliedCamera;
        private HeadPose _pose;
        private bool _hasPose;

        // A camera parented to the player camera draws the player's view as well, and
        // gets the pose written to ITSELF rather than inherited from the parent. See
        // OnPreCull for why it cannot be inherited.
        private Camera _childCamera;
        private Vector3 _childPosition;
        private Quaternion _childRotation;
        private int _childFrame = -1;

        // The portal scope's own save slot, deliberately separate from _frameState.
        // Sharing it would let the scope's LateUpdate-time capture become the frame's
        // "clean" pose, and any camera move made by a game LateUpdate ordered AFTER
        // PortalManager - a camera shake, a mantle animation, a scripted lerp - would
        // then be both rendered from the wrong place and permanently undone by the
        // restore. The scope saves and puts back exactly what it found.
        private Camera _scopeCamera;
        private Vector3 _scopePosition;
        private Quaternion _scopeRotation;

        /// <summary>Yaw about world up rather than about the camera's own up.</summary>
        public bool WorldSpaceYaw { get; set; }

        /// <summary>
        /// Raised immediately after the render hook has written the camera, with both
        /// poses of the frame. Everything that has to agree with the drawn view - the
        /// reticle, the diagnostics line - reads it from here rather than recomputing,
        /// so there is no second derivation that can disagree with the first. The
        /// portal scope does not raise it: the scope runs mid-LateUpdate, where the
        /// game may still move the camera before the frame is drawn.
        /// </summary>
        public event Action<AppliedFrame> Applied;

        public HeadPose Pose { get { return _pose; } }

        public CameraRig(Func<Camera> cameraResolver)
        {
            if (cameraResolver == null) throw new ArgumentNullException("cameraResolver");
            _cameraResolver = cameraResolver;
            _pose = HeadPose.Zero;
            WorldSpaceYaw = true;
        }

        public void Enable()
        {
#if IL2CPP
            _preCull = new Il2Cpp.CameraCallbacks("onPreCull", OnPreCull);
            _postRender = new Il2Cpp.CameraCallbacks("onPostRender", OnPostRender);
#else
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
#endif
        }

        public void Disable()
        {
#if IL2CPP
            _preCull.Remove();
            _postRender.Remove();
#else
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
#endif
            ClearPose();
        }

#if IL2CPP
        private Il2Cpp.CameraCallbacks _preCull;
        private Il2Cpp.CameraCallbacks _postRender;
#endif

        /// <summary>
        /// Hands the rig the pose to draw this frame with. Called once per frame from
        /// the plugin's Update, after the tracking pipeline has run and before the
        /// portal pass in PortalManager.LateUpdate reads it.
        /// </summary>
        public void SetPose(HeadPose pose)
        {
            _pose = pose;
            _hasPose = true;
        }

        /// <summary>Stops applying anything until the next <see cref="SetPose"/>.</summary>
        public void ClearPose()
        {
            _pose = HeadPose.Zero;
            _hasPose = false;
            CloseChild();
            CloseScope();
            Unwind();
        }

        /// <summary>
        /// Opens the tracked window around Superliminal's portal pass. Paired with
        /// <see cref="CloseScope"/>; see the class remarks for why this exists.
        /// </summary>
        public void OpenScope()
        {
            if (_scopeCamera != null) return;
            if (!_hasPose) return;

            Camera cam = _cameraResolver();
            if (cam == null) return;

            Transform tr = cam.transform;
            _scopeCamera = cam;
            _scopePosition = tr.position;
            _scopeRotation = tr.rotation;

            Vector3 trackedEye;
            Quaternion trackedRotation;
            _pose.Compose(_scopePosition, _scopeRotation, WorldSpaceYaw,
                out trackedEye, out trackedRotation);

            tr.rotation = trackedRotation;
            tr.position = trackedEye;
        }

        public void CloseScope()
        {
            if (_scopeCamera == null) return;

            Transform tr = _scopeCamera.transform;
            tr.rotation = _scopeRotation;
            tr.position = _scopePosition;
            _scopeCamera = null;
        }

        /// <summary>
        /// Applies the pose to every camera that draws the player's view, which is the
        /// player camera plus anything parented to it, each in its own
        /// cull-render-restore window.
        ///
        /// Superliminal draws the grab outline with a second camera,
        /// PlayerControllerPrefab/Main Camera/OutLinerCamera, which renders the held
        /// object white into an R8 render texture that a screen-space blit on the main
        /// camera then dilates into the outline. It sits at the same depth as the main
        /// camera and Unity renders it FIRST, so applying only to the player camera
        /// left the mask drawn from the clean pose while the frame it is composited
        /// over was drawn from the tracked one. The outline stayed where the object had
        /// been before the head turned - measured at a 25 degree head yaw, the outliner
        /// culled at 74 degrees while the main camera rendered at 99.
        ///
        /// A child is composed against its OWN transform, not moved by moving the
        /// parent. Both are the same thing while the outliner sits at local identity,
        /// which is where ResizeScript's LateUpdate puts it, but they part company the
        /// moment a held object crosses a portal: ResizeScript then places the outliner
        /// on the far side of the portal, and turning the parent would swing that
        /// offset around the player's eye by the whole head rotation, over a distance
        /// as long as the portal pair is apart. Composing on the child's own transform
        /// is exact either way, because the head offset is applied in whatever frame
        /// the camera is already in.
        /// </summary>
        private void OnPreCull(Camera cam)
        {
            if (!_hasPose) return;
            Camera player = _cameraResolver();
            if (player == null) return;

            if (cam == player)
            {
                Apply(player);
                return;
            }

            if (IsDescendantOf(cam.transform, player.transform)) ApplyChild(cam);
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is under <paramref name="ancestor"/>,
        /// which is what decides whether a camera renders the player's view.
        ///
        /// Walked here rather than through Transform.IsChildOf, which the shared build
        /// stubs do not declare - the build resolves every engine reference from those
        /// stubs, so calling it would compile against a game install and fail in CI.
        /// The chain walk is the same test: IsChildOf is true for the transform itself
        /// as well as its descendants, and the cam == player case is handled above
        /// either way.
        /// </summary>
        private static bool IsDescendantOf(Transform candidate, Transform ancestor)
        {
            for (Transform t = candidate; t != null; t = t.parent)
            {
                if (t == ancestor) return true;
            }
            return false;
        }

        /// <summary>
        /// Puts a camera back at the end of the render that applied it, so each camera
        /// gets its own window rather than one window spanning the whole stack.
        ///
        /// The alternative - hold the pose from the first child's cull until the player
        /// camera's post-render - breaks on a camera rendered by hand. Superliminal
        /// parents a disabled TeleportCamera to the player camera and drives several
        /// cameras through Camera.Render(), which runs cull, render and post-render
        /// synchronously wherever it is called from. Under one long window a Render()
        /// from an Update would leave the player camera turned for the whole of the
        /// rest of that frame's game logic, which is the one thing this mod must never
        /// do.
        /// </summary>
        private void OnPostRender(Camera cam)
        {
            if (_childCamera != null && cam == _childCamera)
            {
                CloseChild();
                return;
            }

            if (_appliedCamera == null || cam != _appliedCamera) return;
            Restore();
        }

        private void ApplyChild(Camera cam)
        {
            // One child at a time. A second child camera in the same frame gets its own
            // window as soon as the first has closed at its own post-render, which is
            // the order Unity renders them in; only a render nested inside another
            // camera's render would be skipped.
            if (_childCamera != null && _childFrame == Time.frameCount) return;
            CloseChild();

            Transform tr = cam.transform;
            _childCamera = cam;
            _childFrame = Time.frameCount;
            _childPosition = tr.position;
            _childRotation = tr.rotation;

            Vector3 trackedEye;
            Quaternion trackedRotation;
            _pose.Compose(_childPosition, _childRotation, WorldSpaceYaw,
                out trackedEye, out trackedRotation);

            tr.rotation = trackedRotation;
            tr.position = trackedEye;
        }

        private void CloseChild()
        {
            if (_childCamera == null) return;

            // Only put it back within the frame that moved it. A child that culled but
            // never rendered leaves a save belonging to a frame that has gone, and
            // ResizeScript rewrites the outliner's transform every LateUpdate, so
            // writing the stale value back would clobber what the game has since put
            // there.
            if (_childFrame == Time.frameCount)
            {
                Transform tr = _childCamera.transform;
                tr.rotation = _childRotation;
                tr.position = _childPosition;
            }
            _childCamera = null;
        }

        private void Apply(Camera cam)
        {
            // A camera switch with the offset still applied would otherwise leave the
            // old camera holding it, where nothing restores it again.
            //
            // Unwinding is all this frame does. Unwind's own BeginFrame stamps the OLD
            // camera's transform as this frame's clean capture, so composing the new
            // camera against it would draw the new camera from the old camera's place
            // and then "restore" the old camera's transform onto it. The next frame
            // captures the new camera cleanly, and a switch is a level load or a rig
            // rebuild, where a frame without tracking is not visible.
            // Tested by reference, not with Unity's ==. The ordinary way the camera
            // changes here is a level load, which DESTROYS the old one, and Unity's ==
            // reports a destroyed object as null - so the == form skips this branch on
            // exactly the switch it exists to catch, and falls through to BeginFrame
            // with the dead camera's transform still marked modified. BeginFrame would
            // then write that transform onto the new camera and capture it as the
            // frame's clean base, drawing the new camera from the old rig's place.
            if (!ReferenceEquals(_appliedCamera, null) && _appliedCamera != cam)
            {
                // A destroyed camera has no transform left to put back, so the state
                // is dropped rather than unwound onto it.
                if (_appliedCamera == null) _frameState = new TransformFrameState();
                else Unwind();
                _appliedCamera = null;
                return;
            }

            Transform tr = cam.transform;
            if (!_frameState.BeginFrame(tr, Time.frameCount)) return;

            _appliedCamera = cam;

            Vector3 cleanEye = _frameState.StoredPosition;
            Quaternion cleanRotation = _frameState.StoredRotation;

            Vector3 trackedEye;
            Quaternion trackedRotation;
            _pose.Compose(cleanEye, cleanRotation, WorldSpaceYaw, out trackedEye, out trackedRotation);

            _frameState.SetRotation(tr, trackedRotation);
            _frameState.SetPosition(tr, trackedEye);

            if (Applied == null) return;

            Applied(new AppliedFrame
            {
                Camera = cam,
                CleanEye = cleanEye,
                CleanRotation = cleanRotation,
                TrackedEye = trackedEye,
                TrackedRotation = trackedRotation
            });
        }

        private void Restore()
        {
            if (_appliedCamera == null) return;
            _frameState.Restore(_appliedCamera.transform, Time.frameCount);
        }

        /// <summary>
        /// Puts the camera back and forgets it, including when the modification was
        /// left over from an earlier frame - a camera that was culled but never
        /// rendered, or a shutdown between the two callbacks. The frame-matched
        /// restore declines in that case; BeginFrame on a later frame is what unwinds
        /// it, and it is the only path that can.
        /// </summary>
        private void Unwind()
        {
            if (_appliedCamera == null) return;
            Transform tr = _appliedCamera.transform;
            _frameState.Restore(tr, Time.frameCount);
            _frameState.BeginFrame(tr, Time.frameCount);
            _appliedCamera = null;
        }
    }
}
