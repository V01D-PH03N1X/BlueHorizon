using BlueHorizon.Player;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Movement
{
    public struct MotorState : INetworkSerializable
    {
        public Vector3 position;
        public Vector3 velocity;
        public bool grounded;
        /// <summary>0 = standing, 1 = fully crouched.</summary>
        public float crouch;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref grounded);
            serializer.SerializeValue(ref crouch);
        }
    }

    public struct MotorContext
    {
        public bool canMove;
    }

    /// <summary>
    /// Deterministic-enough first-person movement step shared by the owning client (prediction) and the server
    /// (authority). It mutates only the given state and CharacterController, so it can be replayed during
    /// reconciliation.
    /// </summary>
    public static class PlayerMotor
    {
        static readonly Collider[] OverlapBuffer = new Collider[8];

        public static Vector3 Forward(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        public static Vector3 Right(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
        }

        public static float Height(MovementSettings m, float crouch) => Mathf.Lerp(m.standingHeight, m.crouchHeight, crouch);

        public static float EyeHeight(MovementSettings m, float crouch) => Mathf.Lerp(m.standingEyeHeight, m.crouchEyeHeight, crouch);

        public static bool IsSprinting(in PlayerInputCommand cmd, in MotorState state) =>
            cmd.Has(InputButtons.Sprint) && cmd.moveY > 40 && state.crouch < 0.5f && state.grounded;

        public static void ApplyShape(CharacterController cc, MovementSettings m, float crouch)
        {
            float h = Height(m, crouch);
            cc.height = h;
            cc.radius = m.radius;
            cc.center = new Vector3(0f, h * 0.5f, 0f);
        }

        public static void Step(ref MotorState s, in PlayerInputCommand cmd, float dt, MovementSettings m, CharacterController cc, in MotorContext ctx)
        {
            Transform t = cc.transform;

            // Crouch: stand up only when there is headroom.
            bool wantsCrouch = cmd.Has(InputButtons.Crouch) && ctx.canMove;
            float targetCrouch = wantsCrouch ? 1f : 0f;
            if (!wantsCrouch && s.crouch > 0f && !HasHeadroom(t.position, m, cc))
                targetCrouch = s.crouch;
            s.crouch = Mathf.MoveTowards(s.crouch, targetCrouch, m.crouchTransitionRate * dt);
            ApplyShape(cc, m, s.crouch);

            // Horizontal intent.
            Vector2 move = ctx.canMove ? cmd.Move : Vector2.zero;
            Vector3 wish = Right(cmd.yaw) * move.x + Forward(cmd.yaw) * move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = s.crouch > 0.5f ? m.crouchSpeed
                : IsSprinting(cmd, s) ? m.sprintSpeed
                : m.runSpeed;
            Vector3 targetVelocity = wish * speed;

            Vector3 horizontal = new(s.velocity.x, 0f, s.velocity.z);
            float accel = s.grounded
                ? (wish.sqrMagnitude > 0.0001f ? m.groundAcceleration : m.groundDeceleration)
                : m.airAcceleration;
            horizontal = Vector3.MoveTowards(horizontal, targetVelocity, accel * dt);

            // Vertical.
            float vy = s.velocity.y;
            if (s.grounded && ctx.canMove && cmd.Has(InputButtons.Jump) && s.crouch < 0.5f)
            {
                vy = Mathf.Sqrt(2f * m.gravity * m.jumpHeight);
                s.grounded = false;
            }
            vy = Mathf.Max(vy - m.gravity * dt, -m.maxFallSpeed);

            Vector3 velocity = new(horizontal.x, vy, horizontal.z);
            Vector3 before = t.position;
            CollisionFlags flags = cc.Move(velocity * dt);
            Vector3 after = t.position;

            s.grounded = (flags & CollisionFlags.Below) != 0;
            if (s.grounded && vy < 0f) vy = -2f;
            if ((flags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;

            // Kill velocity into walls so it does not accumulate.
            Vector3 actual = (after - before) / Mathf.Max(dt, 1e-5f);
            if ((flags & CollisionFlags.Sides) != 0)
            {
                horizontal.x = actual.x;
                horizontal.z = actual.z;
            }

            s.velocity = new Vector3(horizontal.x, vy, horizontal.z);
            s.position = after;
        }

        static bool HasHeadroom(Vector3 feet, MovementSettings m, CharacterController cc)
        {
            float r = m.radius * 0.95f;
            Vector3 bottom = feet + Vector3.up * (r + 0.05f);
            Vector3 top = feet + Vector3.up * (m.standingHeight - r);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, OverlapBuffer, Core.GameLayers.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (OverlapBuffer[i] != cc) return false;
            }
            return true;
        }

        /// <summary>Moves the controller without simulation (spawn, reconciliation).</summary>
        public static void Teleport(CharacterController cc, Vector3 position)
        {
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            cc.transform.position = position;
            cc.enabled = wasEnabled;
        }
    }
}
