using System;
using System.Reflection;
using UnityEngine;

namespace SuperliminalHeadTracking.Game
{
    /// <summary>
    /// Works out which physics layers can stop the rendered eye, so a lean is cut
    /// back by walls and by nothing else.
    ///
    /// The answer is not a list of layer names. Superliminal's layer table is 30
    /// entries deep and most of them - Fakeout, NoClipCamera, Stop Projection,
    /// LightCullingLayer, PortalHitTest, GeneralUse0..4 - are set in the editor and
    /// never named in code, so nothing in the binary says which ones are physically
    /// solid. Guessing from names is how a lean ends up refused by a portal plane in
    /// an open doorway.
    ///
    /// What does say, mechanically, is Unity's own layer collision matrix: the eye
    /// sits inside the player's CharacterController capsule, so what should stop the
    /// eye is exactly what stops the player. Physics.GetIgnoreLayerCollision is read
    /// through reflection because the shared build stubs do not declare it; it is
    /// called once per layer when the mask is first built, never on a frame path.
    ///
    /// The result is intersected with ResizeScript's own _layerMaskIgnoreGrabbed, so
    /// the object in the player's hands is still never what stops the lean.
    /// </summary>
    public static class SolidGeometryMask
    {
        private static MethodInfo _getIgnoreLayerCollision;
        private static bool _resolved;

        /// <summary>What the last Build made of the layer collision matrix.</summary>
        public enum Narrowing
        {
            /// <summary>The matrix was read and cut layers out of the grab mask.</summary>
            Narrowed,

            /// <summary>
            /// The matrix was read fine and permits the Player layer to collide with
            /// every layer, so it distinguishes nothing. Unity's default matrix has
            /// every box ticked, so a project that never edited it lands here - which
            /// is a different thing from a reflection failure and wants a different
            /// fix.
            /// </summary>
            MatrixPermitsEveryLayer,

            /// <summary>The matrix could not be read at all.</summary>
            Unreadable,

            /// <summary>
            /// The matrix was never consulted because ResizeScript's own grab mask did
            /// not read, so there was nothing to narrow. The lean clamp has no mask at
            /// all in this state, which is a bigger loss than a wide one.
            /// </summary>
            NoGrabMask,

            /// <summary>
            /// The matrix was never consulted because the game has no Player layer to
            /// narrow against. Distinct from Unreadable: nothing is wrong with the
            /// reflection, the input was not there.
            /// </summary>
            NoPlayerLayer
        }

        /// <summary>
        /// How the last Build got on. Worth saying in the log either way: a clamp
        /// stopping on more than it should looks the same as one that is working.
        /// </summary>
        public static Narrowing LastNarrowing { get; private set; }

        /// <summary>
        /// Builds the sweep mask from <paramref name="grabMask"/>, which is
        /// ResizeScript._layerMaskIgnoreGrabbed.
        /// </summary>
        public static int Build(int grabMask)
        {
            LastNarrowing = Narrowing.NoGrabMask;
            if (grabMask == 0) return 0;

            LastNarrowing = Narrowing.NoPlayerLayer;
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0) return grabMask;

            LastNarrowing = Narrowing.Unreadable;
            MethodInfo method = ResolveMethod();
            if (method == null) return grabMask;

            int collidable = 0;
            object[] args = new object[2];
            for (int layer = 0; layer < 32; layer++)
            {
                args[0] = playerLayer;
                args[1] = layer;
                if (!(bool)method.Invoke(null, args))
                    collidable |= 1 << layer;
            }

            // Nothing collidable cannot be a real answer - the player walks on the
            // floor - so it is a read that went wrong. Everything collidable IS a real
            // answer, and a common one, since Unity's default matrix ticks every box;
            // it just narrows nothing. Both fall back to the grab mask alone, and they
            // are reported apart because only one of them is a fault.
            if (collidable == 0) return grabMask;
            if (collidable == ~0)
            {
                LastNarrowing = Narrowing.MatrixPermitsEveryLayer;
                return grabMask;
            }

            LastNarrowing = Narrowing.Narrowed;
            return grabMask & collidable;
        }

        private static MethodInfo ResolveMethod()
        {
            if (_resolved) return _getIgnoreLayerCollision;
            _resolved = true;

            Type physics = typeof(Physics);
            _getIgnoreLayerCollision = physics.GetMethod(
                "GetIgnoreLayerCollision",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(int), typeof(int) },
                null);

            return _getIgnoreLayerCollision;
        }
    }
}
