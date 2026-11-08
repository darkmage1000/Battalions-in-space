using System.Collections.Generic;
using UnityEngine;

namespace BIS
{
    public struct UnitInput
    {
        public Vector3 move;          // world space, horizontal, |move| <= 1
        public Vector3 aimPoint;      // where shots should go
        public bool fire, fireDown, aim, jump, jumpDown, sprint, crouch, reload, swap, equip, ability, use;
    }

    public class WeaponState
    {
        public WeaponDef def;
        public float heat, cool, reload;
        public bool overheated;
        public int clip, ammo;
        public WeaponState(WeaponDef d) { def = d; clip = d.clip; ammo = d.ammo; }
        public bool Ready => cool <= 0 && reload <= 0 && !overheated && (def.UsesHeat || def.clip == 0 || clip > 0);
        public void Tick(float dt)
        {
            cool -= dt;
            if (reload > 0) { reload -= dt; if (reload <= 0) { int take = Mathf.Min(def.clip - clip, ammo); clip += take; ammo -= take; } }
            if (def.UsesHeat)
            {
                heat = Mathf.Max(0, heat - dt * (overheated ? 0.45f : 0.6f));
                if (overheated && heat <= 0.05f) overheated = false;
            }
        }
        public void Spend()
        {
            cool = 1f / def.rate;
            if (def.UsesHeat) { heat += def.heatPerShot; if (heat >= 1) { heat = 1; overheated = true; } }
            else if (def.clip > 0) clip--;
        }
        public bool NeedsReload => !def.UsesHeat && def.clip > 0 && clip <= 0 && ammo > 0 && reload <= 0;
        public void StartReload() { if (!def.UsesHeat && def.clip > 0 && clip < def.clip && ammo > 0 && reload <= 0) reload = def.reload; }
        public void Refill() { ammo = def.ammo; clip = def.clip; heat = 0; overheated = false; }
        public string Status => def.UsesHeat ? (overheated ? "OVERHEAT" : $"{Mathf.RoundToInt(heat * 100)}% heat") : def.clip > 0 ? (reload > 0 ? "reloading" : $"{clip} / {ammo}") : "";
    }

    /// <summary>A soldier or hero on foot. Driven by UnitInput from PlayerControl or BotBrain.</summary>
    public partial class Unit : MonoBehaviour, IDamageable
    {
        public Battle battle;
        public Participant part;
        public int team;
        public FactionDef faction;
        public ClassDef cls;
        public HeroDef hero;
        public string displayName;
        public float hp, maxHp;
        public bool dead;
        public CharacterController cc;
        public SoldierRig rig;
        public BotBrain bot;
        public UnitInput input;
        public Vector3 velocity;
        public float yaw, pitch;
        public readonly WeaponState[] weapons = new WeaponState[2];
        public int cur;
        public int equipLeft;
        public float jetFuel = 1, abilityCd, abilityTime, heroDrainAcc;
        public Vehicle vehicle;
        public int seat;
        public bool crouching;
        public Transform lockTarget;
        public float lockTime;
        public float lastHurt = -99, lastFire = -99, spawnTime;
        public Unit lastAttacker;
        public bool grounded;
        float airTime, beamSound, swingTimer, vaultCd;
        int fanShots; float fanTimer;
        Vector3 push;
        readonly List<Detpack> detpacks = new List<Detpack>();
        LineRenderer beamLine;
        AudioSource jetLoop;
        public float rallyUntil, frenzyUntil, cloakUntil, dashUntil, invulnUntil;
        public float spreadMul = 1;   // set by PlayerControl for the player
        public const float SniperHipSpread = 4f;   // degrees of wander on an unscoped sniper shot
        public const float SniperSettle = 2.2f, SniperSettleTime = 0.55f;   // a fresh scope wanders this much, steady after this long
        public const float SniperMoveSpread = 1.4f;                         // and moving while scoped wanders this much
        float scopedSince = -1;

        // -- battle royale: armour, supplies, keys, the grav-chute --
        public const int MaxMedkits = 5, MaxShields = 5;
        public const float MaxArmor = 150, MedkitTime = 3.2f, ShieldTime = 2.2f;
        public int medkits, shields, keys;
        public float armor;
        public float useT, useDur;           // using a medkit / shield cell: seconds left of the channel
        public LootKind useKind;
        public bool Using => useT > 0;
        public bool gliding;
        GameObject chute;

        // -- Wi-Fi play --
        // HostRemote: on the host, the guest's soldier (its position comes from the guest's device, everything else runs here).
        // Replica: on the guest, a copy of a host soldier drawn from snapshots. GuestLocal: on the guest, your own soldier
        // (moves here so it feels instant; shots, damage and abilities happen on the host).
        public enum NetMode : byte { None, HostRemote, Replica, GuestLocal }
        public NetMode net;
        public Vector3 netPos, netVel;
        public float netYaw, netPitch, netAge;
        public bool netGrounded;
        public int netFlags;          // 1 crouch, 2 grounded, 4 cloaked, 8 blocking, 16 in vehicle, 32 second weapon, 64 firing
        Vector3 dashDir;
        readonly HashSet<IDamageable> dashHit = new HashSet<IDamageable>();

        public int Team => team;
        public bool Alive => !dead && hp > 0;
        public bool IsVehicle => false;
        public bool IsHero => hero != null;
        public bool InVehicle => vehicle != null;
        public float Height => rig ? rig.height * (knocked ? 0.36f : crouching ? 0.68f : 1f) : 1.8f;
        public Vector3 AimPoint => transform.position + Vector3.up * Height * 0.62f;
        public Vector3 Eye => transform.position + Vector3.up * Height * 0.9f;
        public WeaponState Weapon => weapons[cur] ?? weapons[0];
        public bool Blocking => hero != null && hero.kind == HeroKind.Blade && input.aim && Alive && !InVehicle;
        public bool Cloaked => Time.time < cloakUntil;
        public float DamageBoost => (Time.time < frenzyUntil ? 1.3f : 1f) * (Time.time < rallyUntil ? 1.25f : 1f);
        public Role RoleOf => cls != null ? cls.role : Role.Assault;
        public float Speed => (hero != null ? hero.speed : cls.speed) * (Time.time < frenzyUntil ? 1.45f : 1f);
        public string ClassName => hero != null ? hero.name : cls.name;

        public void Init(Battle b, Participant p, FactionDef f, ClassDef c, HeroDef h)
        {
            battle = b; part = p; team = p.team; faction = f; cls = c; hero = h;
            displayName = p.name;
            maxHp = hp = h != null ? h.hp : c.hp;
            maxHp *= b.HealthMul(team); hp = maxHp;
            gameObject.layer = Layers.Unit;
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = 1.8f * (h != null ? h.scale : 1f); cc.radius = 0.4f; cc.center = new Vector3(0, cc.height / 2, 0);
            cc.slopeLimit = 50; cc.stepOffset = 0.5f; cc.skinWidth = 0.06f; cc.minMoveDistance = 0;
            if (h != null)
            {
                weapons[0] = new WeaponState(h.weapon);
                weapons[1] = null;
            }
            else
            {
                bool award = p.awards[(int)c.role] && c.award != null;
                weapons[0] = new WeaponState(award ? c.award : c.primary);
                weapons[1] = c.secondary != null ? new WeaponState(c.secondary) : null;
                equipLeft = c.equipCount;
            }
            rig = SoldierRig.Build(transform, f, c, h, weapons[0].def);
            yaw = transform.eulerAngles.y;
            spawnTime = Time.time;
            invulnUntil = Time.time + 1.5f;
            if (c != null && c.special == Special.Jetpack) jetLoop = Sfx.Loop("jet", transform, 0f, 40);
            if (h != null && h.kind == HeroKind.Blade) Sfx.Loop("bladehum", rig.gun, 0.12f, 18);
        }

        // ── damage ───────────────────────────────────────────────────────
        public void TakeDamage(DamageInfo d)
        {
            if (net == NetMode.Replica) return;
            if (net == NetMode.GuestLocal) { if (d.team == -1 && Alive) Net.NetClient.SelfDamage(null, d.amount, d.type); return; }
            if (!Alive || Time.time < invulnUntil) return;
            if (InVehicle && d.type != DmgType.Environment) return;
            float amt = d.amount;
            if (hero != null && d.type == DmgType.Explosive) amt *= 0.7f;
            // royale armour soaks hits first (the zone goes straight through it)
            if (armor > 0 && d.type != DmgType.Environment && !knocked) { float a = Mathf.Min(armor, amt); armor -= a; amt -= a; }
            hp -= amt;
            lastHurt = Time.time;
            if (d.attacker) lastAttacker = d.attacker;
            if (bot != null) bot.OnHurt(d);
            if (part.isPlayer) HUD.Hurt(d.point);
            else if (part.remote) Net.NetHost.Hurt(d.point);
            if (Cloaked) cloakUntil = Time.time + 0.4f;
            if (hp <= 0)
            {
                if (TryKnock(d)) return;                                          // duos: down, not out
                Die(d.attacker ? d.attacker : knocked ? knockedBy : null, d.dir, d.weapon);
            }
        }

        public void Heal(float amt)
        {
            if (!Alive) return;
            hp = Mathf.Min(maxHp, hp + amt);
        }

        public void Push(Vector3 v) { if (net == NetMode.HostRemote) Net.NetHost.Push(v); push += v; }

        public void Die(Unit killer, Vector3 dir, WeaponDef w = null)
        {
            if (dead || net == NetMode.Replica || net == NetMode.GuestLocal) return;
            dead = true; hp = 0;
            if (InVehicle) vehicle.Leave(this, false);
            Net.NetHost.OnUnitDied(this, killer, dir);
            cc.enabled = false;
            rig.Die(dir.sqrMagnitude > 0.01f ? dir : -transform.forward);
            rig.SetCloak(false);
            if (beamLine) beamLine.enabled = false;
            if (jetLoop) jetLoop.volume = 0;
            foreach (var d in detpacks) if (d) Destroy(d.gameObject);
            detpacks.Clear();
            if (chute) Destroy(chute);
            gliding = false; useT = 0;
            battle.OnUnitKilled(this, killer, w);
            if (!IsHero && battle.royale == null && Random.value < 0.35f) Pickup.Drop(transform.position + Vector3.up * 0.3f);
            Destroy(gameObject, 6f);
        }

        // ── per-frame ────────────────────────────────────────────────────
        public void Tick(float dt)
        {
            if (dead) { rig.Animate(0, true, false, 0, dt, false); return; }
            if (net == NetMode.Replica) { ReplicaTick(dt); return; }
            if (InVehicle) { vehicle.PlaceRider(this); return; }

            for (int i = 0; i < 2; i++) weapons[i]?.Tick(dt);
            abilityCd -= dt;

            // using a medkit or shield cell: firing or swapping weapons cancels it
            if (useT > 0 && net != NetMode.GuestLocal)
            {
                if (input.fire || input.swap || knocked) useT = 0;
                else { useT -= dt; if (useT <= 0) FinishUse(); }
            }
            if (net != NetMode.GuestLocal) { DownTick(dt); if (dead) return; }

            // hero health slowly drains (kills restore it)
            if (hero != null && net != NetMode.GuestLocal && !part.boss)
            {
                heroDrainAcc += dt;
                if (heroDrainAcc > 1) { heroDrainAcc -= 1; hp -= maxHp * 0.006f; if (hp <= 0) { Die(null, Vector3.zero); return; } }
            }

            // aim
            Vector3 aimDir = input.aimPoint - Eye;
            if (aimDir.sqrMagnitude < 0.01f) aimDir = transform.forward;
            aimDir.Normalize();
            float targetYaw = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, (part.Human ? 2000 : 540) * dt);
            pitch = -Mathf.Asin(Mathf.Clamp(aimDir.y, -1, 1)) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, yaw, 0);

            if (net == NetMode.HostRemote) NetFollow(dt); else Move(dt);
            if (net != NetMode.GuestLocal && !knocked) Actions(dt);
            else if (Weapon.def.kind == WKind.Beam) BeamVisual((netFlags & 64) != 0, (input.aimPoint - MuzzlePos).normalized);

            // rig + cloak
            Vector3 hv = new Vector3(velocity.x, 0, velocity.z);
            bool bladeSwing = swingTimer > 0 || (net == NetMode.GuestLocal && (netFlags & 64) != 0 && Weapon.def.kind == WKind.Blade);
            rig.Animate(hv.magnitude, grounded, crouching, pitch, dt, bladeSwing);
            rig.SetDown(knocked, Reviving, hv.magnitude, dt);
            swingTimer = 0;
            rig.SetCloak(Cloaked);
            if (!part.isPlayer) { input.swap = input.equip = input.ability = input.jumpDown = input.reload = input.use = false; }
        }

        void Move(float dt)
        {
            grounded = cc.isGrounded;
            if (gliding && Glide(dt)) return;
            crouching = input.crouch && grounded && hero == null;
            bool aimingSlow = input.aim && (Weapon.def.zoomFov > 0);
            float spd = Speed * (input.sprint && !crouching && !input.fire ? 1.45f : 1f) * (crouching ? 0.5f : 1f) * (aimingSlow ? 0.5f : 1f) * (Using ? 0.45f : 1f);
            if (knocked) spd = CrawlSpeed;                       // down: a crawl
            else if (Reviving || CallingIn) spd = 0;             // kneeling over a mate / at a revive station
            if (InSpaceZeroG()) spd *= 0.8f;
            // wading slows you down; lava burns
            if (battle.land != null && battle.land.LiquidAt(transform.position.x, transform.position.z, out float liq, out bool lava) && transform.position.y < liq - 0.15f)
            {
                if (lava) { lavaT += dt; if (lavaT > 0.25f) { lavaT = 0; TakeDamage(new DamageInfo { amount = 14, type = DmgType.Environment, team = -1, point = transform.position }); } }
                else spd *= transform.position.y < liq - 1.25f ? 0.35f : 0.55f;   // deep water: barely moving
            }
            Vector3 wish = input.move * spd;

            if (Time.time < dashUntil)
            {
                wish = dashDir * 22f;
                velocity.x = wish.x; velocity.z = wish.z;
                DashDamage();
            }
            Vector3 hv = new Vector3(velocity.x, 0, velocity.z);
            float acc = grounded ? 45f : 10f;
            hv = Vector3.MoveTowards(hv, new Vector3(wish.x, 0, wish.z), acc * dt);
            velocity.x = hv.x; velocity.z = hv.z;

            if (grounded) { airTime = 0; lastFooting = transform.position; if (velocity.y < 0) velocity.y = -3f; }
            else airTime += dt;
            if (input.jumpDown && grounded && !knocked) { velocity.y = hero != null && hero.kind == HeroKind.Blade ? 9.5f : 7.2f; grounded = false; }
            // vault: running into something waist-high (crate, cover wall, rock) hops over it
            vaultCd -= dt;
            Vector3 wishFlat = new Vector3(wish.x, 0, wish.z);
            if (grounded && vaultCd <= 0 && wishFlat.sqrMagnitude > 1f && Time.time >= dashUntil && !knocked)
            {
                Vector3 d = wishFlat.normalized, basePos = transform.position;
                const int vmask = (1 << Layers.Default) | (1 << Layers.Hull) | (1 << Layers.Vehicle);
                if (Physics.Raycast(basePos + Vector3.up * 0.55f, d, out var low, 0.95f, vmask, QueryTriggerInteraction.Ignore) && !low.collider.transform.IsChildOf(transform)
                    && !Physics.Raycast(basePos + Vector3.up * 1.9f, d, 1.6f, vmask, QueryTriggerInteraction.Ignore)
                    && !Physics.Raycast(basePos + Vector3.up * 0.2f, Vector3.up, 2.4f, vmask, QueryTriggerInteraction.Ignore)
                    // never hop a parapet into a long drop (tower floors, balconies)
                    && Physics.Raycast(basePos + d * 1.8f + Vector3.up * 1.5f, Vector3.down, 4f, Layers.MoveMask, QueryTriggerInteraction.Ignore))
                {
                    velocity.y = 6.6f; velocity += d * 1.5f; grounded = false; vaultCd = 0.7f;
                }
            }

            bool jetting = false;
            if (cls != null && cls.special == Special.Jetpack)
            {
                if (!grounded && input.jump && jetFuel > 0 && airTime > 0.15f && !knocked)
                {
                    jetting = true;
                    velocity.y = Mathf.MoveTowards(velocity.y, 7f, 30 * dt);
                    jetFuel -= dt * 0.42f;
                    if (Random.value < 0.6f) Fx.Emit(transform.position + Vector3.up * 0.9f - transform.forward * 0.3f, Vector3.down * 6 + Random.insideUnitSphere, new Color(1f, 0.6f, 0.25f, 1), 0.35f, 0.25f);
                }
                else if (grounded) jetFuel = Mathf.Min(1, jetFuel + dt * 0.3f);
                if (jetLoop) jetLoop.volume = Mathf.MoveTowards(jetLoop.volume, jetting ? 0.4f : 0f, dt * 4);
            }

            velocity.y += Physics.gravity.y * (jetting ? 0.3f : 1f) * dt;
            if (push.sqrMagnitude > 0.01f) { velocity += push; push = Vector3.zero; }
            if (cc.enabled) cc.Move(velocity * dt);

            // fell off the world / lost in space
            if (battle.space != null)
            {
                if (airTime > 3.5f && !jetting) { Kill("lost in space"); return; }
            }
            else if (battle.land != null && battle.land.Floating && transform.position.y < battle.land.KillY) { if (bot != null) { BotFalls++; BotFallFrom.Add(lastFooting); } Kill("fell from the isles"); return; }
            else if (battle.land != null && transform.position.y < battle.land.Height(transform.position.x, transform.position.z) - 8)
            {
                transform.position = battle.land.Ground(transform.position) + Vector3.up;
            }
        }

        /// <summary>Bots lost off the edge of a floating map (for checking the maps).</summary>
        public static int BotFalls;
        public static readonly List<Vector3> BotFallFrom = new List<Vector3>();
        Vector3 lastFooting;
        bool InSpaceZeroG() => false;
        float lavaT, netAirT;

        /// <summary>Host: the guest's soldier goes where the guest's device says; dashes and space falls are judged here.</summary>
        void NetFollow(float dt)
        {
            Vector3 p = transform.position;
            p = (netPos - p).sqrMagnitude > 9 ? netPos : Vector3.Lerp(p, netPos, 1 - Mathf.Exp(-25 * dt));
            transform.position = p;
            velocity = netVel;
            grounded = netGrounded;
            crouching = (netFlags & 1) != 0 && hero == null && grounded;
            push = Vector3.zero;
            NetGlideCheck();
            if (Time.time < dashUntil) DashDamage();
            if (battle.space != null)
            {
                bool jetting = cls != null && cls.special == Special.Jetpack && !grounded && velocity.y > 0;
                netAirT = grounded || jetting ? 0 : netAirT + dt;
                if (netAirT > 4f) Kill("lost in space");
            }
        }

        /// <summary>Guest: a host soldier, drawn where the snapshots say (a little ahead by its speed so it doesn't lag).</summary>
        void ReplicaTick(float dt)
        {
            netAge += dt;
            if ((netFlags & 4) != 0) cloakUntil = Time.time + 0.2f;
            if (InVehicle) { vehicle.PlaceRider(this); return; }
            Vector3 target = netPos + netVel * Mathf.Min(netAge, 0.25f);
            Vector3 p = transform.position;
            p = (target - p).sqrMagnitude > 25 ? target : Vector3.Lerp(p, target, 1 - Mathf.Exp(-14 * dt));
            transform.position = p;
            yaw = Mathf.LerpAngle(yaw, netYaw, 1 - Mathf.Exp(-16 * dt));
            pitch = Mathf.Lerp(pitch, netPitch, 1 - Mathf.Exp(-16 * dt));
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            velocity = netVel; grounded = netGrounded; crouching = (netFlags & 1) != 0;
            bool down = (netFlags2 & 1) != 0;
            if (down != knocked) { knocked = down; DownShape(down); }
            NetGlideCheck();
            int want = (netFlags & 32) != 0 && weapons[1] != null ? 1 : 0;
            if (want != cur) { cur = want; rig.SetWeapon(Weapon.def); }
            bool firing = (netFlags & 64) != 0;
            rig.Animate(new Vector3(velocity.x, 0, velocity.z).magnitude, grounded, crouching, pitch, dt, firing && Weapon.def.kind == WKind.Blade);
            rig.SetDown(knocked, (netFlags2 & 2) != 0, new Vector3(velocity.x, 0, velocity.z).magnitude, dt);
            rig.SetCloak(Cloaked);
            if (Weapon.def.kind == WKind.Beam) BeamVisual(firing, Quaternion.Euler(pitch, yaw, 0) * Vector3.forward);
            if (cls != null && cls.special == Special.Jetpack && !grounded && velocity.y > 1 && Random.value < 0.6f)
                Fx.Emit(transform.position + Vector3.up * 0.9f - transform.forward * 0.3f, Vector3.down * 6 + Random.insideUnitSphere, new Color(1f, 0.6f, 0.25f, 1), 0.35f, 0.25f);
        }

        /// <summary>Guest: the host says this soldier fell.</summary>
        public void ReplicaDie(Vector3 dir)
        {
            if (dead) return;
            dead = true; hp = 0;
            if (vehicle) { for (int i = 0; i < vehicle.riders.Length; i++) if (vehicle.riders[i] == this) vehicle.riders[i] = null; vehicle = null; }
            cc.enabled = false;
            rig.SetVisible(true);
            rig.Die(dir.sqrMagnitude > 0.01f ? dir : -transform.forward);
            rig.SetCloak(false);
            if (beamLine) beamLine.enabled = false;
            if (jetLoop) jetLoop.volume = 0;
            Destroy(gameObject, 6f);
        }

        /// <summary>Guest: start a Bladesworn dash on your own soldier at once (the host runs its damage).</summary>
        public void LocalDash()
        {
            dashDir = new Vector3(input.aimPoint.x - transform.position.x, 0, input.aimPoint.z - transform.position.z).normalized;
            if (dashDir.sqrMagnitude < 0.1f) dashDir = transform.forward;
            dashUntil = Time.time + 0.45f;
            abilityCd = AbilityCooldown;
            Sfx.Play("blade", transform.position, 0.8f, 0.7f);
        }

        void BeamVisual(bool on, Vector3 dir)
        {
            if (!beamLine)
            {
                beamLine = new GameObject("beam").AddComponent<LineRenderer>();
                beamLine.transform.SetParent(transform, false);
                beamLine.material = Mats.Additive(hero != null ? hero.glow : Color.cyan);
                beamLine.widthMultiplier = 0.18f; beamLine.positionCount = 8; beamLine.useWorldSpace = true;
            }
            beamLine.enabled = on;
            if (!on) return;
            Vector3 from = MuzzlePos;
            Vector3 end = from + dir * Weapon.def.range;
            if (Physics.Raycast(from, dir, out var hit, Weapon.def.range, Layers.ShotMask, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform)) end = hit.point;
            for (int i = 0; i < 8; i++)
            {
                Vector3 q = Vector3.Lerp(from, end, i / 7f);
                if (i > 0 && i < 7) q += Random.insideUnitSphere * 0.35f;
                beamLine.SetPosition(i, q);
            }
            beamSound -= Time.deltaTime;
            if (beamSound <= 0) { beamSound = 0.25f; Sfx.Play("zap", from, 0.4f, Random.Range(0.9f, 1.2f)); }
        }

        public void Kill(string why)
        {
            if (net == NetMode.GuestLocal) { if (Alive) Net.NetClient.SelfDamage(null, 99999, DmgType.Environment); return; }
            if (net == NetMode.Replica) return;
            if (!Alive) return;
            hp = 0;
            Die(null, Vector3.down);
        }

        void Actions(float dt)
        {
            var w = Weapon;
            if (input.swap && weapons[1] != null) { cur = 1 - cur; rig.SetWeapon(Weapon.def); w = Weapon; Sfx.Play("ui", transform.position, 0.3f); }
            // a scoped rifle reloads only with the scope down (people, not bots): zooming in stops a reload
            bool scoped = part.Human && w.def.zoomFov > 0 && input.aim;
            if (w.def.kind == WKind.Sniper && input.aim) { if (scopedSince < 0) scopedSince = Time.time; } else scopedSince = -1;
            if (scoped) { if (w.reload > 0) w.reload = 0; }
            else
            {
                if (input.reload) w.StartReload();
                if (w.NeedsReload) w.StartReload();
            }

            // lock-on for launchers
            if (w.def.lockOn) UpdateLock(dt, w.def); else { lockTarget = null; lockTime = 0; }

            // fan fire (gunslinger ability) runs on its own clock
            if (fanShots > 0)
            {
                fanTimer -= dt;
                if (fanTimer <= 0) { fanTimer = 0.06f; fanShots--; FireBolt(w.def, 1.0f, 2.5f); }
            }

            bool wantFire = w.def.auto ? input.fire : input.fireDown;
            if (w.def.kind == WKind.Beam) Beam(dt, input.fire && !w.overheated, w);
            else if (wantFire && w.Ready && !Blocking)
            {
                switch (w.def.kind)
                {
                    case WKind.Blade: Swing(w.def); break;
                    case WKind.Repair: RepairBeam(w.def, dt); break;
                    default:
                        for (int i = 0; i < w.def.pellets; i++) FireBolt(w.def, 1, w.def.spread);
                        break;
                }
                w.Spend();
                lastFire = Time.time;
                if (Cloaked) cloakUntil = 0;
            }

            if (input.equip) UseEquipment();
            if (input.ability && abilityCd <= 0) UseAbility();
        }

        void UpdateLock(float dt, WeaponDef w)
        {
            Transform best = null; float bestA = 7f;
            Vector3 dir = (input.aimPoint - Eye).normalized;
            foreach (var v in battle.vehicles)
            {
                if (!v || !v.Alive || v.Team == team || v.IsEmpty) continue;
                Vector3 to = v.AimPoint - Eye;
                float d = to.magnitude;
                if (d > w.range) continue;
                float a = Vector3.Angle(dir, to);
                if (a < bestA) { bestA = a; best = v.transform; }
            }
            if (best != lockTarget) { lockTarget = best; lockTime = 0; }
            else if (best) { float before = lockTime; lockTime += dt; if (part.isPlayer && before < 0.9f && lockTime >= 0.9f) Sfx.Play2D("lock", 0.8f); }
        }

        public Vector3 MuzzlePos => rig && rig.muzzle ? rig.muzzle.position : Eye + transform.forward * 0.5f;

        void FireBolt(WeaponDef w, float mul, float spread)
        {
            Vector3 from = MuzzlePos;
            Vector3 dir = (input.aimPoint - from);
            if (dir.sqrMagnitude < 1f) dir = (input.aimPoint - Eye);
            dir.Normalize();
            float sp = spread * spreadMul * (crouching ? 0.6f : 1f) * (new Vector3(velocity.x, 0, velocity.z).magnitude > 3 ? 1.3f : 1f) * (input.aim ? 0.7f : 1f);
            if (w.kind == WKind.Sniper)
            {
                if (!input.aim) sp += SniperHipSpread;     // a sniper is aimed through the scope, not fired from the hip
                else
                {
                    // the scope has to settle, and a moving shooter can't hold it still
                    float held = scopedSince < 0 ? 0 : Time.time - scopedSince;
                    sp += SniperSettle * (1 - Mathf.Clamp01(held / SniperSettleTime));
                    if (new Vector3(velocity.x, 0, velocity.z).magnitude > 1.2f) sp += SniperMoveSpread;
                }
            }
            if (sp > 0) dir = Quaternion.AngleAxis(Random.Range(0, 360f), dir) * Quaternion.AngleAxis(Random.Range(0, sp), Vector3.Cross(dir, Vector3.up).normalized) * dir;
            Transform homing = (w.lockOn && lockTime >= 0.9f) ? lockTarget : null;
            Shots.Fire(w, from, dir, team, this, transform, faction.bolt, mul, homing);
            Fx.Muzzle(from, faction.bolt);
            Sfx.Play(w.sound, from, part.isPlayer ? 0.7f : 0.45f, w.pitch);
        }

        void Swing(WeaponDef w)
        {
            swingTimer = 1;
            Sfx.Play("blade", transform.position, 0.6f, Random.Range(0.9f, 1.15f));
            Vector3 c = transform.position + transform.forward * 1.6f + Vector3.up * 1f;
            var cols = Physics.OverlapSphere(c, w.range * 0.7f, Layers.DamageMask, QueryTriggerInteraction.Ignore);
            var set = new HashSet<IDamageable>();
            foreach (var col in cols)
            {
                var d = col.GetComponentInParent<IDamageable>();
                if (d == null || d == (IDamageable)this || !d.Alive || d.Team == team || !set.Add(d)) continue;
                Shots.Deal(d, w.damage, w, this, team, d.AimPoint, transform.forward, DmgType.Melee);
                Fx.Sparks(d.AimPoint, -transform.forward, hero != null ? hero.glow : Color.white, 10);
                if (d is Unit u) u.Push(transform.forward * 6 + Vector3.up * 2);
            }
        }

        void DashDamage()
        {
            var cols = Physics.OverlapSphere(transform.position + Vector3.up, 1.6f, Layers.DamageMask, QueryTriggerInteraction.Ignore);
            foreach (var col in cols)
            {
                var d = col.GetComponentInParent<IDamageable>();
                if (d == null || d == (IDamageable)this || !d.Alive || d.Team == team || !dashHit.Add(d)) continue;
                Shots.Deal(d, 180, weapons[0].def, this, team, d.AimPoint, dashDir, DmgType.Melee);
                Fx.Sparks(d.AimPoint, -dashDir, hero.glow, 14);
                if (d is Unit u) u.Push(dashDir * 10 + Vector3.up * 5);
            }
        }

        void Beam(float dt, bool on, WeaponState ws)
        {
            if (!beamLine)
            {
                beamLine = new GameObject("beam").AddComponent<LineRenderer>();
                beamLine.transform.SetParent(transform, false);
                beamLine.material = Mats.Additive(hero != null ? hero.glow : Color.cyan);
                beamLine.widthMultiplier = 0.18f; beamLine.positionCount = 8; beamLine.useWorldSpace = true;
            }
            if (!on) { beamLine.enabled = false; return; }
            var w = ws.def;
            ws.heat += w.heatPerShot * dt * 10; if (ws.heat >= 1) { ws.heat = 1; ws.overheated = true; }
            lastFire = Time.time;
            Vector3 from = MuzzlePos;
            Vector3 dir = (input.aimPoint - from).normalized;
            // find a target near the beam
            IDamageable best = null; float bestScore = 999;
            foreach (var u in battle.units)
            {
                if (!u || !u.Alive || u.team == team || u.InVehicle) continue;
                Vector3 to = u.AimPoint - from; float d = to.magnitude;
                if (d > w.range) continue;
                float a = Vector3.Angle(dir, to);
                if (a < 14 && a + d * 0.3f < bestScore) { bestScore = a + d * 0.3f; best = u; }
            }
            foreach (var v in battle.vehicles)
            {
                if (!v || !v.Alive || v.Team == team) continue;
                Vector3 to = v.AimPoint - from; float d = to.magnitude;
                if (d > w.range + 3) continue;
                float a = Vector3.Angle(dir, to);
                if (a < 10 && a + d * 0.3f < bestScore) { bestScore = a + d * 0.3f; best = v; }
            }
            Vector3 end = from + dir * w.range;
            if (best != null)
            {
                end = best.AimPoint;
                Shots.Deal(best, w.damage * dt, w, this, team, end, dir, DmgType.Psi);
                // chain to one more nearby enemy
                foreach (var u in battle.units)
                {
                    if (!u || !u.Alive || u.team == team || (IDamageable)u == best) continue;
                    if ((u.AimPoint - end).sqrMagnitude < 36) { Shots.Deal(u, w.damage * 0.5f * dt, w, this, team, u.AimPoint, dir, DmgType.Psi); break; }
                }
                if (Random.value < 0.3f) Fx.Sparks(end, -dir, hero != null ? hero.glow : Color.cyan, 2);
            }
            else if (Physics.Raycast(from, dir, out var hit, w.range, Layers.SolidMask)) end = hit.point;
            beamLine.enabled = true;
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f;
                Vector3 p = Vector3.Lerp(from, end, t);
                if (i > 0 && i < 7) p += Random.insideUnitSphere * 0.35f;
                beamLine.SetPosition(i, p);
            }
            beamSound -= dt;
            if (beamSound <= 0) { beamSound = 0.25f; Sfx.Play("zap", from, 0.4f, Random.Range(0.9f, 1.2f)); }
        }

        void RepairBeam(WeaponDef w, float dt)
        {
            Vector3 from = Eye;
            Vector3 dir = (input.aimPoint - from).normalized;
            if (Physics.SphereCast(from, 0.3f, dir, out var hit, w.range, Layers.ShotMask, QueryTriggerInteraction.Ignore))
            {
                var rep = hit.collider.GetComponentInParent<IRepairable>();
                if (rep != null && rep.Team == team && rep.CanRepair)
                {
                    float done = rep.Repair(w.damage / w.rate * 1f);
                    if (done > 0) battle.AddRepair(part, done);
                    Fx.Sparks(hit.point, hit.normal, new Color(0.4f, 1f, 0.5f), 3);
                    Sfx.Play("repair", hit.point, 0.4f);
                }
            }
        }

        void UseEquipment()
        {
            if (cls == null || equipLeft <= 0)
            {
                if (cls != null && cls.equip == Equip.Detpack && detpacks.Count > 0) DetonateAll();
                return;
            }
            switch (cls.equip)
            {
                case Equip.Grenade:
                    var g = new WeaponDef { name = "Grenade", kind = WKind.Grenade, damage = 0, speed = 19, range = 60, splash = 6, splashDmg = 260, vsVehicle = 0.6f };
                    Vector3 d = (input.aimPoint - Eye).normalized; d = (d + Vector3.up * 0.35f).normalized;
                    Shots.Fire(g, Eye + transform.forward * 0.4f, d, team, this, transform, faction.bolt, DamageBoost);
                    equipLeft--;
                    break;
                case Equip.Mine:
                    if (grounded) { Mine.Place(this, transform.position + transform.forward * 1.2f); equipLeft--; }
                    break;
                case Equip.Detpack:
                    if (detpacks.Count > 0 && equipLeft <= 0) { DetonateAll(); return; }
                    var dp = Detpack.Throw(this, Eye + transform.forward * 0.5f, (input.aimPoint - Eye).normalized);
                    detpacks.Add(dp);
                    equipLeft--;
                    break;
                case Equip.Supply:
                    SupplyCrate.Place(this, transform.position + transform.forward * 1.4f);
                    equipLeft--;
                    break;
            }
            Sfx.Play("thud", transform.position, 0.4f, 1.3f);
        }

        public void DetonateAll()
        {
            foreach (var d in detpacks) if (d) d.Blow();
            detpacks.Clear();
        }

        public bool HasDetpacksOut => detpacks.Count > 0;

        public float AbilityCooldown => hero != null ? 12f : cls.special == Special.Rally ? 30f : 25f;
        public string AbilityName => hero != null ? hero.abilityName : cls.special == Special.None || cls.special == Special.Jetpack ? null : cls.special.ToString().ToUpper();

        void UseAbility()
        {
            if (hero != null)
            {
                abilityCd = AbilityCooldown;
                switch (hero.kind)
                {
                    case HeroKind.Blade:
                        dashDir = (new Vector3(input.aimPoint.x - transform.position.x, 0, input.aimPoint.z - transform.position.z)).normalized;
                        if (dashDir.sqrMagnitude < 0.1f) dashDir = transform.forward;
                        dashUntil = Time.time + 0.45f; dashHit.Clear();
                        Sfx.Play("blade", transform.position, 0.8f, 0.7f);
                        break;
                    case HeroKind.Gunslinger:
                        fanShots = 12; fanTimer = 0;
                        break;
                    case HeroKind.Juggernaut:
                        var rk = W.Rocket("Barrage"); rk.damage = 160; rk.splashDmg = 140; rk.lockOn = false;
                        for (int i = 0; i < 6; i++)
                        {
                            Vector3 d = (input.aimPoint - Eye).normalized + Random.insideUnitSphere * 0.06f + Vector3.up * 0.03f * i;
                            Shots.Fire(rk, Eye + Vector3.up * 0.4f + transform.right * (i % 2 == 0 ? 0.4f : -0.4f), d, team, this, transform, faction.bolt);
                        }
                        Sfx.Play("rocket", transform.position, 0.9f, 0.8f);
                        break;
                    case HeroKind.Mystic:
                        Vector3 fwd = transform.forward;
                        foreach (var u in battle.units)
                        {
                            if (!u || !u.Alive || u.team == team || u.InVehicle) continue;
                            Vector3 to = u.transform.position - transform.position;
                            if (to.magnitude < 14 && Vector3.Angle(fwd, to) < 50)
                            {
                                Shots.Deal(u, 150, weapons[0].def, this, team, u.AimPoint, to.normalized, DmgType.Psi);
                                u.Push(to.normalized * 16 + Vector3.up * 7);
                            }
                        }
                        for (int i = 0; i < 40; i++)
                            Fx.Emit(Eye + fwd, (fwd + Random.insideUnitSphere * 0.5f).normalized * Random.Range(10, 22), hero.glow, 0.6f, 0.4f);
                        Sfx.Play("zap", transform.position, 1f, 0.6f);
                        break;
                }
                return;
            }
            switch (cls.special)
            {
                case Special.Frenzy:
                    frenzyUntil = Time.time + 6; abilityCd = AbilityCooldown;
                    Sfx.Play("award", transform.position, 0.5f, 0.6f);
                    break;
                case Special.Cloak:
                    cloakUntil = Time.time + 8; abilityCd = AbilityCooldown;
                    Sfx.Play("zap", transform.position, 0.5f, 1.6f);
                    break;
                case Special.Rally:
                    abilityCd = AbilityCooldown;
                    foreach (var u in battle.units)
                        if (u && u.Alive && u.team == team && (u.transform.position - transform.position).sqrMagnitude < 100)
                        { u.Heal(150); u.rallyUntil = Time.time + 8; Fx.Burst(u.AimPoint, faction.bolt, 10, 3, 0.4f, 0.6f); }
                    Sfx.Play("award", transform.position, 0.6f, 1.2f);
                    break;
            }
        }

        // ── battle royale ────────────────────────────────────────────────
        /// <summary>Out of the dropship: fall fast under a grav-chute, steer with the move stick, slow near the ground.</summary>
        public void StartGlide()
        {
            gliding = true;
            velocity = new Vector3(0, -18, 0);
            var k = new MeshKit();
            Color c = faction.bolt;
            k.Sphere(new Vector3(0, 0, 0), new Vector3(2.4f, 0.6f, 1.8f), Quaternion.identity, Color.Lerp(c, Color.white, 0.35f), 10, 4);
            k.Ring(new Vector3(0, -0.1f, 0), 1.1f, 1.22f, MeshKit.Glow(c), 20);
            for (int i = 0; i < 4; i++) k.Box(new Vector3(i % 2 == 0 ? -0.55f : 0.55f, -0.85f, i < 2 ? -0.38f : 0.38f), new Vector3(0.025f, 1.5f, 0.025f), Quaternion.Euler(i < 2 ? -14 : 14, 0, i % 2 == 0 ? -22 : 22), MeshKit.Glow(c * 0.6f));
            chute = k.Make("chute", transform, false);
            chute.transform.localPosition = Vector3.up * 3.1f;
            invulnUntil = Time.time + 2f;
        }

        bool Glide(float dt)
        {
            float agl = transform.position.y - battle.GroundY(transform.position);
            if (grounded || agl < 0.8f) { EndGlide(); velocity.y = -2; return false; }
            Vector3 wish = input.move * 17f;
            velocity.x = Mathf.MoveTowards(velocity.x, wish.x, 22 * dt);
            velocity.z = Mathf.MoveTowards(velocity.z, wish.z, 22 * dt);
            velocity.y = Mathf.MoveTowards(velocity.y, agl > 26 ? -24f : -6.5f, 30 * dt);
            if (cc.enabled) cc.Move(velocity * dt);
            if (chute)
            {
                Vector3 hv = new Vector3(velocity.x, 0, velocity.z);
                Vector3 tilt = Vector3.Cross(Vector3.up, hv) * 1.4f;
                chute.transform.rotation = Quaternion.Euler(tilt.x, transform.eulerAngles.y, tilt.z);
            }
            return true;
        }

        public void EndGlide()
        {
            if (!gliding) return;
            gliding = false;
            if (chute) Destroy(chute);
            Fx.Smoke(transform.position, 2f, 1.2f);
            Sfx.Play("thud", transform.position, 0.6f, 0.8f);
        }

        /// <summary>Copies of a soldier (Wi-Fi) fold their chute once the real one is down.</summary>
        void NetGlideCheck()
        {
            if (gliding && (grounded || transform.position.y - battle.GroundY(transform.position) < 1.2f)) EndGlide();
        }

        /// <summary>Start a medkit (if hurt) or a shield cell (if the armour isn't full). Returns false if there is nothing to use.</summary>
        public bool UseBest()
        {
            if (Using || !Alive || InVehicle || knocked) return false;
            if (medkits > 0 && hp < maxHp - 1) { useKind = LootKind.Medkit; useT = useDur = MedkitTime; }
            else if (shields > 0 && armor < MaxArmor - 1) { useKind = LootKind.Shield; useT = useDur = ShieldTime; }
            else return false;
            Sfx.Play("repair", transform.position, 0.5f, useKind == LootKind.Medkit ? 0.8f : 1.3f);
            return true;
        }

        void FinishUse()
        {
            if (useKind == LootKind.Medkit && medkits > 0) { medkits--; Heal(maxHp * 0.65f); Fx.Burst(AimPoint, new Color(0.4f, 1f, 0.5f), 12, 2, 0.25f, 0.6f); }
            else if (useKind == LootKind.Shield && shields > 0) { shields--; armor = Mathf.Min(MaxArmor, armor + 75); Fx.Burst(AimPoint, new Color(0.4f, 0.75f, 1f), 12, 2, 0.25f, 0.6f); }
            else FinishDownUse();
            if (part.isPlayer) Sfx.Play2D("ui2", 0.6f);
        }

        public void Resupply()
        {
            foreach (var w in weapons) w?.Refill();
            if (cls != null) equipLeft = Mathf.Max(equipLeft, cls.equipCount);
        }
    }

    public interface IRepairable
    {
        int Team { get; }
        bool CanRepair { get; }
        float Repair(float amount);   // returns hp actually restored
    }
}
