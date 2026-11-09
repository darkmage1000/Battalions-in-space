using UnityEngine;

namespace BIS.Net
{
    /// <summary>
    /// Battle royale over Wi-Fi, host side: the guest gets the loot list, chests and vault doors when it joins, every item
    /// that appears or is taken after that, the zone and standings twice a second, and its own pockets with its LocalState.
    /// The guest's jump, heal and loot / chest / vault requests are checked here with the same rules as the local player.
    /// </summary>
    public static partial class NetHost
    {
        static float rStateT;
        static int snapCount;

        public static void RoyaleTick(float dt)
        {
            if (!Live || b == null || b.royale == null) return;
            rStateT -= dt;
            if (rStateT <= 0) { rStateT = 0.5f; SendRoyaleState(); }
        }

        static void SendRoyaleState()
        {
            var r = b.royale;
            var w = new W(Msg.RState).U8((int)r.phase).F(r.dropT).F(r.zoneC.x).F(r.zoneC.y).F(r.zoneR).F(r.nextC.x).F(r.nextC.y).F(r.nextR)
                .U8(r.zonePhase).F(r.zoneTimer).B(r.shrinking).U16(r.AlivePlayers).U16(r.AliveTeams).F(r.playTime)
                .B(r.bossSpawned).B(r.bossDead).V(r.bossLair).Str(r.bossPlace ?? "");
            w.U16(b.people.Count);
            foreach (var p in b.people) w.U8(p.placement).B(p.dropped);
            w.V(r.dropPickPos).B(r.playerPicked);            // the host's landing pick (it leads a Wi-Fi duo)
            Session.link.Send(w);
        }

        static void WriteLoot(W w, LootItem it)
        {
            w.U16(it.id).U8((int)it.kind).U8(it.tier).U8(it.count).V(it.pos)
             .S8(it.weapon != null ? it.weapon.lootGun : -1).U8(it.weapon != null ? it.weapon.lootFaction : 0);
        }

        public static void LootAdd(LootItem it) { if (Live) { var w = new W(Msg.RLootAdd); WriteLoot(w, it); Session.link.Send(w); } }
        public static void LootGone(int id) { if (Live) Session.link.Send(new W(Msg.RLootGone).U16(id)); }
        public static void ChestOpened(int index) { if (Live && index >= 0) Session.link.Send(new W(Msg.RChest).U16(index)); }
        public static void VaultOpened(int index) { if (Live && index >= 0) Session.link.Send(new W(Msg.RVault).U8(index)); }
        public static void StationUsed(int index) { if (Live && index >= 0) Session.link.Send(new W(Msg.RStationUsed).U8(index)); }

        /// <summary>Everything royale that exists now, for a guest that has just built the map.</summary>
        static void SendRoyaleFull()
        {
            var r = b.royale;
            var cw = new W(Msg.RChests).U16(r.chests.Count);
            foreach (var c in r.chests) cw.V(c.pos).Q(c.rot).B(c.opened);
            Session.link.Send(cw);
            for (int i = 0; i < r.loot.Count; i += 100)
            {
                int n = Mathf.Min(100, r.loot.Count - i);
                var lw = new W(Msg.RLoot).U16(n);
                for (int k = 0; k < n; k++) WriteLoot(lw, r.loot[i + k]);
                Session.link.Send(lw);
            }
            for (int i = 0; i < b.land.vaults.Count; i++) if (b.land.vaults[i].open) VaultOpened(i);
            var sw = new W(Msg.RStations).U8(r.stations.Count);
            foreach (var s in r.stations) sw.V(s.pos).Q(s.rot).B(s.used);
            Session.link.Send(sw);
            SendRoyaleState();
        }

        static void WriteRoyaleLocal(W w, Unit u)
        {
            bool a = u && u.Alive;
            w.B(a);
            if (!a) return;
            w.F(u.armor).U8(u.medkits).U8(u.shields).U8(u.keys).F(u.useT).F(u.useDur).U8((int)u.useKind);
            for (int s = 0; s < 2; s++)
            {
                var ws = u.weapons[s];
                if (ws == null) { w.S8(-2).U8(0).S8(0); continue; }
                w.S8(ws.def.tier >= 0 ? ws.def.lootGun : -1).U8(ws.def.lootFaction).S8(ws.def.tier);
            }
            // duos: down or not, whose tag we carry, and how far a mate's revive on us is
            w.B(u.knocked).S8(u.tagOf).F(u.ReviveProgress).I16(u.useTarget ? Pid(u.useTarget) : -1);
        }

        static void HandleRoyale(R r)
        {
            var ry = b.royale;
            if (ry == null) return;
            switch (r.type)
            {
                case Msg.RJump:
                    {
                        Vector3 p = r.V(); bool now = r.B();
                        if (remote != null && !remote.dropped) { remote.dropAt = p; if (now) ry.remoteJump = true; }
                        break;
                    }
                case Msg.RHeal:
                    {
                        var u = remote?.unit;
                        if (u && u.Alive && !u.UseBest()) Session.link?.Send(new W(Msg.Say).Str(u.medkits + u.shields == 0 ? "No medkits or shield cells" : "Health and armour are full"));
                        break;
                    }
                case Msg.RTake:
                    {
                        int what = r.U8(), id = r.I32();
                        var u = remote?.unit;
                        if (!u || !u.Alive || u.InVehicle) break;
                        Vector3 at = u.transform.position;
                        if (what == 0)
                        {
                            var it = ry.ById(id);
                            if (it == null || (it.pos - at).magnitude > 4) break;
                            string why = ry.CantTake(u, it);
                            if (why != null) Session.link?.Send(new W(Msg.Say).Str(why)); else ry.Take(u, it);
                        }
                        else if (what == 1)
                        {
                            if (id >= 0 && id < ry.chests.Count && (ry.chests[id].pos - at).magnitude < 4.5f) ry.OpenChest(u, ry.chests[id]);
                        }
                        else if (what == 3)
                        {
                            // revive a downed mate
                            var m = id >= 0 && id < b.people.Count ? b.people[id].unit : null;
                            if (m && !u.StartRevive(m)) Session.link?.Send(new W(Msg.Say).Str("Can't revive from here"));
                        }
                        else if (what == 5)
                        {
                            // drop a medkit (0), shield cell (1) or the gun in hand (2) for the mate
                            string why = ry.CantDrop(u, id);
                            if (why != null) Session.link?.Send(new W(Msg.Say).Str(why)); else ry.DropFromPockets(u, id);
                        }
                        else if (what == 4)
                        {
                            if (!u.StartStation(id)) Session.link?.Send(new W(Msg.Say).Str("Can't use the station now"));
                        }
                        else if (what == 2 && id >= 0 && id < b.land.vaults.Count)
                        {
                            var v = b.land.vaults[id];
                            if ((v.lockPoint - at).magnitude > 5) break;
                            if (u.keys <= 0) Session.link?.Send(new W(Msg.Say).Str("You need a vault key"));
                            else ry.TryOpenVault(u, v);
                        }
                        break;
                    }
            }
        }

        /// <summary>Royale snapshots: soldiers far from the guest go out one time in four (100 players is a lot of data).</summary>
        static bool SkipFar(Unit u)
        {
            if (b.royale == null || remote == null || !remote.unit) return false;
            if (snapCount % 4 == 0) return false;
            Vector3 g = remote.unit.InVehicle ? remote.unit.vehicle.transform.position : remote.unit.transform.position;
            return (u.transform.position - g).sqrMagnitude > 280 * 280;
        }
    }
}
