using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Weapons
{
    /// <summary>Replicated, predicted weapon state for both slots. Times are in player simulation seconds.</summary>
    public struct WeaponSimState : INetworkSerializable
    {
        public byte activeSlot;
        public short magazine0, magazine1;
        public short reserve0, reserve1;
        public float nextFireTime;
        /// <summary>0 when not reloading.</summary>
        public float reloadEndTime;
        public float equipEndTime;
        public byte sprayCount;
        public float lastShotTime;
        public float recoilPitch;
        public float recoilYaw;
        public bool triggerWasHeld;

        public short Magazine(int slot) => slot == 0 ? magazine0 : magazine1;
        public short Reserve(int slot) => slot == 0 ? reserve0 : reserve1;

        public void SetMagazine(int slot, short value)
        {
            if (slot == 0) magazine0 = value; else magazine1 = value;
        }

        public void SetReserve(int slot, short value)
        {
            if (slot == 0) reserve0 = value; else reserve1 = value;
        }

        public bool IsReloading => reloadEndTime > 0f;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref activeSlot);
            serializer.SerializeValue(ref magazine0);
            serializer.SerializeValue(ref magazine1);
            serializer.SerializeValue(ref reserve0);
            serializer.SerializeValue(ref reserve1);
            serializer.SerializeValue(ref nextFireTime);
            serializer.SerializeValue(ref reloadEndTime);
            serializer.SerializeValue(ref equipEndTime);
            serializer.SerializeValue(ref sprayCount);
            serializer.SerializeValue(ref lastShotTime);
            serializer.SerializeValue(ref recoilPitch);
            serializer.SerializeValue(ref recoilYaw);
            serializer.SerializeValue(ref triggerWasHeld);
        }
    }

    public struct WeaponContext
    {
        public bool canFire;
        public bool sprinting;
        public bool grounded;
        public bool crouched;
        /// <summary>0..1 horizontal speed relative to run speed.</summary>
        public float moveFraction;
    }

    public struct ShotEvent
    {
        public bool fired;
        public byte slot;
        public Vector3 direction;
        public float spreadAngle;
    }

    /// <summary>
    /// Weapon handling rules: cadence, ammunition, reload, switching, spread and recoil.
    /// Runs identically on the owning client (prediction, for immediate feedback) and on the server (authority).
    /// Spread uses a seed derived from the command sequence so both sides compute the same shot direction.
    /// </summary>
    public static class WeaponSimulation
    {
        public static WeaponSimState Fresh(WeaponDefinition primary, WeaponDefinition secondary)
        {
            var s = new WeaponSimState
            {
                activeSlot = (byte)(primary != null ? 0 : 1),
                magazine0 = (short)(primary != null ? primary.magazineSize : 0),
                reserve0 = (short)(primary != null ? primary.reserveAmmo : 0),
                magazine1 = (short)(secondary != null ? secondary.magazineSize : 0),
                reserve1 = (short)(secondary != null ? secondary.reserveAmmo : 0),
            };
            return s;
        }

        public static void Step(ref WeaponSimState s, in PlayerInputCommand cmd, float simTime, float dt,
            in WeaponStats primary, in WeaponStats secondary, in WeaponContext ctx, out ShotEvent shot)
        {
            shot = default;

            // Weapon switching.
            int requested = cmd.Has(InputButtons.Weapon1) ? 0 : cmd.Has(InputButtons.Weapon2) ? 1 : -1;
            if (requested >= 0 && requested != s.activeSlot && Get(requested, primary, secondary).Definition != null)
            {
                s.activeSlot = (byte)requested;
                s.reloadEndTime = 0f;
                s.sprayCount = 0;
                s.equipEndTime = simTime + Get(requested, primary, secondary).Definition.equipTime;
            }

            WeaponStats stats = Get(s.activeSlot, primary, secondary);
            WeaponDefinition def = stats.Definition;
            if (def == null)
                return;

            int slot = s.activeSlot;
            bool equipping = simTime < s.equipEndTime;

            // Reload completion.
            if (s.IsReloading && simTime >= s.reloadEndTime)
            {
                int needed = def.magazineSize - s.Magazine(slot);
                int moved = Mathf.Min(needed, s.Reserve(slot));
                s.SetMagazine(slot, (short)(s.Magazine(slot) + moved));
                s.SetReserve(slot, (short)(s.Reserve(slot) - moved));
                s.reloadEndTime = 0f;
            }

            // Reload request (manual, or automatic on an empty magazine).
            bool canReload = !s.IsReloading && !equipping && s.Magazine(slot) < def.magazineSize && s.Reserve(slot) > 0;
            if (canReload && (cmd.Has(InputButtons.Reload) || s.Magazine(slot) == 0))
            {
                s.reloadEndTime = simTime + def.reloadTime;
                s.sprayCount = 0;
            }

            // Recoil recovery and spray reset.
            if (simTime - s.lastShotTime > def.recoil.resetTime)
                s.sprayCount = 0;

            bool held = cmd.Has(InputButtons.Fire);
            bool trigger = held && (def.fireMode == FireMode.Automatic || !s.triggerWasHeld);
            s.triggerWasHeld = held;

            bool ready = ctx.canFire
                         && !equipping
                         && !s.IsReloading
                         && s.Magazine(slot) > 0
                         && simTime >= s.nextFireTime
                         && !(ctx.sprinting && def.blockFireWhileSprinting);

            if (trigger && ready)
            {
                float interval = def.FireInterval;
                // Continuous fire keeps exact cadence across tick quantization; otherwise restart the clock.
                s.nextFireTime = simTime - s.nextFireTime < dt ? s.nextFireTime + interval : simTime + interval;
                s.SetMagazine(slot, (short)(s.Magazine(slot) - 1));

                float spread = SpreadAngle(def, stats, s.sprayCount, ctx);
                Quaternion view = ViewRotation(cmd.yaw + s.recoilYaw, cmd.pitch + s.recoilPitch);
                Vector3 dir = ApplySpread(view, spread, cmd.sequence);

                shot = new ShotEvent { fired = true, slot = (byte)slot, direction = dir, spreadAngle = spread };

                // Recoil applies to the next shot and to the camera.
                float recoilScale = stats.RecoilMultiplier;
                s.recoilPitch = Mathf.Min(s.recoilPitch + def.recoil.verticalPerShot * recoilScale, def.recoil.maxVertical);
                s.recoilYaw = def.recoil.horizontalAmplitude * recoilScale * Mathf.Sin(s.sprayCount * 0.9f);
                if (s.sprayCount < 255) s.sprayCount++;
                s.lastShotTime = simTime;
            }
            else
            {
                float recover = def.recoil.recoveryRate * dt;
                s.recoilPitch = Mathf.MoveTowards(s.recoilPitch, 0f, recover);
                s.recoilYaw = Mathf.MoveTowards(s.recoilYaw, 0f, recover);
            }
        }

        public static WeaponStats Get(int slot, in WeaponStats primary, in WeaponStats secondary) =>
            slot == 0 ? primary : secondary;

        public static float SpreadAngle(WeaponDefinition def, in WeaponStats stats, int sprayCount, in WeaponContext ctx)
        {
            SpreadSettings sp = def.spread;
            float angle = sp.baseSpread
                          + sp.movingSpread * Mathf.Clamp01(ctx.moveFraction)
                          + (ctx.grounded ? 0f : sp.airborneSpread)
                          + Mathf.Min(sp.bloomPerShot * sprayCount, sp.maxBloom);
            if (ctx.crouched) angle *= sp.crouchMultiplier;
            return angle * stats.SpreadMultiplier;
        }

        /// <summary>Pitch is positive upwards.</summary>
        public static Quaternion ViewRotation(float yaw, float pitch) => Quaternion.Euler(-pitch, yaw, 0f);

        public static Vector3 ApplySpread(Quaternion view, float spreadDegrees, uint seed)
        {
            if (spreadDegrees <= 0f)
                return view * Vector3.forward;
            // Deterministic hash → uniform point in a disc → cone deviation.
            uint h = Hash(seed);
            float u = (h & 0xFFFF) / 65535f;
            float v = (h >> 16) / 65535f;
            float r = Mathf.Sqrt(u) * spreadDegrees;
            float theta = v * Mathf.PI * 2f;
            Quaternion deviation = Quaternion.Euler(r * Mathf.Sin(theta), r * Mathf.Cos(theta), 0f);
            return (view * deviation) * Vector3.forward;
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }
    }
}
