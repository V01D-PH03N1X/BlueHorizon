using System;
using System.Collections.Generic;
using BlueHorizon.Abilities;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.Match;
using BlueHorizon.Movement;
using BlueHorizon.Testing;
using BlueHorizon.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace BlueHorizon.Player
{
    /// <summary>
    /// Server-authoritative character simulation with owner-side prediction.
    ///
    /// Owner client, every network tick: sample input → simulate locally (prediction) → store in history →
    /// send the last <see cref="InputBundle.Capacity"/> commands unreliably.
    /// Server, every network tick: process queued commands (bounded by a token budget so a client cannot
    /// simulate faster than real time) → movement, weapons, abilities → send authoritative state to the owner
    /// and snapshots to everyone else.
    /// Owner, on authoritative state: compare with the predicted state for that command; on mismatch, rewind
    /// to the server state and replay the unacknowledged commands.
    /// Other clients: interpolate snapshots <see cref="InterpolationDelay"/> seconds in the past.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerSimulation : NetworkBehaviour
    {
        const int HistorySize = 256;
        const int MaxQueuedCommands = 24;
        const int MaxCommandBudget = 6;
        const int MaxCommandsPerTick = 3;
        const int RemoteSnapshotInterval = 2;
        const int SnapshotBufferSize = 32;
        public const float InterpolationDelay = 0.1f;
        /// <summary>
        /// Clients send input every tick, so silence means the client is gone. Disconnecting after this long stops the
        /// server from streaming to a dead endpoint for the full 30 s transport timeout (see RISKS.md, R-07).
        /// </summary>
        const float InputSilenceTimeout = 5f;
        /// <summary>Server ticks without input before a starved character keeps falling (gravity only).</summary>
        const int StarvationTicks = 6;
        /// <summary>Input RPCs per second above which the excess is dropped and logged (clients send ~60).</summary>
        const int MaxInputRpcsPerSecond = 180;
        const float ExtrapolationLimit = 0.1f;
        const float CorrectionSmoothing = 15f;

        struct HistoryEntry
        {
            public PlayerInputCommand command;
            public MotorState motor;
            public WeaponSimState weapons;
            public float simTime;
        }

        public struct OwnerState : INetworkSerializable
        {
            public uint lastSequence;
            public byte teleportId;
            public float simTime;
            public float yaw;
            public MotorState motor;
            public WeaponSimState weapons;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref lastSequence);
                serializer.SerializeValue(ref teleportId);
                serializer.SerializeValue(ref simTime);
                serializer.SerializeValue(ref yaw);
                serializer.SerializeNetworkSerializable(ref motor);
                serializer.SerializeNetworkSerializable(ref weapons);
            }
        }

        public struct RemoteSnapshot : INetworkSerializable
        {
            public double time;
            public byte teleportId;
            public Vector3 position;
            public float yaw;
            public float pitch;
            public float crouch;
            public byte activeSlot;
            public bool grounded;
            public bool reloading;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref time);
                serializer.SerializeValue(ref teleportId);
                serializer.SerializeValue(ref position);
                serializer.SerializeValue(ref yaw);
                serializer.SerializeValue(ref pitch);
                serializer.SerializeValue(ref crouch);
                serializer.SerializeValue(ref activeSlot);
                serializer.SerializeValue(ref grounded);
                serializer.SerializeValue(ref reloading);
            }
        }

        CharacterController _cc;
        PlayerAvatar _avatar;
        PlayerRig _rig;
        IPlayerInputSource _input;

        MotorState _motor;
        WeaponSimState _weapons;
        float _simTime;
        byte _teleportId;
        PlayerInputCommand _lastCommand;
        InputButtons _previousButtons;

        // Owner prediction.
        readonly HistoryEntry[] _history = new HistoryEntry[HistorySize];
        readonly PlayerInputCommand[] _recentCommands = new PlayerInputCommand[InputBundle.Capacity];
        uint _nextSequence = 1;
        uint _lastAckSequence;
        Vector3 _renderFrom, _renderTo;
        float _renderTickTime;

        // Server.
        readonly Queue<PlayerInputCommand> _queue = new();
        uint _lastReceivedSequence;
        uint _lastProcessedSequence;
        int _budget;
        int _ticksSinceSnapshot;
        double _lastInputTime;
        readonly PoseHistory _poses = new();
        double _lastViewTime;
        double _currentViewTime;
        int _starvedTicks;
        int _rpcWindowCount;
        double _rpcWindowStart;
        double _lastAnomalyLog;
        Vector3 _correctionOffset;

        // Remote interpolation.
        readonly RemoteSnapshot[] _snapshots = new RemoteSnapshot[SnapshotBufferSize];
        int _snapshotCount;
        float _remoteYaw, _remotePitch;
        byte _remoteSlot;
        bool _remoteReloading;

        public int CorrectionCount { get; private set; }
        /// <summary>Diagnostics: input bundles received (server) / authoritative states received (owner).</summary>
        public int MessagesReceived { get; private set; }
        public MovementSettings Movement => _avatar.Agent != null ? _avatar.Agent.movement : DefaultMovement;
        static readonly MovementSettings DefaultMovement = new();

        public Vector3 Position => _motor.position;
        public Vector3 Velocity => _motor.velocity;
        public bool Grounded => _motor.grounded;
        public float Crouch => _motor.crouch;
        public WeaponSimState Weapons => _weapons;
        public byte ActiveSlot => IsOwner || IsServer ? _weapons.activeSlot : _remoteSlot;
        public bool IsReloading => IsOwner || IsServer ? _weapons.IsReloading : _remoteReloading;
        public float SimTime => _simTime;

        /// <summary>Server: the Interact button is held in the latest processed command.</summary>
        public bool InteractHeld { get; private set; }

        /// <summary>View angles for presentation: live input for the owner, interpolated for others.</summary>
        public float ViewYaw => IsOwner && _input != null ? _input.Yaw + _weapons.recoilYaw : IsServer ? _lastCommand.yaw : _remoteYaw;
        public float ViewPitch => IsOwner && _input != null ? _input.Pitch + _weapons.recoilPitch : IsServer ? _lastCommand.pitch : _remotePitch;

        public Vector3 EyePosition => _motor.position + Vector3.up * PlayerMotor.EyeHeight(Movement, _motor.crouch);

        /// <summary>Smoothed eye position for the owner's camera (interpolated between predicted ticks).</summary>
        public Vector3 RenderEyePosition
        {
            get
            {
                float dt = TickInterval;
                float alpha = Mathf.Clamp01((Time.time - _renderTickTime) / dt);
                return Vector3.Lerp(_renderFrom, _renderTo, alpha) + _correctionOffset + Vector3.up * PlayerMotor.EyeHeight(Movement, _motor.crouch);
            }
        }

        public Vector3 AimDirection => WeaponSimulation.ViewRotation(_lastCommand.yaw, _lastCommand.pitch) * Vector3.forward;

        /// <summary>Owner: a predicted shot was fired this tick (play local effects).</summary>
        public event Action<ShotEvent> LocalShotPredicted;
        /// <summary>Non-owner clients: a remote player fired (origin, end point, slot, suppressed, hitPlayer).</summary>
        public event Action<Vector3, Vector3, byte, bool, bool> RemoteShot;
        /// <summary>Owner: the server teleported us (spawn, round start).</summary>
        public event Action Teleported;

        float TickInterval => NetworkManager != null ? 1f / NetworkManager.NetworkConfig.TickRate : 1f / 60f;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _avatar = GetComponent<PlayerAvatar>();
        }

        public override void OnNetworkSpawn()
        {
            _rig = GetComponent<PlayerRig>();
            _cc.minMoveDistance = 0f;
            _cc.skinWidth = 0.03f;
            _cc.stepOffset = 0.35f;
            _cc.slopeLimit = 46f;
            PlayerMotor.ApplyShape(_cc, Movement, 0f);

            _motor.position = transform.position;
            _renderFrom = _renderTo = transform.position;
            ResetWeapons();

            NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;

            if (IsOwner)
            {
                LaunchConfig config = LaunchConfig.Current;
                _input = config.bot
                    ? new TestBotInputSource(this, _avatar)
                    : new KeyboardMouseInputSource(Movement.minPitch, Movement.maxPitch);
                _input.SetLook(transform.eulerAngles.y, 0f);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
                NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
            _input?.Dispose();
            _input = null;
        }

        void ResetWeapons()
        {
            GameContent content = GameContent.Instance;
            _weapons = content != null ? WeaponSimulation.Fresh(_avatar.WeaponInSlot(0), _avatar.WeaponInSlot(1)) : default;
            _simTime = 0f;
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                _input?.UpdateFrame(Time.deltaTime);
                // Visual correction smoothing: reconciliation snaps the simulation, the camera blends the error away.
                _correctionOffset = Vector3.Lerp(_correctionOffset, Vector3.zero, 1f - Mathf.Exp(-CorrectionSmoothing * Time.deltaTime));
            }
            else if (!IsServer)
                InterpolateRemote();

            if (IsServer && !IsOwner)
                transform.rotation = Quaternion.Euler(0f, _lastCommand.yaw, 0f);

            _rig?.SetHeightScale(PlayerMotor.Height(Movement, IsOwner || IsServer ? _motor.crouch : RemoteCrouch) / PlayerRig.StandingHeight);
        }

        // ───────────── Tick ─────────────

        void OnNetworkTick()
        {
            float dt = TickInterval;
            if (IsServer)
            {
                if (IsOwner)
                {
                    // Host's own player: no network hop, simulate the local command directly.
                    PlayerInputCommand cmd = BuildLocalCommand();
                    ServerSimulate(cmd, dt);
                    PushRender();
                }
                else
                {
                    ServerProcessQueue(dt);
                }
                ServerBroadcast();
            }
            else if (IsOwner)
            {
                OwnerPredictTick(dt);
            }
        }

        PlayerInputCommand BuildLocalCommand()
        {
            var cmd = new PlayerInputCommand { sequence = _nextSequence++ };
            _input?.Fill(ref cmd);
            // What this client is looking at: remote players are rendered InterpolationDelay in the past (host: present).
            cmd.viewTime = IsServer ? NetworkManager.ServerTime.Time : RemoteRenderTime(NetworkManager);
            cmd.Sanitize(Movement.minPitch, Movement.maxPitch);
            return cmd;
        }

        void PushRender()
        {
            _renderFrom = _renderTo;
            _renderTo = _motor.position;
            _renderTickTime = Time.time;
        }

        /// <summary>One step of movement + weapons from a command. Used by server authority and client prediction/replay.</summary>
        /// <summary>Crawl speed of a downed player relative to the agent's movement.</summary>
        public const float DownedSpeedMultiplier = 0.35f;
        const InputButtons DownedBlocked = InputButtons.Jump | InputButtons.Sprint | InputButtons.Fire | InputButtons.Aim
                                           | InputButtons.Reload | InputButtons.Interact | InputButtons.Ability1 | InputButtons.Ability2
                                           | InputButtons.Signature | InputButtons.ToggleAttachment;

        void Simulate(in PlayerInputCommand input, float dt, bool authoritative, bool firstPrediction)
        {
            bool alive = _avatar.IsAlive;
            // Downed players crawl (forced crouch, slow) and cannot fight, interact or use abilities.
            bool downed = alive && _avatar.Health.IsDowned;
            PlayerInputCommand cmd = input;
            if (downed) cmd.buttons = (cmd.buttons & ~DownedBlocked) | InputButtons.Crouch;
            MatchManager match = MatchManager.Instance;
            bool canMove = alive && (match == null || match.CanMove);
            bool canFight = alive && !downed && (match == null || match.CanFight);

            if (alive && _cc.enabled)
            {
                WeaponStats active = _avatar.StatsForSlot(_weapons.activeSlot);
                float aimSpeed = cmd.Has(InputButtons.Aim) && active.Definition != null ? active.AdsMoveSpeedMultiplier : 1f;
                if (downed) aimSpeed = DownedSpeedMultiplier;
                var motorCtx = new MotorContext { canMove = canMove, speedMultiplier = aimSpeed };
                PlayerMotor.Step(ref _motor, cmd, dt, Movement, _cc, motorCtx);
            }

            _simTime += dt;

            float horizontalSpeed = new Vector2(_motor.velocity.x, _motor.velocity.z).magnitude;
            var weaponCtx = new WeaponContext
            {
                canFire = canFight,
                sprinting = PlayerMotor.IsSprinting(cmd, _motor),
                grounded = _motor.grounded,
                crouched = _motor.crouch > 0.5f,
                moveFraction = horizontalSpeed / Mathf.Max(0.01f, Movement.runSpeed),
            };
            WeaponStats primary = _avatar.PrimaryStats;
            WeaponStats secondary = _avatar.SecondaryStats;
            WeaponSimulation.Step(ref _weapons, cmd, _simTime, dt, primary, secondary, weaponCtx, out ShotEvent shot);

            if (shot.fired)
            {
                if (authoritative)
                    CombatSystem.ServerResolveShot(_avatar, EyePosition, shot, _currentViewTime, NetworkManager.ServerTime.Time);
                if (firstPrediction && IsOwner)
                    LocalShotPredicted?.Invoke(shot);
            }

            if (authoritative)
                ServerHandleActions(cmd, alive);

            _lastCommand = cmd;
        }

        // ───────────── Server ─────────────

        void ServerSimulate(PlayerInputCommand cmd, float dt)
        {
            cmd.Sanitize(Movement.minPitch, Movement.maxPitch);
            double now = NetworkManager.ServerTime.Time;
            LaunchConfig config = LaunchConfig.Current;
            _currentViewTime = config.lagCompensation
                ? LagCompensation.ClampViewTime(cmd.viewTime, now, config.maxRewindMs / 1000.0, _lastViewTime)
                : now;
            _lastViewTime = _currentViewTime;
            Simulate(cmd, dt, authoritative: true, firstPrediction: true);
            _lastProcessedSequence = cmd.sequence;
        }

        void ServerProcessQueue(float dt)
        {
            double now = NetworkManager.ServerTime.Time;
            if (_lastInputTime == 0) _lastInputTime = now;
            if (now - _lastInputTime > InputSilenceTimeout)
            {
                BHLog.Warn("Server", $"No input from client {OwnerClientId} for {InputSilenceTimeout}s; disconnecting");
                _lastInputTime = now;
                NetworkManager.DisconnectClient(OwnerClientId, "Connection timed out (no input received).");
                return;
            }

            _budget = Mathf.Min(_budget + 1, MaxCommandBudget);
            int processed = 0;
            while (_queue.Count > 0 && _budget > 0 && processed < MaxCommandsPerTick)
            {
                ServerSimulate(_queue.Dequeue(), dt);
                _budget--;
                processed++;
            }

            if (processed > 0)
            {
                _starvedTicks = 0;
            }
            else if (++_starvedTicks > StarvationTicks && _avatar.IsAlive && _cc.enabled && !_motor.grounded)
            {
                // Input starvation: keep gravity working so a lagging player cannot hang in the air.
                // Does not consume a sequence number; the owner is corrected when its input resumes.
                PlayerInputCommand idle = _lastCommand;
                idle.buttons = InputButtons.None;
                idle.moveX = idle.moveY = 0;
                PlayerMotor.Step(ref _motor, idle, dt, Movement, _cc, new MotorContext { canMove = false });
            }
        }

        bool ServerAcceptInputRpc()
        {
            double now = NetworkManager.ServerTime.Time;
            if (now - _rpcWindowStart >= 1.0)
            {
                _rpcWindowStart = now;
                _rpcWindowCount = 0;
            }
            if (++_rpcWindowCount <= MaxInputRpcsPerSecond) return true;
            if (now - _lastAnomalyLog > 10.0)
            {
                _lastAnomalyLog = now;
                BHLog.Warn("Security", $"Client {OwnerClientId} exceeded {MaxInputRpcsPerSecond} input messages/s; dropping excess");
            }
            return false;
        }

        /// <summary>Server: move this character's hitboxes to its recorded pose at <paramref name="time"/> (lag compensation).</summary>
        public void ServerApplyHistoricalHitboxes(double time)
        {
            if (_rig == null || _rig.HitboxRoot == null || !_poses.TrySample(time, out Vector3 position, out float crouch)) return;
            _rig.HitboxRoot.position = position;
            _rig.HitboxRoot.localScale = new Vector3(1f, PlayerMotor.Height(Movement, crouch) / PlayerRig.StandingHeight, 1f);
        }

        public void ServerRestoreHitboxes()
        {
            if (_rig == null || _rig.HitboxRoot == null) return;
            _rig.HitboxRoot.localPosition = Vector3.zero;
            _rig.HitboxRoot.localScale = new Vector3(1f, PlayerMotor.Height(Movement, _motor.crouch) / PlayerRig.StandingHeight, 1f);
        }

        void ServerHandleActions(in PlayerInputCommand cmd, bool alive)
        {
            InputButtons pressed = cmd.buttons & ~_previousButtons;
            _previousButtons = cmd.buttons;
            bool standing = alive && !_avatar.Health.IsDowned;
            InteractHeld = standing && cmd.Has(InputButtons.Interact);

            if (!standing) return;

            Vector3 eye = EyePosition;
            Vector3 aim = WeaponSimulation.ViewRotation(cmd.yaw, cmd.pitch) * Vector3.forward;
            if ((pressed & InputButtons.Ability1) != 0) _avatar.Abilities.ServerTryActivate(AbilitySlot.Utility1, eye, aim);
            if ((pressed & InputButtons.Ability2) != 0) _avatar.Abilities.ServerTryActivate(AbilitySlot.Utility2, eye, aim);
            if ((pressed & InputButtons.Signature) != 0) _avatar.Abilities.ServerTryActivate(AbilitySlot.Signature, eye, aim);

            if ((pressed & InputButtons.ToggleAttachment) != 0)
            {
                MatchManager match = MatchManager.Instance;
                if (match == null || match.LoadoutChangesAllowed)
                {
                    if (_avatar.ServerToggleSuppressor(_weapons.activeSlot))
                        BHLog.Info("Loadout", $"{_avatar.PlayerName} suppressor on slot {_weapons.activeSlot}: {_avatar.HasSuppressor(_weapons.activeSlot)}");
                }
            }
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void SubmitInputRpc(InputBundle bundle, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;
            if (!ServerAcceptInputRpc())
                return;
            MessagesReceived++;
            _lastInputTime = NetworkManager.ServerTime.Time;

            int count = Mathf.Min(bundle.count, InputBundle.Capacity);
            for (int i = 0; i < count; i++)
            {
                PlayerInputCommand cmd = bundle[i];
                if (cmd.sequence <= _lastReceivedSequence)
                    continue;
                if (_queue.Count >= MaxQueuedCommands)
                    break;
                _queue.Enqueue(cmd);
                _lastReceivedSequence = cmd.sequence;
            }
        }

        void ServerBroadcast()
        {
            _poses.Record(NetworkManager.ServerTime.Time, _motor.position, _motor.crouch);

            if (!IsOwner)
                OwnerStateRpc(MakeOwnerState());

            if (++_ticksSinceSnapshot >= RemoteSnapshotInterval)
            {
                _ticksSinceSnapshot = 0;
                RemoteStateRpc(new RemoteSnapshot
                {
                    time = NetworkManager.ServerTime.Time,
                    teleportId = _teleportId,
                    position = _motor.position,
                    yaw = _lastCommand.yaw,
                    pitch = _lastCommand.pitch,
                    crouch = _motor.crouch,
                    activeSlot = _weapons.activeSlot,
                    grounded = _motor.grounded,
                    reloading = _weapons.IsReloading,
                });
            }
        }

        OwnerState MakeOwnerState() => new()
        {
            lastSequence = _lastProcessedSequence,
            teleportId = _teleportId,
            simTime = _simTime,
            yaw = _lastCommand.yaw,
            motor = _motor,
            weapons = _weapons,
        };

        /// <summary>Server: authoritative relocation (spawn / round start). Clears queued input.</summary>
        public void ServerTeleport(Vector3 position, float yaw, bool resetWeapons)
        {
            _motor = new MotorState { position = position, grounded = true };
            PlayerMotor.ApplyShape(_cc, Movement, 0f);
            PlayerMotor.Teleport(_cc, position);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (resetWeapons) ResetWeapons();
            _teleportId++;
            _queue.Clear();
            _lastCommand.yaw = yaw;
            _lastCommand.pitch = 0f;
            _renderFrom = _renderTo = position;
            _snapshotCount = 0;
            _poses.Clear();

            if (IsOwner)
            {
                _input?.SetLook(yaw, 0f);
                Teleported?.Invoke();
            }
            else
            {
                TeleportRpc(MakeOwnerState(), keepLook: false);
            }
        }

        /// <summary>Server: the loadout changed mid-life (Weapon Game level-up). Resets weapons, keeps position and view.</summary>
        public void ServerResetWeapons()
        {
            ResetWeapons();
            _teleportId++;
            _queue.Clear();
            if (!IsOwner) TeleportRpc(MakeOwnerState(), keepLook: true);
        }

        // ───────────── Owner prediction ─────────────

        void OwnerPredictTick(float dt)
        {
            Physics.SyncTransforms();
            PlayerInputCommand cmd = BuildLocalCommand();
            Simulate(cmd, dt, authoritative: false, firstPrediction: true);
            Record(cmd);
            PushRender();

            // Shift the redundancy window and send.
            for (int i = 0; i < _recentCommands.Length - 1; i++)
                _recentCommands[i] = _recentCommands[i + 1];
            _recentCommands[^1] = cmd;

            var bundle = new InputBundle();
            int count = 0;
            for (int i = 0; i < _recentCommands.Length; i++)
            {
                if (_recentCommands[i].sequence == 0 || _recentCommands[i].sequence <= _lastAckSequence) continue;
                bundle[count++] = _recentCommands[i];
            }
            bundle.count = (byte)count;
            SubmitInputRpc(bundle);
        }

        void Record(in PlayerInputCommand cmd)
        {
            _history[cmd.sequence % HistorySize] = new HistoryEntry
            {
                command = cmd,
                motor = _motor,
                weapons = _weapons,
                simTime = _simTime,
            };
        }

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        void OwnerStateRpc(OwnerState state)
        {
            if (IsServer) return;
            MessagesReceived++;
            if (state.teleportId != _teleportId) return; // Stale state from before a teleport.
            if (state.lastSequence <= _lastAckSequence) return;
            _lastAckSequence = state.lastSequence;

            if (state.lastSequence == 0)
            {
                // Server has not processed any of our input yet; nothing to reconcile.
                return;
            }

            ref HistoryEntry entry = ref _history[state.lastSequence % HistorySize];
            bool havePrediction = entry.command.sequence == state.lastSequence;
            if (havePrediction && Matches(entry, state))
                return;

            // Misprediction: rewind to the authoritative state and replay newer commands.
            CorrectionCount++;
            Vector3 predictedBefore = _motor.position;
            ApplyAuthoritative(state);
            float dt = TickInterval;
            for (uint seq = state.lastSequence + 1; seq < _nextSequence; seq++)
            {
                ref HistoryEntry e = ref _history[seq % HistorySize];
                if (e.command.sequence != seq) break;
                Simulate(e.command, dt, authoritative: false, firstPrediction: false);
                e.motor = _motor;
                e.weapons = _weapons;
                e.simTime = _simTime;
            }

            // Blend small errors visually instead of snapping the camera; large ones (e.g. blocked by a wall) snap.
            Vector3 error = predictedBefore - _motor.position;
            if (error.sqrMagnitude < 4f)
            {
                _correctionOffset += error;
                _renderFrom -= error;
            }
            else
            {
                _correctionOffset = Vector3.zero;
                _renderFrom = _motor.position;
            }
            _renderTo = _motor.position;
        }

        static bool Matches(in HistoryEntry predicted, in OwnerState server)
        {
            if ((predicted.motor.position - server.motor.position).sqrMagnitude > 0.0004f) return false;
            if (Mathf.Abs(predicted.motor.velocity.y - server.motor.velocity.y) > 0.25f) return false;
            WeaponSimState a = predicted.weapons, b = server.weapons;
            return a.activeSlot == b.activeSlot
                   && a.magazine0 == b.magazine0 && a.magazine1 == b.magazine1
                   && a.reserve0 == b.reserve0 && a.reserve1 == b.reserve1
                   && a.IsReloading == b.IsReloading;
        }

        void ApplyAuthoritative(in OwnerState state)
        {
            _motor = state.motor;
            _weapons = state.weapons;
            _simTime = state.simTime;
            PlayerMotor.ApplyShape(_cc, Movement, _motor.crouch);
            PlayerMotor.Teleport(_cc, _motor.position);
        }

        [Rpc(SendTo.Owner)]
        void TeleportRpc(OwnerState state, bool keepLook)
        {
            if (IsServer) return;
            _teleportId = state.teleportId;
            _lastAckSequence = state.lastSequence;
            ApplyAuthoritative(state);
            // Any history entries are from before the teleport; mark them unusable.
            for (int i = 0; i < _history.Length; i++) _history[i].command.sequence = 0;
            _renderFrom = _renderTo = _motor.position;
            _correctionOffset = Vector3.zero;
            if (keepLook) return;
            _input?.SetLook(state.yaw, 0f);
            transform.rotation = Quaternion.Euler(0f, state.yaw, 0f);
            Teleported?.Invoke();
        }

        // ───────────── Remote interpolation ─────────────

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void RemoteStateRpc(RemoteSnapshot snapshot)
        {
            if (IsOwner) return;
            SnapshotClock.Observe(snapshot.time);

            if (_snapshotCount > 0)
            {
                RemoteSnapshot newest = _snapshots[_snapshotCount - 1];
                if (snapshot.time <= newest.time) return; // Out of order.
                if (snapshot.teleportId != newest.teleportId) _snapshotCount = 0; // Teleported: snap.
            }

            if (_snapshotCount == SnapshotBufferSize)
            {
                Array.Copy(_snapshots, 1, _snapshots, 0, SnapshotBufferSize - 1);
                _snapshotCount--;
            }
            _snapshots[_snapshotCount++] = snapshot;
        }

        float RemoteCrouch { get; set; }

        /// <summary>Server time at which remote players are rendered on this client (also reported for lag compensation).</summary>
        static double RemoteRenderTime(NetworkManager network) =>
            (SnapshotClock.IsValid ? SnapshotClock.ServerTimeEstimate : network.ServerTime.Time) - InterpolationDelay;

        void InterpolateRemote()
        {
            if (_snapshotCount == 0) return;

            double renderTime = RemoteRenderTime(NetworkManager);
            RemoteSnapshot from = _snapshots[0], to = _snapshots[0];
            for (int i = 0; i < _snapshotCount; i++)
            {
                if (_snapshots[i].time <= renderTime) from = _snapshots[i];
                to = _snapshots[i];
                if (_snapshots[i].time > renderTime) break;
            }

            float t = to.time > from.time ? Mathf.Clamp01((float)((renderTime - from.time) / (to.time - from.time))) : 1f;
            Vector3 pos = Vector3.Lerp(from.position, to.position, t);

            // Snapshots ran out (loss/jitter): extrapolate briefly along the last known velocity.
            RemoteSnapshot newest = _snapshots[_snapshotCount - 1];
            if (renderTime > newest.time && _snapshotCount >= 2)
            {
                RemoteSnapshot previous = _snapshots[_snapshotCount - 2];
                double span = newest.time - previous.time;
                if (span > 1e-4)
                {
                    Vector3 velocity = (newest.position - previous.position) / (float)span;
                    float ahead = Mathf.Min((float)(renderTime - newest.time), ExtrapolationLimit);
                    pos = newest.position + velocity * ahead;
                }
            }
            _remoteYaw = Mathf.LerpAngle(from.yaw, to.yaw, t);
            _remotePitch = Mathf.Lerp(from.pitch, to.pitch, t);
            RemoteCrouch = Mathf.Lerp(from.crouch, to.crouch, t);
            _remoteSlot = to.activeSlot;
            _remoteReloading = to.reloading;
            _motor.position = pos;
            _motor.grounded = to.grounded;

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, _remoteYaw, 0f));
            PlayerMotor.ApplyShape(_cc, Movement, RemoteCrouch);

            // Drop snapshots that are no longer needed.
            int firstNeeded = 0;
            for (int i = 0; i < _snapshotCount - 1; i++)
            {
                if (_snapshots[i + 1].time <= renderTime) firstNeeded = i + 1;
            }
            if (firstNeeded > 0)
            {
                Array.Copy(_snapshots, firstNeeded, _snapshots, 0, _snapshotCount - firstNeeded);
                _snapshotCount -= firstNeeded;
            }
        }

        // ───────────── Shot effects ─────────────

        /// <summary>Server: tell other clients a shot happened so they can render it.</summary>
        public void ServerBroadcastShot(Vector3 origin, Vector3 end, byte slot, bool suppressed, bool hitPlayer)
        {
            ShotFxRpc(origin, end, slot, suppressed, hitPlayer);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void ShotFxRpc(Vector3 origin, Vector3 end, byte slot, bool suppressed, bool hitPlayer)
        {
            if (IsServer && !IsClient) return;
            RemoteShot?.Invoke(origin, end, slot, suppressed, hitPlayer);
        }
    }
}
