using System;
using System.Reflection;
using CameraUnlock.Core.Unity.Utilities;
using UnityEngine;

namespace SuperliminalHeadTracking.Game
{
    /// <summary>
    /// Cached access to the Superliminal types the mod reads. Everything here is
    /// resolved once and held in a static field; nothing on a per-frame path does a
    /// name lookup.
    ///
    /// Superliminal splits its code across two assemblies. GameManager, MouseLook and
    /// CharacterMotor are in Assembly-CSharp-firstpass; ResizeScript, PortalManager
    /// and DrawCursorScriptHand are in Assembly-CSharp. Neither is referenced at
    /// build time, so both are found by walking the loaded assemblies.
    /// </summary>
    public static class GameReflection
    {
        /// <summary>
        /// How long the types are given to appear before the mod stops waiting. The
        /// four assemblies involved load at different moments, so a miss on any one
        /// frame means nothing; a miss half a minute in means it is not there at all.
        /// </summary>
        private const float ResolveTimeoutSeconds = 30f;

        /// <summary>
        /// Retries that must actually have run before the deadline above counts. See
        /// ReportPending for why elapsed time alone is not enough in this game.
        /// </summary>
        private const int ResolveMinAttempts = 600;

        private const string PhotonName = "Photon.Pun.PhotonNetwork";

        private static bool _resolved;
        private static bool _dormant;
        private static float _firstAttemptTime = -1f;
        private static int _attempts;
        private static Type _gameManagerType;
        private static Action<string> _log;
        private static Action<string> _logError;

        private static FieldInfo _gmInstance;
        private static FieldInfo _gmPlayerCamera;
        private static FieldInfo _gmPm;
        private static PropertyInfo _pmCanControl;

        private static PropertyInfo _photonIsConnected;
        private static PropertyInfo _photonInRoom;
        private static bool _photonTypeFound;

        private static Type _resizeScriptType;
        private static FieldInfo _resizeLayerMaskIgnorePlayer;
        private static FieldInfo _resizeLayerMaskIgnoreGrabbed;

        private static Type _portalHitTestLinkType;
        private static FieldInfo _portalHitTestLinkPortal;
        private static PropertyInfo _portalCanProjectThroughMe;
        private static MethodInfo _portalHelperTransformRay;
        private static MethodInfo _portalHelperFromToScale;

        // Reused so following a portal does not allocate the argument array itself on
        // every frame the crosshair sits on a portal. The boxing of the Ray in and out
        // of Invoke, and of the bool out of GetValue, is not avoided by this and is
        // the price of reaching the game's own maths by reflection.
        private static readonly object[] _transformRayArgs = new object[2];
        private static readonly object[] _portalArg = new object[1];

        /// <summary>All the game types the mod needs were found.</summary>
        public static bool Resolved { get { return _resolved; } }

        /// <summary>
        /// A resolve attempt found GameManager but not what it needs off it, so no
        /// later attempt can succeed either. Latched so the plugin's per-frame retry
        /// stops rather than re-deciding, and re-reporting, the same answer every
        /// frame for the rest of the session.
        /// </summary>
        public static bool Dormant { get { return _dormant; } }

        /// <summary>The PortalManager type, or null on a build without one.</summary>
        public static Type PortalManagerType { get; private set; }

        /// <summary>The DrawCursorScriptHand type that owns the crosshair canvas.</summary>
        public static Type CursorScriptType { get; private set; }

        public static void Initialize(Action<string> log, Action<string> logError)
        {
            _log = log;
            _logError = logError;
            if (_resolved || _dormant) return;

            _attempts++;
            if (_firstAttemptTime < 0f) _firstAttemptTime = Time.realtimeSinceStartup;

            // Each probe is skipped once it has answered, so a retry frame only walks
            // the assembly list for whatever is still missing.
            if (_gameManagerType == null) _gameManagerType = FindType("GameManager");
            if (_gameManagerType == null) { ReportPending("GameManager", true); return; }

            if (!ResolveGameManagerMembers(_gameManagerType)) return;

            if (_resizeScriptType == null)
            {
                _resizeScriptType = FindType("ResizeScript");
                if (_resizeScriptType != null)
                {
                    // Left null when the type is not int, so the mask reads below
                    // return 0 and the reticle and the lean clamp report themselves
                    // unavailable rather than throwing. Both already handle a mask
                    // they never got.
                    _resizeLayerMaskIgnorePlayer = IntField(_resizeScriptType, "_layerMaskIgnorePlayer");
                    _resizeLayerMaskIgnoreGrabbed = IntField(_resizeScriptType, "_layerMaskIgnoreGrabbed");
                }
            }

            if (PortalManagerType == null) PortalManagerType = FindType("PortalManager");
            if (CursorScriptType == null) CursorScriptType = FindType("DrawCursorScriptHand");
            if (_photonIsConnected == null) ResolvePhoton();
            if (!PortalTracingAvailable) ResolvePortalTracing();

            // GameManager is in Assembly-CSharp-firstpass, the three game types are in
            // Assembly-CSharp and PhotonNetwork is in PhotonUnityNetworking, so the
            // four arrive at four different moments and a miss on any one frame means
            // nothing at all. Keep retrying until they are in.
            //
            // Photon is tested FIRST and on its own, never as one entry in a list of
            // what is missing: it is the only fatal absence, and a list would let a
            // missing ResizeScript be the one reported, take the non-fatal path, and
            // latch with the multiplayer gate stuck closed - which is exactly the
            // state this is written to prevent.
            if (!PhotonResolved)
            {
                // Fatal because IsMultiplayer fails CLOSED: without it the mod reports
                // Multiplayer for the rest of the session and never applies a pose, so
                // dormant-with-a-reason is the honest version of the same outcome.
                ReportPending(_photonTypeFound ? PhotonName + ".IsConnected/InRoom" : PhotonName,
                    true);
                return;
            }

            // The other three each have a written degradation path - an uncompensated
            // crosshair, an unbound crosshair, an uninstalled portal scope - so losing
            // one to a future rename must not cost the player head tracking as well.
            //
            // The cost of waiting for them, taken knowingly: on a build that HAS
            // renamed one, nothing tracks until the deadline passes, because Resolved
            // is what the state detector gates on. On a healthy build every type is in
            // within the first level load and the deadline is never reached, so this
            // is a one-off delay on an already-broken build rather than a startup cost
            // anybody normally pays.
            string missing = DescribeMissing();
            if (missing != null) { ReportPending(missing, false); return; }

            Latch();
        }

        private static bool PhotonResolved
        {
            get { return _photonIsConnected != null && _photonInRoom != null; }
        }

        /// <summary>
        /// Binds the GameManager members and checks their types. A mismatch here is
        /// dormancy rather than a retry: the name is present and carries something
        /// else, which no later frame changes, and every read casts to a concrete type
        /// so letting it through would throw on the frame path instead.
        /// </summary>
        private static bool ResolveGameManagerMembers(Type gameManager)
        {
            if (_pmCanControl != null) return true;

            _gmInstance = gameManager.GetField("GM", BindingFlags.Public | BindingFlags.Static);
            _gmPlayerCamera = gameManager.GetField("playerCamera", BindingFlags.Public | BindingFlags.Instance);
            _gmPm = gameManager.GetField("PM", BindingFlags.Public | BindingFlags.Instance);

            if (_gmInstance == null || _gmPlayerCamera == null || _gmPm == null)
            {
                GoDormant("GameManager found but its fields did not resolve.");
                return false;
            }

            if (!typeof(UnityEngine.Object).IsAssignableFrom(gameManager))
            {
                GoDormant("GameManager is not a UnityEngine.Object, so the mod cannot tell"
                          + " a destroyed instance from a live one.");
                return false;
            }

            if (!gameManager.IsAssignableFrom(_gmInstance.FieldType)
                || !typeof(Camera).IsAssignableFrom(_gmPlayerCamera.FieldType))
            {
                GoDormant("GameManager.GM/playerCamera are not the types this mod reads ("
                          + _gmInstance.FieldType.Name + "/" + _gmPlayerCamera.FieldType.Name + ").");
                return false;
            }

            _pmCanControl = ReadableBoolProperty(_gmPm.FieldType, "canControl", BindingFlags.Instance);
            if (_pmCanControl == null)
            {
                GoDormant("GameManager.PlayerManager.canControl did not resolve as a readable bool.");
                return false;
            }

            return true;
        }

        private static void Latch()
        {
            _resolved = true;
            Log("Resolved Superliminal types"
                + " (ResizeScript=" + (_resizeScriptType != null)
                + ", PortalManager=" + (PortalManagerType != null)
                + ", DrawCursorScriptHand=" + (CursorScriptType != null)
                + ", PhotonNetwork=" + (_photonIsConnected != null)
                + ", PortalTracing=" + PortalTracingAvailable + ")");
        }

        /// <summary>
        /// The optional types the mod still wants and has not found, or null once they
        /// are all present. Every one of them is named rather than just the first, so
        /// a build that moved two does not report one and hide the other.
        /// </summary>
        private static string DescribeMissing()
        {
            string missing = null;
            if (_resizeScriptType == null) missing = "ResizeScript";
            if (PortalManagerType == null)
                missing = missing == null ? "PortalManager" : missing + ", PortalManager";
            if (CursorScriptType == null)
                missing = missing == null ? "DrawCursorScriptHand" : missing + ", DrawCursorScriptHand";
            return missing;
        }

        /// <summary>
        /// Leaves the resolve unlatched so the next frame tries again, until the
        /// deadline turns "has not loaded yet" into "is not in this build".
        ///
        /// The deadline is a retry count AND a wall-clock elapsed, because neither is
        /// enough on its own here. Superliminal never sets Application.runInBackground
        /// and pauses on focus loss, so the player loop stops while the window is in
        /// the background: on elapsed time alone a 40 second alt-tab during boot spends
        /// the whole budget across frames that never ran, and the mod latches on its
        /// first frame back having retried almost nothing.
        /// </summary>
        private static void ReportPending(string missing, bool fatal)
        {
            if (_attempts < ResolveMinAttempts) return;
            if (Time.realtimeSinceStartup - _firstAttemptTime < ResolveTimeoutSeconds) return;

            if (fatal)
            {
                GoDormant(missing + " did not resolve, so this is not a Superliminal build"
                          + " the mod recognises.");
                return;
            }

            // Not fatal, so the mod runs with that one feature off rather than not at
            // all. Said once, at error level, because a feature that has quietly
            // stopped working looks exactly like one that was never needed.
            LogError(missing + " did not resolve. Head tracking still runs; what is off is "
                     + DescribeLoss() + ".");
            Latch();
        }

        /// <summary>What the player loses for each optional type that is missing.</summary>
        private static string DescribeLoss()
        {
            string loss = null;
            if (_resizeScriptType == null)
                loss = "crosshair placement and the lean collision clamp";
            if (PortalManagerType == null)
                loss = Join(loss, "the portal scope, so portals may be culled and aimed from"
                                  + " the untracked camera while the head is turned");
            if (CursorScriptType == null)
                loss = Join(loss, "crosshair placement");
            return loss;
        }

        private static string Join(string existing, string addition)
        {
            return existing == null ? addition : existing + "; and " + addition;
        }

        private static void GoDormant(string reason)
        {
            _dormant = true;

            // Unbound, not just flagged. The members are assigned before they are
            // validated, and PlayerCamera / CanControl are read every frame by callers
            // that do not check Resolved - so leaving a half-bound set behind would
            // throw a NullReference or an InvalidCast out of the plugin's Update ten
            // times a second, which is the opposite of what the message below says.
            // Initialize returns at the top once _dormant is set, so nothing rebinds.
            _gmInstance = null;
            _gmPlayerCamera = null;
            _gmPm = null;
            _pmCanControl = null;

            LogError(reason + " The mod stays dormant and the game runs untouched.");
        }

        /// <summary>
        /// Superliminal ships Photon for its Battle Royale and co-op modes. Both run
        /// through PhotonNetwork, so its connection state is the whole multiplayer
        /// test - it is true from the moment the player enters the multiplayer menu's
        /// connect step, not just once a match starts.
        /// </summary>
        private static void ResolvePhoton()
        {
            Type photon = FindType(PhotonName);
            if (photon == null) return;

            // Recorded so the deadline can say which of the two failed. The type being
            // absent points at the game build; the type being present with unreadable
            // properties points at a Photon version bump, and those want different
            // answers from whoever reads the log.
            _photonTypeFound = true;

            _photonIsConnected = ReadableBoolProperty(photon, "IsConnected", BindingFlags.Static);
            _photonInRoom = ReadableBoolProperty(photon, "InRoom", BindingFlags.Static);
        }

        /// <summary>
        /// True when a portal's own ray transform can be used, so the aim cast can
        /// follow a look-ray through a portal the way the game's does.
        /// </summary>
        public static bool PortalTracingAvailable { get; private set; }

        /// <summary>
        /// Resolves what is needed to continue a ray through a portal.
        ///
        /// Not part of the resolve gate. This narrows the crosshair's depth when the
        /// player looks through a portal and does nothing at all otherwise, so a build
        /// that moved these members should lose the refinement rather than the mod.
        /// </summary>
        private static void ResolvePortalTracing()
        {
            PortalTracingAvailable = false;

            _portalHitTestLinkType = FindType("PortalHitTestLink");
            Type portalHelper = FindType("PortalHelper");
            Type portal = FindType("Portal");
            if (_portalHitTestLinkType == null || portalHelper == null || portal == null) return;
            if (!typeof(UnityEngine.Object).IsAssignableFrom(portal)) return;

            _portalHitTestLinkPortal = _portalHitTestLinkType.GetField(
                "Portal", BindingFlags.Public | BindingFlags.Instance);
            if (_portalHitTestLinkPortal == null
                || !portal.IsAssignableFrom(_portalHitTestLinkPortal.FieldType)) return;

            _portalCanProjectThroughMe = ReadableBoolProperty(
                portal, "CanProjectThroughMe", BindingFlags.Instance);
            _portalHelperTransformRay = portalHelper.GetMethod(
                "TransformRay", BindingFlags.Public | BindingFlags.Static,
                null, new[] { portal, typeof(Ray) }, null);
            _portalHelperFromToScale = portalHelper.GetMethod(
                "CalculateFromToPortalScale", BindingFlags.Public | BindingFlags.Static,
                null, new[] { portal }, null);

            PortalTracingAvailable = _portalCanProjectThroughMe != null
                                     && _portalHelperTransformRay != null
                                     && _portalHelperTransformRay.ReturnType == typeof(Ray)
                                     && _portalHelperFromToScale != null
                                     && _portalHelperFromToScale.ReturnType == typeof(float);
        }

        /// <summary>
        /// Continues an aim ray through the portal the given collider belongs to, if
        /// it belongs to one that can be seen through.
        ///
        /// The test is the presence of a PortalHitTestLink on the collider rather than
        /// its layer, which is the same answer - the link is only ever put on a
        /// portal's hit-test collider - and does not need GameObject.layer, which the
        /// shared build stubs do not declare.
        /// </summary>
        public static bool TryTransformRayThroughPortal(Collider hitCollider, Vector3 hitPoint,
            Vector3 direction, out Ray onward, out float farSideScale)
        {
            onward = default(Ray);
            farSideScale = 1f;
            if (!PortalTracingAvailable || hitCollider == null) return false;

            Component link = hitCollider.GetComponent(_portalHitTestLinkType);
            if (link == null) return false;

            var portal = (UnityEngine.Object)_portalHitTestLinkPortal.GetValue(link);
            if (portal == null) return false;
            if (!(bool)_portalCanProjectThroughMe.GetValue(portal, null)) return false;

            _transformRayArgs[0] = portal;
            _transformRayArgs[1] = new Ray(hitPoint, direction);
            onward = (Ray)_portalHelperTransformRay.Invoke(null, _transformRayArgs);
            _transformRayArgs[0] = null;
            _transformRayArgs[1] = null;

            // A portal with no destination transforms to a default Ray, whose zero
            // direction would make the next cast meaningless.
            if (onward.direction == Vector3.zero) return false;

            // Superliminal's portals scale, and the far side's distances are in the
            // far side's units. TransformRay scales the ORIGIN but not the direction,
            // so a segment of length L beyond the portal has a near-side pre-image of
            // L / scale - and the near-side length is the one that belongs on this
            // frame's aim ray.
            _portalArg[0] = portal;
            farSideScale = (float)_portalHelperFromToScale.Invoke(null, _portalArg);
            _portalArg[0] = null;

            return farSideScale > 0f;
        }

        /// <summary>
        /// Typed as a UnityEngine.Object rather than an object so the null checks on
        /// it get Unity's own == , which reports a destroyed instance as null. The
        /// resolve refuses to latch unless GameManager is a UnityEngine.Object, so
        /// this cast cannot fail.
        /// </summary>
        private static UnityEngine.Object GameManagerInstance
        {
            get { return _gmInstance == null ? null : (UnityEngine.Object)_gmInstance.GetValue(null); }
        }

        /// <summary>
        /// The camera the player looks through. Null outside a loaded level.
        ///
        /// Resolved once per frame. Every render callback asks for this - once per
        /// camera culled, and a portal level culls a lot of them - and each answer is
        /// two reflected field reads. The camera GameManager hands out cannot change
        /// between two culls of the same frame, so the first caller of the frame pays
        /// for the reflection and the rest read the cache.
        /// </summary>
        public static Camera PlayerCamera { get { return _playerCameraCache.Value; } }

        private static readonly PerFrameCache<Camera> _playerCameraCache =
            new PerFrameCache<Camera>(ResolvePlayerCamera);

        private static Camera ResolvePlayerCamera()
        {
            UnityEngine.Object gm = GameManagerInstance;
            return gm == null ? null : (Camera)_gmPlayerCamera.GetValue(gm);
        }

        /// <summary>
        /// The game's own "the player is driving" flag. Superliminal clears it for the
        /// pause menu, the escape menu, the options screen, level select, the credits,
        /// scripted camera moves (MOSTEventLockMovement), door transitions, the portal
        /// lerps and the dream editor UI, so it covers every non-gameplay state the
        /// mod would otherwise have to enumerate one at a time.
        /// </summary>
        public static bool CanControl
        {
            get
            {
                UnityEngine.Object gm = GameManagerInstance;
                if (gm == null) return false;
                object pm = _gmPm.GetValue(gm);
                if (pm == null) return false;
                return (bool)_pmCanControl.GetValue(pm, null);
            }
        }

        /// <summary>
        /// True from the moment the game connects to Photon, which covers the
        /// multiplayer menu, the lobby and a live Battle Royale or co-op match.
        /// Fails closed if Photon is somehow unreadable: an unknown multiplayer state
        /// has to read as multiplayer, or the mod would run in a session it must not.
        /// The resolve refuses to latch without Photon, so this guard is a backstop
        /// rather than a state the mod runs in.
        /// </summary>
        public static bool IsMultiplayer
        {
            get
            {
                if (_photonIsConnected == null || _photonInRoom == null) return true;
                return (bool)_photonIsConnected.GetValue(null, null)
                       || (bool)_photonInRoom.GetValue(null, null);
            }
        }

        /// <summary>
        /// The layer mask ResizeScript casts its own grab ray with, read off the live
        /// component rather than rebuilt from layer names. It is the game's own answer
        /// to "what does a look-ray stop on", which is exactly the surface the
        /// crosshair marks, so the reticle's depth comes from the same mask the game
        /// uses to decide what the player is pointing at. Returns 0 when the component
        /// is not in the scene yet.
        /// </summary>
        public static int GrabRayLayerMask(Component resizeScript)
        {
            if (resizeScript == null || _resizeLayerMaskIgnorePlayer == null) return 0;
            return (int)_resizeLayerMaskIgnorePlayer.GetValue(resizeScript);
        }

        /// <summary>
        /// The mask ResizeScript uses once it is holding something: the same set
        /// minus the Grabbed and NoInteractionWithAll layers. This is the lean
        /// clamp's mask, so a held object cannot be what stops the player leaning.
        /// Returns 0 when the component is not in the scene yet.
        /// </summary>
        public static int SolidWorldLayerMask(Component resizeScript)
        {
            if (resizeScript == null || _resizeLayerMaskIgnoreGrabbed == null) return 0;
            return (int)_resizeLayerMaskIgnoreGrabbed.GetValue(resizeScript);
        }

        /// <summary>
        /// The live ResizeScript, which Superliminal puts on the player camera. Null
        /// outside a loaded level.
        /// </summary>
        public static Component ResizeScript
        {
            get
            {
                if (_resizeScriptType == null) return null;
                Camera cam = PlayerCamera;
                return cam == null ? null : cam.GetComponent(_resizeScriptType);
            }
        }

        /// <summary>
        /// Names the layers set in a mask. Used once, when a mask is first read, so
        /// the log records what the game's own answer actually was rather than a
        /// bare integer nobody can check.
        /// </summary>
        public static string DescribeLayerMask(int mask)
        {
            var sb = new System.Text.StringBuilder();
            for (int layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) == 0) continue;
                string name = LayerMask.LayerToName(layer);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(string.IsNullOrEmpty(name) ? layer.ToString() : name)
                  .Append('(').Append(layer).Append(')');
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        /// <summary>
        /// Walks the loaded assemblies for a type by name. Superliminal's game code is
        /// split across Assembly-CSharp and Assembly-CSharp-firstpass and neither is a
        /// build-time reference, so the assembly a type lives in is not hardcoded here.
        /// </summary>
        private static Type FindType(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(typeName, false);
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>
        /// A public bool property that can actually be read, or null. The callers cast
        /// the value to bool, so a property of another type is no more use to them than
        /// a missing one and must not reach them.
        /// </summary>
        private static PropertyInfo ReadableBoolProperty(Type owner, string name, BindingFlags scope)
        {
            PropertyInfo property = owner.GetProperty(name, BindingFlags.Public | scope);
            return property != null && property.CanRead && property.PropertyType == typeof(bool)
                ? property
                : null;
        }

        /// <summary>A private instance int field, or null. Same reason.</summary>
        private static FieldInfo IntField(Type owner, string name)
        {
            FieldInfo field = owner.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(int) ? field : null;
        }

        private static void Log(string message)
        {
            if (_log != null) _log(message);
        }

        private static void LogError(string message)
        {
            if (_logError != null) _logError(message);
        }
    }
}
