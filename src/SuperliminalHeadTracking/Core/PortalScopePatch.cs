using System;
using System.Reflection;
using HarmonyLib;
using SuperliminalHeadTracking.Game;

namespace SuperliminalHeadTracking.Core
{
    /// <summary>
    /// Wraps Superliminal's portal pass in the head-tracked camera pose.
    ///
    /// PortalManager.LateUpdate builds the portal visibility tree, culls it with
    /// GeometryUtility.CalculateFrustumPlanes against the camera's own matrices, and
    /// positions every portal camera through PortalHelper.PlacePortalCamera, which
    /// reads sourceCamera.transform. All of that happens before any render callback
    /// fires. Left alone, a frame drawn through a head-turned view would show portals
    /// chosen and aimed for the view the game intended: the ones at the edge of the
    /// turned-toward side get culled away, and the ones that survive render the wrong
    /// slice of the room behind them.
    ///
    /// [DefaultExecutionOrder] is no help - it is baked into a build's script
    /// execution order table and an assembly loaded into a shipped game is never in
    /// that table - so the pose is opened around the method itself.
    ///
    /// The scope is closed by a FINALIZER rather than a postfix, because a postfix
    /// does not run when the original throws. Unity catches an exception out of a
    /// MonoBehaviour's LateUpdate and carries on, so that path is reachable, and
    /// leaving the scope open on it is not self-correcting: the render hook's
    /// TransformFrameState has its own save slot and no knowledge of the scope's
    /// write, so its BeginFrame would capture the already-tracked transform as the
    /// frame's clean pose, compose tracking on top of it, and restore to THAT at
    /// onPostRender - leaving the game reading a head-turned camera for the rest of
    /// the frame. The next frame's close would then write the throwing frame's saved
    /// world position onto the camera. The finalizer returns void, so the game's own
    /// exception propagates unchanged.
    /// </summary>
    public static class PortalScopePatch
    {
        private static CameraRig.CameraRig _rig;

        /// <summary>
        /// Patches PortalManager.LateUpdate. Returns false when the type or the method
        /// is not there, which is a real failure rather than a shrug: without the
        /// scope the portal graph is culled and its cameras placed from the untracked
        /// camera, so portals drop out at the edge of the turned-toward side and the
        /// survivors render the wrong slice of the room.
        /// </summary>
        public static bool TryApply(Harmony harmony, CameraRig.CameraRig rig, Action<string> log)
        {
            Type portalManager = GameReflection.PortalManagerType;
            if (portalManager == null)
            {
                log("PortalManager not found - the portal scope is not installed.");
                return false;
            }

            MethodInfo target = portalManager.GetMethod(
                "LateUpdate", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (target == null)
            {
                log("PortalManager.LateUpdate not found - the portal scope is not installed.");
                return false;
            }

            _rig = rig;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(PortalScopePatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static)),
                finalizer: new HarmonyMethod(typeof(PortalScopePatch).GetMethod(
                    "Finalizer", BindingFlags.NonPublic | BindingFlags.Static)));

            log("Portal scope installed on PortalManager.LateUpdate.");
            return true;
        }

        /// <summary>
        /// Drops the rig the patch holds. Called after UnpatchSelf, so the static does
        /// not keep a destroyed plugin's camera rig alive for the rest of the process.
        /// </summary>
        public static void Release()
        {
            _rig = null;
        }

        private static void Prefix()
        {
            if (_rig != null) _rig.OpenScope();
        }

        private static void Finalizer()
        {
            if (_rig != null) _rig.CloseScope();
        }
    }
}
