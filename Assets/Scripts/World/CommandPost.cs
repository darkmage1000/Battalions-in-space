using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// A capturable command post. Infantry of one team inside the radius (with no enemies) first
    /// neutralises an enemy post, then captures it. Owned posts are spawn points and spawn vehicles.
    /// </summary>
    public class CommandPost : MonoBehaviour
    {
        public class Pad
        {
            public VKind kind;
            public Vector3 pos;
            public Quaternion rot;
            public Vehicle vehicle;
            public float timer;
        }

        public Battle battle;
        public int index;
        public string postName;
        public int owner = -1;
        public int capTeam = -1;          // team the progress belongs to
        public float progress;            // 0..1
        public float radius = 10f;
        public bool contested;
        public readonly int[] present = new int[2];
        public readonly List<Pad> pads = new List<Pad>();
        Transform holo;
        Renderer ringR, holoR, beamR;
        float spin;
        public Vector3 Pos => transform.position;

        public void Init(Battle b, int i, string n, int startOwner, Vector3 p)
        {
            battle = b; index = i; postName = n; owner = startOwner;
            capTeam = owner; progress = owner >= 0 ? 1 : 0;
            var k = new MeshKit();
            // pole + emitter
            k.Cyl(new Vector3(0, 2.2f, 0), 0.18f, 4.4f, Quaternion.identity, new Color(0.3f, 0.3f, 0.33f), 8);
            k.Cyl(new Vector3(0, 4.5f, 0), 0.6f, 0.3f, Quaternion.identity, new Color(0.22f, 0.22f, 0.25f), 8);
            k.Make("pole", transform, true);
            k.Clear();
            k.Nose(new Vector3(0, 0, 0.6f), new Vector3(1.2f, 1.2f, 1.2f), Quaternion.identity, Color.white);
            k.Nose(new Vector3(0, 0, -0.6f), new Vector3(1.2f, 1.2f, 1.2f), Quaternion.Euler(0, 180, 0), Color.white);
            var hg = k.Make("holo", transform, false);
            holo = hg.transform; holo.localPosition = new Vector3(0, 6.2f, 0); holo.localRotation = Quaternion.Euler(90, 0, 0);
            holoR = hg.GetComponent<Renderer>();
            k.Clear();
            k.Ring(Vector3.zero, radius - 0.35f, radius, Color.white, 40);
            var rg = k.Make("ring", transform, false);
            rg.transform.localPosition = Vector3.up * 0.25f;
            ringR = rg.GetComponent<Renderer>();
            k.Clear();
            k.Cyl(Vector3.zero, 0.5f, 1, Quaternion.identity, Color.white, 8, 0.05f, false);
            var bg = k.Make("beam", transform, false);
            bg.transform.localPosition = Vector3.up * 26; bg.transform.localScale = new Vector3(1, 40, 1);
            beamR = bg.GetComponent<Renderer>();
            Recolor();
        }

        public void ForceOwner(int t) { owner = t; capTeam = t; progress = 1; Recolor(); }

        public void AddPad(VKind k, Vector3 pos, Quaternion rot) => pads.Add(new Pad { kind = k, pos = pos, rot = rot, timer = 2f });

        public Color TeamColor(int t) => t < 0 ? new Color(0.85f, 0.85f, 0.85f) : battle.TeamColor(t);

        void Recolor()
        {
            Color c = TeamColor(owner);
            holoR.sharedMaterial = Mats.Additive(new Color(c.r, c.g, c.b, 0.9f), false);
            ringR.sharedMaterial = Mats.Alpha(new Color(c.r, c.g, c.b, 0.55f));
            beamR.sharedMaterial = Mats.Additive(new Color(c.r, c.g, c.b, 0.18f), false);
        }

        public void Tick(float dt)
        {
            spin += dt * 60;
            holo.localRotation = Quaternion.Euler(0, spin, 0) * Quaternion.Euler(90, 0, 0);
            holo.localPosition = new Vector3(0, 6.2f + Mathf.Sin(spin * 0.05f) * 0.25f, 0);

            present[0] = present[1] = 0;
            float r2 = radius * radius;
            foreach (var u in battle.units)
            {
                if (!u || !u.Alive || u.InVehicle) continue;
                Vector3 d = u.transform.position - Pos;
                if (d.y > 8 || d.y < -6) continue;
                d.y = 0;
                if (d.sqrMagnitude < r2) present[u.team]++;
            }
            contested = present[0] > 0 && present[1] > 0;
            int t = present[0] > 0 && present[1] == 0 ? 0 : present[1] > 0 && present[0] == 0 ? 1 : -1;
            float rate = t >= 0 ? (0.1f + 0.03f * Mathf.Min(present[t] - 1, 4)) * battle.CaptureMul(t) : 0;
            if (t >= 0)
            {
                if (owner == t) { progress = Mathf.MoveTowards(progress, 1, rate * dt); capTeam = t; }
                else if (capTeam != t && progress > 0)
                {
                    progress -= rate * dt;
                    if (progress <= 0)
                    {
                        progress = 0;
                        if (owner >= 0) { int lost = owner; owner = -1; capTeam = -1; battle.OnPostNeutralised(this, lost, t); Recolor(); }
                        capTeam = t;
                    }
                }
                else
                {
                    capTeam = t;
                    progress += rate * dt;
                    if (progress >= 1) { progress = 1; owner = t; battle.OnPostCaptured(this, t); Recolor(); }
                }
            }
            else if (!contested)
            {
                // drift back to the owner's state
                if (owner >= 0) { if (capTeam == owner) progress = Mathf.MoveTowards(progress, 1, 0.05f * dt); else { progress -= 0.05f * dt; if (progress <= 0) { capTeam = owner; } } }
                else progress = Mathf.MoveTowards(progress, 0, 0.03f * dt);
            }

            // vehicles
            foreach (var pad in pads)
            {
                if (pad.vehicle && pad.vehicle.Alive)
                {
                    // abandoned far away? recall it
                    if (pad.vehicle.IsEmpty && (pad.vehicle.transform.position - pad.pos).sqrMagnitude > 50 * 50 && pad.vehicle.IdleTime > 45) pad.vehicle.SelfDestruct();
                    continue;
                }
                if (owner < 0) continue;
                pad.timer -= dt;
                if (pad.timer <= 0)
                {
                    if (Physics.CheckSphere(pad.pos + Vector3.up * 2, 2.5f, (1 << Layers.Unit) | (1 << Layers.Vehicle))) { pad.timer = 2; continue; }
                    var def = battle.Faction(owner).Vehicle(pad.kind);
                    pad.vehicle = battle.SpawnVehicle(def, owner, pad.pos, pad.rot);
                    pad.timer = def.respawn * battle.VehicleRespawnMul(owner);
                }
            }
        }

        public Vector3 SpawnPoint()
        {
            for (int i = 0; i < 12; i++)
            {
                float a = Random.Range(0, Mathf.PI * 2), d = Random.Range(3f, radius - 1);
                Vector3 p = Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                p.y = battle.GroundY(p) + 0.1f;
                if (!Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.45f, Layers.MoveMask | (1 << Layers.Unit), QueryTriggerInteraction.Ignore)) return p;
            }
            Vector3 q = Pos + Random.insideUnitSphere * 2; q.y = battle.GroundY(q) + 0.3f;
            return q;
        }
    }
}
