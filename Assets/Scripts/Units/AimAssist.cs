using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Soft aim assist for the player. Three parts, scaled by Settings.AimAssist (Off / Low / Medium / High):
    /// friction (look slows while the crosshair is on an enemy), pull (the view drifts gently toward an enemy
    /// near the crosshair) and magnetism (shots go to an enemy inside a small cone, with lead).
    /// On for touch and gamepad; for the mouse only when Settings.MouseAssist is switched on.
    /// Targets must be in line of sight; the most centred, nearest one wins.
    /// </summary>
    public static class AimAssist
    {
        public static readonly string[] Names = { "Off", "Low", "Medium", "High" };
        static readonly float[] magnet = { 0, 2.5f, 4.5f, 7f };     // degrees: shots bend to a target inside this cone
        static readonly float[] pull = { 0, 6f, 10f, 15f };         // degrees: the view drifts toward targets inside this cone
        static readonly float[] pullRate = { 0, 8f, 18f, 32f };     // degrees per second
        static readonly float[] friction = { 1, 0.8f, 0.62f, 0.45f }; // look speed while on target

        static int Level => Mathf.Clamp(Settings.AimAssist, 0, 3);
        public static bool On => Level > 0 && (Settings.Current != Settings.InputDevice.KeyboardMouse || Settings.MouseAssist);
        // touch screens get wider cones and a stronger pull than a gamepad; the mouse (if enabled) the gentlest
        static float DeviceMul => Settings.Current == Settings.InputDevice.Touch ? 1.6f : Settings.Current == Settings.InputDevice.Gamepad ? 1f : 0.6f;
        public static float MagnetCone => magnet[Level] * DeviceMul;
        public static float PullCone => pull[Level] * DeviceMul;
        public static float PullRate => pullRate[Level] * DeviceMul;
        public static float Friction => Mathf.Lerp(1, friction[Level], Mathf.Clamp01(DeviceMul));

        /// <summary>
        /// Best enemy near the view direction: lowest (angle + distance share), visible from eye.
        /// point = where to aim (lead for the shot speed); angle = degrees off the view direction.
        /// </summary>
        public static IDamageable Find(Battle b, int team, Vector3 eye, Vector3 dir, float cone, float range, float shotSpeed,
                                       Transform ignore, bool infantry, bool vehicles, bool shipsOnly, out Vector3 point, out float angle)
        {
            Vector3 bp = Vector3.zero; float ba = 999;
            IDamageable best = null; float bestScore = float.MaxValue;
            if (infantry)
                foreach (var u in b.units)
                {
                    if (!u || !u.Alive || u.team == team || u.InVehicle || u.Cloaked) continue;
                    Consider(u, u.grounded ? new Vector3(u.velocity.x, 0, u.velocity.z) : u.velocity);
                }
            if (vehicles)
                foreach (var v in b.vehicles)
                {
                    if (!v || !v.Alive || v.Team == team || v.IsEmpty) continue;
                    if (shipsOnly && !v.IsShip) continue;
                    Consider(v, v.vel);
                }
            point = bp; angle = ba;
            return best;

            void Consider(IDamageable t, Vector3 vel)
            {
                Vector3 p = t.AimPoint;
                Vector3 to = p - eye;
                float d = to.magnitude;
                if (d < 1 || d > range) return;
                float a = Vector3.Angle(dir, to);
                if (a > cone) return;
                float score = a + d / range * cone * 0.6f;
                if (score >= bestScore) return;
                // line of sight
                if (Physics.Raycast(eye, to / d, out var hit, d - 0.5f, Layers.SightMask | (1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                {
                    if (!(ignore && hit.collider.transform.IsChildOf(ignore)) && hit.collider.GetComponentInParent<IDamageable>() != t) return;
                }
                bestScore = score; best = t; ba = a;
                float time = shotSpeed > 1 ? d / shotSpeed : 0;
                bp = p + vel * time * 0.9f;
            }
        }

        /// <summary>Turn a yaw / pitch pair toward a point at a limited rate.</summary>
        public static void PullView(ref float yaw, ref float pitch, Vector3 eye, Vector3 point, float rate, float dt)
        {
            Vector3 to = point - eye;
            if (to.sqrMagnitude < 0.01f) return;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, rate * dt);
            pitch = Mathf.MoveTowards(pitch, wantPitch, rate * dt);
        }
    }
}
