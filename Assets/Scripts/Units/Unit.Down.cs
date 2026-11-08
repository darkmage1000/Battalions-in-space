using UnityEngine;

namespace BIS
{
    /// <summary>
    /// Battle royale duos: a hit that would kill you while your mate is still up knocks you down instead. Down you crawl,
    /// can't shoot, use anything or get in a vehicle, and bleed out over <see cref="BleedTime"/> (enemies can finish you
    /// sooner). Your mate kneels beside you for <see cref="ReviveTime"/> to bring you back (bleeding stops meanwhile).
    /// Finished off, you drop an ID tag; your mate carries it to a revive station to call you back in by grav-chute.
    /// Reviving and calling in use the medkit channel (<see cref="Using"/>, <see cref="useKind"/> Revive / Station), so
    /// moving off, firing or swapping cancels them, and a Wi-Fi guest sees the progress through the same fields.
    /// </summary>
    public partial class Unit
    {
        public const float DownHp = 100f, BleedTime = 30f, CrawlSpeed = 1.5f;
        public const float ReviveTime = 5f, ReviveRange = 2.3f, ReviveHp = 0.3f;
        public const float StationTime = 4f, StationRange = 3.6f;

        public bool knocked;
        public float knockedAt;
        public Unit knockedBy;
        public Unit revivedBy;            // a mate kneeling over us right now (bleeding is paused)
        float reviveHold;                 // ...until this time (the reviver renews it every frame)
        public float reviveShown;         // guest: the host's revive progress on us (0..1, -1 = nobody)
        public Unit useTarget;            // the mate we are reviving
        public int useStation = -1;       // the revive station we are calling a mate in at
        public int tagOf = -1;            // carrying the ID tag of b.people[tagOf]
        public int netFlags2;             // Wi-Fi copies: 1 knocked, 2 reviving
        float standH;

        public bool BeingRevived => knocked && Time.time < reviveHold;
        public bool Reviving => Using && useKind == LootKind.Revive;
        public bool CallingIn => Using && useKind == LootKind.Station;
        /// <summary>How far along a revive on this (knocked) soldier is, 0..1, or -1 when nobody is at it.</summary>
        public float ReviveProgress => net == NetMode.GuestLocal ? reviveShown
            : BeingRevived && revivedBy && revivedBy.Reviving ? 1 - revivedBy.useT / Mathf.Max(0.01f, revivedBy.useDur) : -1;

        /// <summary>Lethal damage in duos with a mate still on their feet: down instead of dead.</summary>
        bool TryKnock(DamageInfo d)
        {
            var r = battle.royale;
            if (knocked || r == null || !r.CanKnock(this)) return false;
            knocked = true; knockedAt = Time.time;
            knockedBy = d.attacker && d.attacker.team != team ? d.attacker : lastAttacker;
            hp = DownHp;
            useT = 0; useTarget = null; useStation = -1;
            if (gliding) EndGlide();
            DownShape(true);
            r.OnKnocked(this, knockedBy, d.weapon);
            return true;
        }

        /// <summary>The capsule lies down with the soldier (so shots over a crawling body miss), and stands back up.</summary>
        public void DownShape(bool down)
        {
            if (!cc) return;
            if (standH <= 0) standH = cc.height;
            float h = down ? 0.9f : standH;
            if (Mathf.Abs(cc.height - h) < 0.01f) return;
            cc.height = h; cc.center = new Vector3(0, h / 2, 0);
            crouching = false;
            if (rig && rig.gun) rig.gun.gameObject.SetActive(!down);
        }

        /// <summary>Wi-Fi guest: the host says whether our own soldier is down.</summary>
        public void SetKnockedLocal(bool down)
        {
            if (down == knocked) return;
            knocked = down;
            if (down) { knockedAt = Time.time; useT = 0; if (gliding) EndGlide(); }
            DownShape(down);
        }

        /// <summary>Every frame on the device that owns the soldier's rules (host / single player): bleeding, and the
        /// revive / call-in channels staying valid.</summary>
        void DownTick(float dt)
        {
            if (knocked)
            {
                if (!BeingRevived)
                {
                    revivedBy = null;
                    hp -= DownHp / BleedTime * dt;
                    if (hp <= 0) { Die(knockedBy, Vector3.zero, null); return; }
                }
            }
            if (Reviving)
            {
                var t = useTarget;
                if (!t || !t.Alive || !t.knocked || t.team != team || knocked || Flat(t.transform.position - transform.position) > ReviveRange + 1f) { useT = 0; useTarget = null; }
                else { t.reviveHold = Time.time + 0.25f; t.revivedBy = this; }
            }
            if (CallingIn)
            {
                var r = battle.royale;
                if (r == null || tagOf < 0 || !r.StationFree(useStation) || Flat(r.stations[useStation].pos - transform.position) > StationRange + 1f) { useT = 0; useStation = -1; }
            }
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        /// <summary>Kneel beside a downed mate and bring them back (a channel: moving off, firing or swapping stops it).</summary>
        public bool StartRevive(Unit mate)
        {
            if (Using || !Alive || knocked || InVehicle || !mate || !mate.Alive || !mate.knocked || mate.team != team || mate == this) return false;
            if (Flat(mate.transform.position - transform.position) > ReviveRange + 0.4f) return false;
            useKind = LootKind.Revive; useT = useDur = ReviveTime; useTarget = mate;
            mate.reviveHold = Time.time + 0.25f; mate.revivedBy = this;
            Sfx.Play("repair", transform.position, 0.5f, 1.1f);
            return true;
        }

        /// <summary>Use a revive station with a mate's ID tag: they come back by grav-chute when the channel finishes.</summary>
        public bool StartStation(int idx)
        {
            var r = battle.royale;
            if (Using || !Alive || knocked || InVehicle || tagOf < 0 || r == null || !r.StationFree(idx)) return false;
            if (Flat(r.stations[idx].pos - transform.position) > StationRange + 0.4f) return false;
            useKind = LootKind.Station; useT = useDur = StationTime; useStation = idx;
            Sfx.Play("repair", transform.position, 0.6f, 0.7f);
            return true;
        }

        void FinishDownUse()
        {
            if (useKind == LootKind.Revive && useTarget && useTarget.knocked && useTarget.Alive) useTarget.Revive(this);
            else if (useKind == LootKind.Station) battle.royale?.UseStation(this, useStation);
            useTarget = null; useStation = -1;
        }

        public void Revive(Unit by)
        {
            if (!knocked || !Alive) return;
            knocked = false; knockedBy = null; revivedBy = null; reviveHold = 0;
            hp = maxHp * ReviveHp;
            DownShape(false);
            invulnUntil = Time.time + 1f;
            Fx.Burst(AimPoint, new Color(0.4f, 1f, 0.55f), 16, 2.5f, 0.25f, 0.7f);
            Sfx.Play("award", transform.position, 0.5f, 1.3f);
            battle.royale?.OnRevived(this, by);
        }
    }
}
