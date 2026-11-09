using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Duos (teams of two). A soldier whose mate is still on their feet is knocked down instead of killed (see
    /// <see cref="Unit.TryKnock"/>); when nobody on a team is standing, everyone of it who is down is finished. A soldier
    /// who dies while their mate lives drops an ID tag where they fell. Their mate picks it up and takes it to a revive
    /// station (one use each, spread over the map): the fallen mate drops back in by grav-chute above it with a sidearm.
    /// </summary>
    public partial class Royale
    {
        public bool Duos;                        // any two players share a team (worked out from the roster, so a Wi-Fi guest knows too)

        public class ReviveStation
        {
            public Vector3 pos;
            public Quaternion rot;
            public GameObject go;
            public bool used;
        }
        public readonly List<ReviveStation> stations = new List<ReviveStation>();
        readonly Dictionary<int, Participant> mates = new Dictionary<int, Participant>();

        void InitDuos()
        {
            var size = new Dictionary<int, int>();
            foreach (var p in b.people) if (!p.boss) { size.TryGetValue(p.team, out int n); size[p.team] = n + 1; }
            foreach (var kv in size) if (kv.Value > 1) Duos = true;
        }

        /// <summary>The other player on p's team (null when solo).</summary>
        public Participant MateOf(Participant p)
        {
            if (p == null || !Duos) return null;
            foreach (var o in b.people) if (o != p && o.team == p.team && !o.boss) return o;
            return null;
        }

        // ── the jump leader ──────────────────────────────────────────────
        // In duos one player leads the drop: they pick the spot and jump; their mate jumps the moment they do, beside them,
        // and their chute keeps alongside until (a person) they steer away. The leader is the team's first person in the
        // roster (the host over Wi-Fi), else its first bot - the same answer on both devices.
        public bool playerSplit;                 // the local player (a follower) has steered off on their own
        public Vector3 leaderPick; public bool leaderPicked;   // Wi-Fi guest: where the host (our leader) means to land

        public Participant LeaderOf(Participant p)
        {
            if (p == null || !Duos) return null;
            Participant best = null;
            foreach (var o in b.people)
                if (o.team == p.team && !o.boss && (best == null || (o.Human && !best.Human))) best = o;
            return best;
        }
        public bool Follower(Participant p) => Duos && LeaderOf(p) is Participant l && l != p;

        /// <summary>Where a follower's chute aims: a few metres off the leader on a fixed side (not their facing, which
        /// swings as they look around).</summary>
        public Vector3 FollowSpot(Unit leader) => leader.transform.position + new Vector3(4.5f, 0, -2.5f);

        /// <summary>The horizontal stick a follower's chute needs this frame to keep beside the gliding leader.</summary>
        public static Vector3 FollowMove(Unit me, Unit leader, Vector3 spot)
        {
            Vector3 lv = leader.velocity; lv.y = 0;
            Vector3 gap = spot - me.transform.position; gap.y = 0;
            return Vector3.ClampMagnitude((lv + gap * 1.2f) / 17f, 1f);
        }

        static bool Standing(Participant p) => p != null && p.unit && p.unit.Alive && !p.unit.knocked;

        public bool CanKnock(Unit u) => Duos && !u.gliding && !u.part.boss && !u.IsHero && Standing(MateOf(u.part));

        public static int Knocks, Revives, Respawns;            // test harness counters
        public void OnKnocked(Unit u, Unit by, WeaponDef w)
        {
            Knocks++;
            string kname = by ? by.displayName : null;
            HUD.Kill(kname, by ? by.team : -1, u.displayName, u.team, "knocked down");
            Net.NetHost.OnKill(kname, by ? by.team : -1, u.displayName, u.team, "knocked down");
            var mate = MateOf(u.part);
            if (u.part.isPlayer) { HUD.Message(mate != null ? $"KNOCKED DOWN - crawl to cover, {mate.name} can revive you" : "KNOCKED DOWN", UI.Bad, 4); Sfx.Play2D("alarm", 0.5f); }
            else if (u.part.remote) Net.NetHost.Text(mate != null ? $"KNOCKED DOWN - crawl to cover, {mate.name} can revive you" : "KNOCKED DOWN", UI.Bad, 4, "alarm");
            if (mate != null) TeamText(mate, $"{u.displayName} is down! Get to them and hold USE to revive", new Color(1f, 0.6f, 0.3f), 4);
            if (by && by.part.isPlayer) HUD.Message($"Knocked down {u.displayName}", UI.Gold, 2);
            CheckWipe(u.team);
        }

        public void OnRevived(Unit u, Unit by)
        {
            Revives++;
            if (u.part.isPlayer) HUD.Message(by ? $"{by.displayName} got you back up" : "Back on your feet", UI.Good, 3);
            else if (u.part.remote) Net.NetHost.Text(by ? $"{by.displayName} got you back up" : "Back on your feet", UI.Good, 3, "award");
            if (by) TeamText(by.part, $"You revived {u.displayName}", UI.Good, 2.5f);
        }

        /// <summary>Nobody on the team is standing: whoever is down is finished (by whoever knocked them).</summary>
        void CheckWipe(int team)
        {
            foreach (var p in b.people) if (p.team == team && !p.boss && Standing(p)) return;
            foreach (var p in b.people)
                if (p.team == team && !p.boss && p.unit && p.unit.Alive && p.unit.knocked) p.unit.Die(p.unit.knockedBy, Vector3.zero, null);
        }

        /// <summary>A message to one player of a team (the local player, or the Wi-Fi guest).</summary>
        void TeamText(Participant to, string text, Color c, float t)
        {
            if (to == null) return;
            if (to.isPlayer) HUD.Message(text, c, t);
            else if (to.remote) Net.NetHost.Text(text, c, t, null);
        }

        /// <summary>Called from <see cref="OnUnitKilled"/>: finish the downed, and drop the fallen one's ID tag for their mate.</summary>
        void DuoDeath(Unit victim)
        {
            if (!Duos || client) return;
            CheckWipe(victim.team);
            var mate = MateOf(victim.part);
            if (mate == null || !mate.Alive) return;
            Drop(LootKind.Tag, 0, null, b.people.IndexOf(victim.part), TagSpot(victim.transform.position), false);
            TeamText(mate, $"{victim.displayName} is out - pick up their ID TAG and take it to a REVIVE STATION (green on the map)", new Color(0.5f, 1f, 0.6f), 6);
            if (victim.part.isPlayer) HUD.Message($"{mate.name} can bring you back: your ID tag to a revive station", new Color(0.5f, 1f, 0.6f), 6);
            else if (victim.part.remote) Net.NetHost.Text($"{mate.name} can bring you back: your ID tag to a revive station", new Color(0.5f, 1f, 0.6f), 6, null);
        }

        /// <summary>Where an ID tag lands: where they fell, unless that is somewhere nobody can walk to (a wall top, a roof
        /// with no stairs, a ledge) - then the nearest open ground below. Upper floors with stairs are fine.</summary>
        public Vector3 TagSpot(Vector3 at)
        {
            var land = b.land;
            float top = land.Top(at.x, at.z);
            Vector3 p = at; p.y = b.GroundY(at + Vector3.up * 0.5f) + 0.04f;
            if (p.y - top > 1.2f && land.FloorAt(p + Vector3.up * 0.1f) == null)
            {
                p = land.nav.Walkable(at) ? at : land.nav.SnapPoint(at);
                p.y = land.Top(p.x, p.z) + 0.04f;
            }
            return p;
        }

        void ClearTeamTags(int team)
        {
            var gone = loot.FindAll(it => it.kind == LootKind.Tag && TagOwner(it) is Participant o && o.team == team);
            foreach (var it in gone) Remove(it);
        }

        /// <summary>Whose tag an ID tag item is.</summary>
        public Participant TagOwner(LootItem it) => it != null && it.kind == LootKind.Tag && it.count >= 0 && it.count < b.people.Count ? b.people[it.count] : null;
        public string ItemName(LootItem it)
        {
            var o = TagOwner(it);
            return o != null ? $"{o.name}'s ID tag" : it.Name;
        }
        /// <summary>The ID tag of p lying on the ground, if any.</summary>
        public LootItem TagOnGround(Participant p)
        {
            int idx = b.people.IndexOf(p);
            foreach (var it in loot) if (it.kind == LootKind.Tag && it.count == idx && !it.taken) return it;
            return null;
        }
        /// <summary>Who carries p's ID tag (null if nobody).</summary>
        public Unit TagCarrier(Participant p)
        {
            int idx = b.people.IndexOf(p);
            foreach (var u in b.units) if (u && u.Alive && u.tagOf == idx) return u;
            return null;
        }

        // ── giving things to your mate ───────────────────────────────────
        /// <summary>Why u can't drop this now (what: 0 a medkit, 1 a shield cell, 2 the gun in hand), or null.</summary>
        public string CantDrop(Unit u, int what)
        {
            if (u == null || !u.Alive || u.knocked || u.InVehicle || u.Using) return "Not now";
            switch (what)
            {
                case 0: return u.medkits > 0 ? null : "No medkits";
                case 1: return u.shields > 0 ? null : "No shield cells";
                default:
                    var w = u.Weapon;
                    if (w == null || w.def.tier < 0) return "You can't drop your sidearm";
                    return null;
            }
        }

        /// <summary>Put a medkit, a shield cell or the gun in hand on the ground in front of u (for a mate to take).
        /// The one who dropped it won't auto-pick it up again for a few seconds.</summary>
        public bool DropFromPockets(Unit u, int what)
        {
            if (client || CantDrop(u, what) != null) return false;
            Vector3 p = u.transform.position + u.transform.forward * 1.6f;
            p.y = b.GroundY(p + Vector3.up * 0.6f) + 0.04f;
            if (Mathf.Abs(p.y - u.transform.position.y) > 1.2f) p = u.transform.position + Vector3.up * 0.04f;
            LootItem it;
            if (what == 0) { u.medkits--; it = Drop(LootKind.Medkit, 1, null, 1, p, false); }
            else if (what == 1) { u.shields--; it = Drop(LootKind.Shield, 1, null, 1, p, false); }
            else
            {
                var w = u.Weapon;
                it = Drop(LootKind.Weapon, w.def.tier, w.def, 1, p, false);
                // the other gun goes in hand; with none left the race's sidearm comes back
                int other = 1 - u.cur;
                if (u.weapons[other] != null) { u.weapons[u.cur] = null; if (u.cur == 0) { u.weapons[0] = u.weapons[1]; u.weapons[1] = null; } u.cur = 0; }
                else u.weapons[u.cur] = new WeaponState(LootTable.Secondary(u.faction));
                u.rig.SetWeapon(u.Weapon.def);
            }
            it.droppedBy = u; it.dropLock = Time.time + 6;
            Sfx.Play("ui2", p, 0.5f);
            return true;
        }

        // ── revive stations ──────────────────────────────────────────────
        public bool StationFree(int i) => i >= 0 && i < stations.Count && !stations[i].used;

        public int NearestStation(Vector3 p, float r, bool freeOnly = true)
        {
            int best = -1; float bd = r * r;
            for (int i = 0; i < stations.Count; i++)
            {
                if (freeOnly && stations[i].used) continue;
                Vector3 d = stations[i].pos - p; if (Mathf.Abs(d.y) > 3f && r < 20) continue; d.y = 0;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = i; }
            }
            return best;
        }

        void SpawnStations()
        {
            var land = b.land;
            int n = Mathf.Clamp(TotalPlayers / 6, 6, 18);
            float minGap = land.PlayHalf * 0.32f;
            for (int tries = 0; tries < 900 && stations.Count < n; tries++)
            {
                if (tries % 150 == 149) minGap *= 0.8f;      // relax the spacing if the map is crowded
                Vector3 p = RandomGround(land.PlayHalf * 0.88f);
                bool ok = true;
                foreach (var s in stations) if ((s.pos - p).sqrMagnitude < minGap * minGap) { ok = false; break; }
                if (!ok || InVault(p)) continue;
                // open ground: not inside or against a building, not on a steep slope
                if (Physics.CheckSphere(p + Vector3.up * 1.6f, 2.6f, (1 << Layers.Default) | (1 << Layers.Vehicle), QueryTriggerInteraction.Ignore)) continue;
                float slope = Mathf.Max(Mathf.Abs(land.Top(p.x + 2, p.z) - land.Top(p.x - 2, p.z)), Mathf.Abs(land.Top(p.x, p.z + 2) - land.Top(p.x, p.z - 2)));
                if (slope > 1.4f) continue;
                if (land.LiquidAt(p.x, p.z, out _, out _)) continue;
                AddStation(p, Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0), false);
            }
        }

        ReviveStation AddStation(Vector3 p, Quaternion q, bool used)
        {
            var s = new ReviveStation { pos = p, rot = q, used = used };
            var go = new GameObject("revive station");
            go.transform.SetParent(lootRoot, false);
            go.transform.SetPositionAndRotation(p, q);
            go.AddComponent<MeshFilter>().sharedMesh = LootModels.Station(used);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Lit;
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 1.3f, -1.05f); bc.size = new Vector3(0.7f, 2.6f, 0.5f);   // the pylon (the pad is walked on)
            s.go = go;
            stations.Add(s);
            return s;
        }

        void MarkStationUsed(int i)
        {
            if (i < 0 || i >= stations.Count || stations[i].used) return;
            var s = stations[i];
            s.used = true;
            s.go.GetComponent<MeshFilter>().sharedMesh = LootModels.Station(true);
            Fx.Burst(s.pos + Vector3.up * 1.5f, new Color(0.4f, 1f, 0.55f), 24, 4, 0.3f, 0.8f);
        }

        /// <summary>The channel at a station finished: call the mate whose tag u carries back in.</summary>
        public void UseStation(Unit u, int idx)
        {
            if (client || !StationFree(idx) || u.tagOf < 0 || u.tagOf >= b.people.Count) return;
            var p = b.people[u.tagOf];
            u.tagOf = -1;
            if (p.Alive || p.team != u.team || p.boss) return;
            MarkStationUsed(idx);
            Net.NetHost.StationUsed(idx);
            Respawns++;
            var s = stations[idx];
            Sfx.Play("award", s.pos, 1f, 0.9f);
            Vector3 at = s.pos + s.rot * Vector3.forward * 3f; at.y = b.land.Top(at.x, at.z);
            p.dropAt = at; p.placement = 0;
            var nu = b.SpawnUnit(p, ClassFor(p), null, at + Vector3.up * 55, s.rot.eulerAngles.y);
            nu.StartGlide();
            if (p.isPlayer) { b.control.OnSpawned(nu); HUD.Message($"{u.displayName} called you back in - steer your grav-chute down", UI.Good, 5); Sfx.Play2D("award", 0.8f); }
            TeamText(u.part, $"{p.name} is dropping back in", UI.Good, 3);
            if (p.remote) Net.NetHost.Text($"{u.displayName} called you back in - steer your grav-chute down", UI.Good, 5, "award");
        }

        /// <summary>Wi-Fi guest: the host's stations.</summary>
        public void ClientStation(Vector3 p, Quaternion q, bool used) => AddStation(p, q, used);
        public void ClientStationUsed(int i) => MarkStationUsed(i);
    }
}
