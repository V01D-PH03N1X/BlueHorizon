using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BlueHorizon.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace BlueHorizon.Networking
{
    /// <summary>Artificial network conditions for testing (-simLatency/-simJitter/-simLoss). Off by default.</summary>
    [Serializable]
    public struct SimulatedNetworkConditions
    {
        /// <summary>Added round-trip time in milliseconds (half on send, half on receive).</summary>
        public int latencyMs;
        /// <summary>Random ± variation per packet and direction, milliseconds.</summary>
        public int jitterMs;
        /// <summary>Packet loss per direction, percent.</summary>
        public float lossPercent;

        public bool Enabled => latencyMs > 0 || jitterMs > 0 || lossPercent > 0f;

        public override string ToString() => $"latency {latencyMs}ms, jitter ±{jitterMs}ms, loss {lossPercent:0.#}%";
    }

    /// <summary>
    /// Managed UDP network interface used on Windows servers (and by any process with simulated network conditions).
    ///
    /// 1. ICMP safety: on Windows, when a client process dies its port closes and every packet the server still sends
    ///    there triggers an ICMP reply. The stock Unity Transport socket then stopped delivering packets from *all*
    ///    clients within seconds (RISKS.md, R-07). This socket disables SIO_UDP_CONNRESET.
    /// 2. Network simulation: optional latency, jitter and loss on both directions, so latency behaviour and lag
    ///    compensation can be measured on one machine.
    ///
    /// Work runs synchronously on the main thread inside the Schedule* calls (allowed for managed interfaces).
    /// </summary>
    public struct IcmpSafeUdpInterface : INetworkInterface
    {
        const int SioUdpConnReset = -1744830452; // _WSAIOW(IOC_VENDOR, 12)
        const int BufferSize = 2048;

        struct Delayed
        {
            public byte[] data;
            public int length;
            public IPEndPoint endpoint;
            public long dueMs;
        }

        sealed class State
        {
            public Socket socket;
            public NetworkEndpoint local;
            public readonly byte[] buffer = new byte[BufferSize];
            public readonly Dictionary<IPEndPoint, NetworkEndpoint> toNetwork = new();
            public readonly Dictionary<NetworkEndpoint, IPEndPoint> toIp = new();
            public EndPoint anyV4 = new IPEndPoint(IPAddress.Any, 0);
            public EndPoint anyV6 = new IPEndPoint(IPAddress.IPv6Any, 0);

            public SimulatedNetworkConditions conditions;
            public readonly List<Delayed> outgoing = new();
            public readonly List<Delayed> incoming = new();
            public readonly Stack<byte[]> pool = new();
            public readonly System.Random random = new(12345);
            public readonly Stopwatch clock = Stopwatch.StartNew();

            public byte[] Rent() => pool.Count > 0 ? pool.Pop() : new byte[BufferSize];
            public void Return(byte[] b) => pool.Push(b);

            /// <summary>Returns false if the packet is lost; otherwise its release time.</summary>
            public bool Schedule(out long dueMs)
            {
                dueMs = clock.ElapsedMilliseconds;
                if (conditions.lossPercent > 0f && random.NextDouble() * 100.0 < conditions.lossPercent) return false;
                int jitter = conditions.jitterMs > 0 ? random.Next(-conditions.jitterMs, conditions.jitterMs + 1) : 0;
                dueMs += Math.Max(0, conditions.latencyMs / 2 + jitter);
                return true;
            }
        }

        readonly State _state;

        IcmpSafeUdpInterface(State state) => _state = state;

        public static IcmpSafeUdpInterface Create(SimulatedNetworkConditions conditions = default)
        {
            var state = new State { conditions = conditions };
            return new IcmpSafeUdpInterface(state);
        }

        public NetworkEndpoint LocalEndpoint => _state.local;

        public int Initialize(ref NetworkSettings settings, ref int packetPadding) => 0;

        public int Bind(NetworkEndpoint endpoint)
        {
            try
            {
                _state.socket?.Close();
                AddressFamily family = endpoint.Family == NetworkFamily.Ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
                var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp)
                {
                    Blocking = false,
                    ReceiveBufferSize = 1 << 20,
                    SendBufferSize = 1 << 20,
                };
                if (Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsServer or RuntimePlatform.WindowsEditor)
                    socket.IOControl(SioUdpConnReset, new byte[4], null);
                socket.Bind(ToIp(endpoint));
                _state.socket = socket;
                _state.local = ToNetwork((IPEndPoint)socket.LocalEndPoint);
                return 0;
            }
            catch (Exception e)
            {
                BHLog.Error("Network", $"IcmpSafeUdpInterface bind to {endpoint} failed: {e.Message}");
                return (int)Unity.Networking.Transport.Error.StatusCode.NetworkSocketError;
            }
        }

        public int Listen() => 0;

        public unsafe JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            dep.Complete();
            Socket socket = _state.socket;
            if (socket == null) return default;
            bool simulate = _state.conditions.Enabled;

            byte[] buffer = _state.buffer;
            while (socket.Available > 0)
            {
                EndPoint remote = socket.AddressFamily == AddressFamily.InterNetworkV6 ? _state.anyV6 : _state.anyV4;
                int received;
                try
                {
                    received = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remote);
                }
                catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
                {
                    continue;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
                {
                    break;
                }
                catch (SocketException e)
                {
                    BHLog.Warn("Network", $"UDP receive error: {e.SocketErrorCode}");
                    arguments.ReceiveResult.ErrorCode = (int)Unity.Networking.Transport.Error.StatusCode.NetworkSocketError;
                    break;
                }

                if (received <= 0) continue;
                if (simulate)
                {
                    if (!_state.Schedule(out long due)) continue;
                    byte[] copy = _state.Rent();
                    Buffer.BlockCopy(buffer, 0, copy, 0, received);
                    _state.incoming.Add(new Delayed { data = copy, length = received, endpoint = (IPEndPoint)remote, dueMs = due });
                    continue;
                }
                if (!Deliver(ref arguments, buffer, received, (IPEndPoint)remote)) break;
            }

            if (simulate) DeliverDue(ref arguments);
            return default;
        }

        unsafe bool Deliver(ref ReceiveJobArguments arguments, byte[] data, int length, IPEndPoint remote)
        {
            if (!arguments.ReceiveQueue.EnqueuePacket(out PacketProcessor packet))
            {
                arguments.ReceiveResult.ErrorCode = (int)Unity.Networking.Transport.Error.StatusCode.NetworkReceiveQueueFull;
                return false;
            }
            if (length > packet.BytesAvailableAtEnd)
            {
                packet.Drop();
                return true;
            }
            fixed (byte* src = data) packet.AppendToPayload(src, length);
            packet.EndpointRef = ToNetwork(remote);
            return true;
        }

        void DeliverDue(ref ReceiveJobArguments arguments)
        {
            long now = _state.clock.ElapsedMilliseconds;
            List<Delayed> list = _state.incoming;
            for (int i = 0; i < list.Count; i++)
            {
                Delayed d = list[i];
                if (d.dueMs > now) continue;
                bool ok = Deliver(ref arguments, d.data, d.length, d.endpoint);
                _state.Return(d.data);
                list.RemoveAt(i--);
                if (!ok) break;
            }
        }

        public unsafe JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            dep.Complete();
            Socket socket = _state.socket;
            if (socket == null) return default;
            bool simulate = _state.conditions.Enabled;

            byte[] buffer = _state.buffer;
            int count = arguments.SendQueue.Count;
            for (int i = 0; i < count; i++)
            {
                PacketProcessor packet = arguments.SendQueue[i];
                if (packet.Length == 0) continue;
                IPEndPoint target = ToIp(packet.EndpointRef);
                if (simulate)
                {
                    if (!_state.Schedule(out long due)) continue;
                    byte[] copy = _state.Rent();
                    int copied;
                    fixed (byte* dst = copy) copied = packet.CopyPayload(dst, copy.Length);
                    _state.outgoing.Add(new Delayed { data = copy, length = copied, endpoint = target, dueMs = due });
                    continue;
                }
                int length;
                fixed (byte* dst = buffer) length = packet.CopyPayload(dst, buffer.Length);
                SendRaw(socket, buffer, length, target);
            }

            if (simulate)
            {
                long now = _state.clock.ElapsedMilliseconds;
                List<Delayed> list = _state.outgoing;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].dueMs > now) continue;
                    SendRaw(socket, list[i].data, list[i].length, list[i].endpoint);
                    _state.Return(list[i].data);
                    list.RemoveAt(i--);
                }
            }
            return default;
        }

        static void SendRaw(Socket socket, byte[] data, int length, IPEndPoint target)
        {
            try
            {
                socket.SendTo(data, 0, length, SocketFlags.None, target);
            }
            catch (SocketException)
            {
                // Unreachable peers and transient send failures are handled by the transport's timeouts.
            }
        }

        public void Dispose()
        {
            _state.socket?.Close();
            _state.socket = null;
        }

        NetworkEndpoint ToNetwork(IPEndPoint ip)
        {
            if (_state.toNetwork.TryGetValue(ip, out NetworkEndpoint ep)) return ep;
            ep = NetworkEndpoint.Parse(ip.Address.ToString(), (ushort)ip.Port,
                ip.AddressFamily == AddressFamily.InterNetworkV6 ? NetworkFamily.Ipv6 : NetworkFamily.Ipv4);
            _state.toNetwork[new IPEndPoint(ip.Address, ip.Port)] = ep;
            _state.toIp[ep] = new IPEndPoint(ip.Address, ip.Port);
            return ep;
        }

        IPEndPoint ToIp(NetworkEndpoint ep)
        {
            if (_state.toIp.TryGetValue(ep, out IPEndPoint ip)) return ip;
            NativeArray<byte> raw = ep.GetRawAddressBytes();
            ip = new IPEndPoint(new IPAddress(raw.ToArray()), ep.Port);
            _state.toIp[ep] = ip;
            return ip;
        }
    }

    /// <summary>Builds the Unity Transport driver on <see cref="IcmpSafeUdpInterface"/> with Netcode's default pipelines.</summary>
    public sealed class IcmpSafeDriverConstructor : INetworkStreamDriverConstructor
    {
        readonly SimulatedNetworkConditions _conditions;

        public IcmpSafeDriverConstructor(SimulatedNetworkConditions conditions = default) => _conditions = conditions;

        public void CreateDriver(UnityTransport transport, out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            driver = NetworkDriver.Create(IcmpSafeUdpInterface.Create(_conditions).WrapToUnmanaged(), transport.GetDefaultNetworkSettings());
            transport.GetDefaultPipelineConfigurations(out var unreliable, out var unreliableSequenced, out var reliable);
            unreliableFragmentedPipeline = driver.CreatePipeline(unreliable);
            unreliableSequencedFragmentedPipeline = driver.CreatePipeline(unreliableSequenced);
            reliableSequencedPipeline = driver.CreatePipeline(reliable);
        }
    }
}
