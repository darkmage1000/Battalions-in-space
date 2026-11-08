using UnityEngine;

namespace BIS.Net
{
    /// <summary>Battle royale over Wi-Fi, guest side: loot, chests, vaults and the zone from the host; our pockets; requests.</summary>
    public static partial class NetClient
    {
        static Vector3 lastPick; static bool lastJump; static float pickSendT;

        /// <summary>Before the jump: tell the host where we want to land (and when we jump).</summary>
        public static void RequestJump(Vector3 at, bool now)
        {
            pickSendT -= Time.unscaledDeltaTime;
            if (now == lastJump && (at - lastPick).sqrMagnitude < 1 && pickSendT > 0) return;
            lastPick = at; lastJump = now; pickSendT = 0.5f;
            Session.link?.Send(new W(Msg.RJump).V(at).B(now));
        }
        public static void RequestHeal() => Session.link?.Send(new W(Msg.RHeal));
        /// <summary>what: 0 an item (id), 1 a chest (index), 2 a vault (index), 3 revive a mate (person index), 4 a revive station (index).</summary>
        public static void RequestLoot(int what, int id) { Session.link?.Send(new W(Msg.RTake).U8(what).I32(id)); }

        static void HandleRoyale(R r)
        {
            var ry = b != null ? b.royale : null;
            if (ry == null) return;
            switch (r.type)
            {
                case Msg.RLoot: { int n = r.U16(); for (int i = 0; i < n; i++) ReadLoot(r, ry); break; }
                case Msg.RLootAdd: ReadLoot(r, ry); break;
                case Msg.RLootGone: ry.ClientRemove(r.U16()); break;
                case Msg.RChests: { int n = r.U16(); for (int i = 0; i < n; i++) { Vector3 p = r.V(); Quaternion q = r.Q(); bool o = r.B(); ry.ClientChest(p, q, o); } break; }
                case Msg.RChest: ry.ClientChestOpened(r.U16()); break;
                case Msg.RVault: { int i = r.U8(); if (i < b.land.vaults.Count) b.land.OpenVault(b.land.vaults[i]); break; }
                case Msg.RStations: { int n = r.U8(); for (int i = 0; i < n; i++) { Vector3 p = r.V(); Quaternion q = r.Q(); bool u = r.B(); if (i >= ry.stations.Count) ry.ClientStation(p, q, u); else if (u) ry.ClientStationUsed(i); } break; }
                case Msg.RStationUsed: ry.ClientStationUsed(r.U8()); break;
                case Msg.RState:
                    {
                        ry.phase = (Royale.Phase)r.U8(); ry.dropT = r.F();
                        ry.zoneC = new Vector2(r.F(), r.F()); ry.zoneR = r.F();
                        ry.nextC = new Vector2(r.F(), r.F()); ry.nextR = r.F();
                        ry.zonePhase = r.U8(); ry.zoneTimer = r.F(); ry.shrinking = r.B();
                        ry.AlivePlayers = r.U16(); ry.AliveTeams = r.U16(); ry.playTime = r.F();
                        ry.bossSpawned = r.B(); ry.bossDead = r.B(); ry.bossLair = r.V(); ry.bossPlace = r.Str();
                        int n = r.U16();
                        for (int i = 0; i < n; i++)
                        {
                            int pl = r.U8(); bool dr = r.B();
                            if (i < b.people.Count) { b.people[i].placement = pl; b.people[i].dropped = dr; }
                        }
                        break;
                    }
            }
        }

        static void ReadLoot(R r, Royale ry)
        {
            int id = r.U16(); var kind = (LootKind)r.U8(); int tier = r.U8(), count = r.U8(); Vector3 pos = r.V(); int gun = r.S8(), fac = r.U8();
            if (ry.ById(id) != null) return;
            var w = kind == LootKind.Weapon ? LootTable.Make(Mathf.Max(0, gun), fac, tier) : null;
            ry.Drop(kind, tier, w, count, pos, false, id);
        }

        /// <summary>Our pockets and found weapons (the host's copy of us is the real one).</summary>
        static void ReadRoyaleLocal(R r)
        {
            var me = Me;
            if (!r.B()) return;
            float armor = r.F(); int med = r.U8(), sh = r.U8(), keys = r.U8(); float useT = r.F(), useDur = r.F(); var useKind = (LootKind)r.U8();
            var slots = new (int gun, int fac, int tier)[2];
            for (int s = 0; s < 2; s++) slots[s] = (r.S8(), r.U8(), r.S8());
            bool down = r.B(); int tag = r.S8(); float rev = r.F(); int useTgt = r.I16();
            if (!me || me.dead) return;
            me.SetKnockedLocal(down); me.tagOf = tag; me.reviveShown = rev;
            me.useTarget = UnitOf(useTgt);
            me.armor = armor; me.medkits = med; me.shields = sh; me.keys = keys; me.useT = useT; me.useDur = useDur; me.useKind = useKind;
            for (int s = 0; s < 2; s++)
            {
                var (gun, fac, tier) = slots[s];
                var ws = me.weapons[s];
                if (gun == -2) { if (ws != null && s == 1) { me.weapons[1] = null; if (me.cur == 1) { me.cur = 0; me.rig.SetWeapon(me.Weapon.def); } } continue; }
                if (gun < 0) continue;      // the sidearm (or repair tool) we landed with
                if (ws != null && ws.def.lootGun == gun && ws.def.lootFaction == fac && ws.def.tier == tier) continue;
                me.weapons[s] = new WeaponState(LootTable.Make(gun, fac, tier));
                if (s == me.cur) me.rig.SetWeapon(me.weapons[s].def);
            }
        }
    }
}
