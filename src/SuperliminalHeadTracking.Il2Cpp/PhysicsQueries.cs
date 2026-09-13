using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace SuperliminalHeadTracking.Il2Cpp
{
    internal static class PhysicsQueries
    {
        // Unity returns a one-byte bool. The generated proxy marshals four bytes,
        // making ignored and collidable layer pairs both read as true on this build.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte LayerCollisionQuery(int first, int second);

        private static readonly LayerCollisionQuery Query =
            Marshal.GetDelegateForFunctionPointer<LayerCollisionQuery>(
                IL2CPP.il2cpp_resolve_icall("UnityEngine.Physics::GetIgnoreLayerCollision"));

        public static bool GetIgnoreLayerCollision(int first, int second)
        {
            return Query(first, second) != 0;
        }
    }
}
