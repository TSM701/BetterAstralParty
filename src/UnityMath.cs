using UnityEngine;

namespace BetterAstralParty;

// UnityEngine value math in managed code (2026-11-18). In the interop assemblies every Vector2/Vector3 constructor and
// operator, properties such as magnitude and every Mathf function are il2cpp_runtime_invoke calls whose value result the
// game boxes on its own heap: the field layouts made hundreds a frame, and most of the game's native collections started
// inside the mod's tick. These repeat Unity's own float math without a call.
internal static class UnityMath
{
    internal static Vector2 V2(float x, float y) { Vector2 v = default; v.x = x; v.y = y; return v; }
    internal static Vector3 V3(float x, float y, float z) { Vector3 v = default; v.x = x; v.y = y; v.z = z; return v; }
    internal static float Magnitude(Vector3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
    internal static Vector3 Sub(Vector3 a, Vector3 b) => V3(a.x - b.x, a.y - b.y, a.z - b.z);
    // Unity's Vector3 == zero: squared length under 1e-5 squared.
    internal static bool IsZero(Vector3 v) => v.x * v.x + v.y * v.y + v.z * v.z < 1e-5f * 1e-5f;

    // Mathf.DeltaAngle: the shortest turn from current to target, in (-180, 180].
    internal static float DeltaAngle(float current, float target)
    {
        var t = target - current;
        var turn = Math.Clamp(t - (float)Math.Floor(t / 360f) * 360f, 0f, 360f);
        return turn > 180f ? turn - 360f : turn;
    }
}
