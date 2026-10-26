using System;
using System.Collections.Generic;
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
    /// <summary>
    /// UDP network interface for Windows servers that ignores ICMP "port unreachable" errors.
    ///
    /// Why: on Windows, when a client process dies its port closes and every packet the server still sends there
    /// triggers an ICMP reply. The default Unity Transport socket then stopped delivering packets from *all*
    /// clients within seconds (reproduced and documented in RISKS.md, R-07). This interface uses a managed socket
    /// with SIO_UDP_CONNRESET disabled, which is the standard fix for UDP game servers on Windows.
    ///
    /// The work runs synchronously on the main thread inside the Schedule* calls (allowed by Unity Transport for
    /// managed interfaces). Throughput is ample for a 10-player match; revisit for larger servers.
    /// </summary>
    public struct IcmpSafeUdpInterface : INetworkInterface
    {
        const int SioUdpConnReset = -1744830452; // _WSAIOW(IOC_VENDOR, 12)
        const int BufferSize = 2048;

        sealed class State
        {
            public Socket socket;
            public NetworkEndpoint local;
            public readonly byte[] buffer = new byte[BufferSize];
            public readonly Dictionary<IPEndPoint, NetworkEndpoint> toNetwork = new();
            public readonly Dictionary<NetworkEndpoint, IPEndPoint> toIp = new();
            public EndPoint anyV4 = new IPEndPoint(IPAddress.Any, 0);
            public EndPoint anyV6 = new IPEndPoint(IPAddress.IPv6Any, 0);
        }

        readonly State _state;

        IcmpSafeUdpInterface(State state) => _state = state;

        public static IcmpSafeUdpInterface Create() => new(new State());

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
                if (!arguments.ReceiveQueue.EnqueuePacket(out PacketProcessor packet))
                {
                    arguments.ReceiveResult.ErrorCode = (int)Unity.Networking.Transport.Error.StatusCode.NetworkReceiveQueueFull;
                    break;
                }
                if (received > packet.BytesAvailableAtEnd)
                {
                    packet.Drop();
                    continue;
                }
                fixed (byte* src = buffer) packet.AppendToPayload(src, received);
                packet.EndpointRef = ToNetwork((IPEndPoint)remote);
            }
            return default;
        }

        public unsafe JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            dep.Complete();
            Socket socket = _state.socket;
            if (socket == null) return default;

            byte[] buffer = _state.buffer;
            int count = arguments.SendQueue.Count;
            for (int i = 0; i < count; i++)
            {
                PacketProcessor packet = arguments.SendQueue[i];
                if (packet.Length == 0) continue;
                int length;
                fixed (byte* dst = buffer) length = packet.CopyPayload(dst, buffer.Length);
                try
                {
                    socket.SendTo(buffer, 0, length, SocketFlags.None, ToIp(packet.EndpointRef));
                }
                catch (SocketException)
                {
                    // Unreachable peers and transient send failures are handled by the transport's timeouts.
                }
            }
            return default;
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
        public void CreateDriver(UnityTransport transport, out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            driver = NetworkDriver.Create(IcmpSafeUdpInterface.Create().WrapToUnmanaged(), transport.GetDefaultNetworkSettings());
            transport.GetDefaultPipelineConfigurations(out var unreliable, out var unreliableSequenced, out var reliable);
            unreliableFragmentedPipeline = driver.CreatePipeline(unreliable);
            unreliableSequencedFragmentedPipeline = driver.CreatePipeline(unreliableSequenced);
            reliableSequencedPipeline = driver.CreatePipeline(reliable);
        }
    }
}
