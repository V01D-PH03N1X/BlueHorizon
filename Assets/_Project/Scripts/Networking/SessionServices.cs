using System;
using System.Threading.Tasks;
using BlueHorizon.Core;

namespace BlueHorizon.Networking
{
    /// <summary>Where a client should open its gameplay connection.</summary>
    public readonly struct ServerEndpoint
    {
        public readonly string Address;
        public readonly ushort Port;

        public ServerEndpoint(string address, ushort port)
        {
            Address = address;
            Port = port;
        }

        public override string ToString() => $"{Address}:{Port}";
    }

    /// <summary>
    /// Client side of matchmaking: turns "I want to play" into a <see cref="ServerEndpoint"/>.
    /// The vertical slice uses <see cref="DirectSessionConnector"/>. A production implementation would
    /// create/join a player session (lobby or matchmaker ticket), wait for the match assignment and the
    /// dedicated server allocation, then return that server's address — the gameplay connection itself
    /// (NetworkBootstrap.StartClient) does not change.
    /// </summary>
    public interface ISessionConnector
    {
        string Description { get; }
        Task<ServerEndpoint> FindMatchAsync(string playerName);
        /// <summary>Called when the gameplay connection ends so the session/ticket can be cleaned up.</summary>
        Task LeaveAsync();
    }

    /// <summary>Connects to an address typed by the user or passed on the command line.</summary>
    public sealed class DirectSessionConnector : ISessionConnector
    {
        readonly ServerEndpoint _endpoint;

        public DirectSessionConnector(string address, ushort port) => _endpoint = new ServerEndpoint(address, port);

        public string Description => $"Direct connection to {_endpoint}";

        public Task<ServerEndpoint> FindMatchAsync(string playerName) => Task.FromResult(_endpoint);

        public Task LeaveAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Server side of hosting: lifecycle signals a hosting provider expects (server ready, players changed,
    /// match finished). The local implementation only logs. A provider adapter (e.g. Unity Multiplayer
    /// Services / a third-party fleet manager) implements this without touching match logic.
    /// </summary>
    public interface IServerHostingAdapter
    {
        void OnServerStarted(ushort port, int maxPlayers);
        void OnReadyForPlayers();
        void OnPlayerCountChanged(int players);
        void OnMatchCompleted();
        void OnShutdown();
    }

    public sealed class LocalServerHostingAdapter : IServerHostingAdapter
    {
        public void OnServerStarted(ushort port, int maxPlayers) =>
            BHLog.Info("Hosting", $"Local dedicated server listening on UDP {port}, max {maxPlayers} players");

        public void OnReadyForPlayers() => BHLog.Info("Hosting", "Ready for players");

        public void OnPlayerCountChanged(int players) => BHLog.Info("Hosting", $"Players connected: {players}");

        public void OnMatchCompleted() => BHLog.Info("Hosting", "Match completed");

        public void OnShutdown() => BHLog.Info("Hosting", "Server shutting down");
    }

    public static class HostingServices
    {
        static IServerHostingAdapter _adapter;

        public static IServerHostingAdapter Adapter
        {
            get => _adapter ??= new LocalServerHostingAdapter();
            set => _adapter = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
