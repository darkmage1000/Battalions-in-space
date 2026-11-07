using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Damage you deal, shown where you dealt it: a number that floats up off the target (blue when it only came off
    /// armour, gold on a kill; quick hits on one target add up into one number) and, for a few seconds after a hit,
    /// the target's name with its armour and health bars over its head.
    /// </summary>
    public partial class HUD
    {
        class DmgNum { public IDamageable t; public Vector3 p; public float hp, ar, born; public bool kill; }
        static readonly List<DmgNum> dmgNums = new List<DmgNum>();
        static IDamageable hitTarget;
        static float hitTargetAt;

        public static void Damage(IDamageable t, Vector3 p, float hp, float ar, bool kill)
        {
            if (t == null || (hp + ar < 0.5f && !kill)) return;
            float now = Time.unscaledTime;
            hitTarget = kill ? null : t; hitTargetAt = now;
            foreach (var n in dmgNums)
                if (n.t == t && now - n.born < 0.4f && !n.kill) { n.hp += hp; n.ar += ar; n.kill |= kill; n.born = now; n.p = p; return; }
            dmgNums.Add(new DmgNum { t = t, p = p, hp = hp, ar = ar, born = now, kill = kill });
            if (dmgNums.Count > 16) dmgNums.RemoveAt(0);
        }

        static void ClearDamage() { dmgNums.Clear(); hitTarget = null; }

        void DrawDamage(Camera cam)
        {
            float now = Time.unscaledTime;
            for (int i = dmgNums.Count - 1; i >= 0; i--)
            {
                var n = dmgNums[i];
                float age = now - n.born;
                if (age > 1.1f) { dmgNums.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(n.p + Vector3.up * (0.5f + age * 1.4f));
                if (sp.z <= 0) continue;
                Vector2 g = new Vector2(sp.x / UI.S, (Screen.height - sp.y) / UI.S);
                int total = Mathf.RoundToInt(n.hp + n.ar);
                bool armourOnly = n.hp < 0.5f && n.ar > 0;
                Color c = n.kill ? UI.Gold : armourOnly ? ArmorCol : Color.white;
                c.a = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.65f, 1.1f, age));
                int size = total >= 150 ? 30 : total >= 60 ? 25 : 21;
                float pop = age < 0.08f ? 1.25f : 1f;
                UI.Text(new Rect(g.x - 80, g.y - 20, 160, 40), n.kill ? $"{total}  KILL" : total.ToString(), Mathf.RoundToInt(size * pop), c, TextAnchor.MiddleCenter);
            }

            // the last thing you hit: name, armour and health
            if (hitTarget == null || now - hitTargetAt > 3f || !(hitTarget is Component comp) || !comp || !hitTarget.Alive) return;
            var tu = hitTarget as Unit; var tv = hitTarget as Vehicle;
            Vector3 top = tu ? tu.transform.position + Vector3.up * (tu.Height + 0.55f) : tv.transform.position + Vector3.up * (tv.model != null ? tv.model.bounds.max.y + 1f : 3f);
            Vector3 s2 = cam.WorldToScreenPoint(top);
            if (s2.z <= 0) return;
            Vector2 q = new Vector2(s2.x / UI.S, (Screen.height - s2.y) / UI.S);
            float fade = 1 - Mathf.InverseLerp(2.4f, 3f, now - hitTargetAt);
            const float W = 130;
            string nm = tu ? (tu.part.boss ? "WARLORD" : tu.displayName) : tv.def.name;
            UI.Text(new Rect(q.x - 120, q.y - 34, 240, 22), nm, 15, new Color(1, 1, 1, 0.9f * fade), TextAnchor.MiddleCenter);
            float hpF = tu ? tu.hp / tu.maxHp : tv.hp / tv.maxHp;
            if (tu && tu.armor > 0)
            {
                UI.Fill(new Rect(q.x - W / 2 - 1, q.y - 13, W + 2, 6), new Color(0, 0, 0, 0.6f * fade));
                UI.Fill(new Rect(q.x - W / 2, q.y - 12, W * Mathf.Clamp01(tu.armor / Unit.MaxArmor), 4), new Color(ArmorCol.r, ArmorCol.g, ArmorCol.b, fade));
            }
            UI.Fill(new Rect(q.x - W / 2 - 1, q.y - 6, W + 2, 9), new Color(0, 0, 0, 0.6f * fade));
            Color hc = Color.Lerp(UI.Bad, UI.Good, hpF); hc.a = fade;
            UI.Fill(new Rect(q.x - W / 2, q.y - 5, W * Mathf.Clamp01(hpF), 7), hc);
        }
    }
}
