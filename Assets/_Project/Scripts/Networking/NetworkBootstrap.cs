using System;
using BlueHorizon.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlueHorizon.Networking
{
    public enum ClientConnectionState
    {
        Offline,
        Connecting,
        Connected,
        Disconnected,
    }

    /// <summary>
    /// Process entry point on the NetworkManager object. Chooses the role from <see cref="LaunchConfig"/>
    /// (dedicated server builds always run as server), configures the transport, approves connections and
    /// loads the map scene on the server. No source changes are needed to switch roles.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";

        [SerializeField] GameContent content;

        public static NetworkBootstrap Instance { get; private set; }

        public ClientConnectionState ClientState { get; private set; } = ClientConnectionState.Offline;
        public string LastDisconnectReason { get; private set; } = "";
        public ServerEndpoint LastEndpoint { get; private set; }
        public bool IsDedicatedServer { get; private set; }
        public event Action StateChanged;

        NetworkManager _network;
        UnityTransport _transport;
        LaunchConfig _config;
        int _matchesCompleted;
        ISessionConnector _session;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Logs, config and UI numbers use one format regardless of the machine's locale.
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            content.MakeCurrent();
            GameLayers.ConfigureCollisionMatrix();
            _network = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();
            _transport.MaxConnectAttempts = 10;
            _transport.ConnectTimeoutMS = 1000;
            // Network prefabs are registered through the NetworkPrefabsList asset assigned by the project setup tool.

            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            _network.OnServerStarted += OnServerStarted;
            _network.OnTransportFailure += OnTransportFailure;
        }

        void Start()
        {
            _config = LaunchConfig.Current;
            BHLog.Info("Boot", $"Blue Horizon {Application.version} | Unity {Application.unityVersion} | role {_config.role} | batch {Application.isBatchMode}");

            if (_config.quitAfterSeconds > 0f)
                Invoke(nameof(QuitFromTimer), _config.quitAfterSeconds);

            Testing.PerformanceMonitor.Install(_config.perfLogInterval >= 0f ? _config.perfLogInterval : _config.role == LaunchRole.Server ? 30f : 0f);
            Testing.ScreenshotRecorder.Install(_config.screenshotInterval, _config.screenshotDir);

            switch (_config.role)
            {
                case LaunchRole.Server:
                    StartDedicatedServer();
                    break;
                case LaunchRole.Host:
                    StartHost(_config.playerName);
                    break;
                case LaunchRole.Client:
                    if (Application.isBatchMode)
                    {
                        // Headless test clients would otherwise run uncapped and burn a full core each.
                        Application.targetFrameRate = 120;
                    }
                    StartClient(new ServerEndpoint(_config.address, _config.port), _config.playerName);
                    break;
                default:
                    if (SceneManager.GetActiveScene().name != MenuScene)
                        SceneManager.LoadScene(MenuScene);
                    break;
            }
        }

        // ───────────── Server ─────────────

        void StartDedicatedServer()
        {
            IsDedicatedServer = true;
            Application.targetFrameRate = (int)_network.NetworkConfig.TickRate * 2;
            QualitySettings.vSyncCount = 0;

            _transport.SetConnectionData(true, "0.0.0.0", _config.port, _config.listenAddress);
            _network.NetworkConfig.ConnectionApproval = true;
            _network.ConnectionApprovalCallback = ApproveConnection;
            ConfigureSocket(server: true);

            if (!_network.StartServer())
            {
                BHLog.Error("Boot", $"Failed to start server on port {_config.port}. Is the port in use?");
                Quit(1);
                return;
            }
            HostingServices.Adapter.OnServerStarted(_config.port, _config.maxPlayers);
        }

        public bool StartHost(string playerName)
        {
            LaunchConfig.Current.role = LaunchRole.Host;
            _config = LaunchConfig.Current;
            _transport.SetConnectionData(true, "127.0.0.1", _config.port, "0.0.0.0");
            _network.NetworkConfig.ConnectionApproval = true;
            _network.ConnectionApprovalCallback = ApproveConnection;
            _network.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                protocol = ConnectionPayload.CurrentProtocol,
                playerName = playerName,
            }.ToBytes();

            SetClientState(ClientConnectionState.Connecting, "");
            ConfigureSocket(server: true);
            if (!_network.StartHost())
            {
                SetClientState(ClientConnectionState.Disconnected, $"Could not host on port {_config.port}.");
                return false;
            }
            BHLog.Warn("Boot", "Running as listen-server HOST. Development convenience only — validate with the dedicated server build.");
            return true;
        }

        /// <summary>
        /// Windows servers use <see cref="IcmpSafeUdpInterface"/> so a crashed client cannot stall everyone's traffic
        /// (RISKS.md, R-07); any process with simulated network conditions uses it too. -defaultSocket forces the stock
        /// Unity Transport socket for comparison.
        /// </summary>
        void ConfigureSocket(bool server)
        {
            bool windows = Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsServer or RuntimePlatform.WindowsEditor;
            SimulatedNetworkConditions conditions = _config.NetworkConditions;
            bool forceDefault = CommandLineArgs.FromEnvironment().Has("defaultSocket");
            if (forceDefault || !(conditions.Enabled || (server && windows)))
            {
                UnityTransport.s_DriverConstructor = null;
                return;
            }
            UnityTransport.s_DriverConstructor = new IcmpSafeDriverConstructor(conditions);
            BHLog.Info("Network", conditions.Enabled
                ? $"Using managed UDP socket with SIMULATED network conditions: {conditions}"
                : "Using ICMP-safe UDP socket (Windows server)");
        }

        void OnServerStarted()
        {
            BHLog.Info("Server", $"Started. Loading map '{content.mapScene}'");
            _network.SceneManager.OnLoadEventCompleted += OnServerSceneLoaded;
            var status = _network.SceneManager.LoadScene(content.mapScene, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
                BHLog.Error("Server", $"Map load failed to start: {status}");
            HostingServices.Adapter.OnReadyForPlayers();
        }

        void OnServerSceneLoaded(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> completed, System.Collections.Generic.List<ulong> timedOut)
        {
            if (sceneName != content.mapScene || Match.MatchManager.Instance != null) return;
            BHLog.Info("Server", $"Map '{sceneName}' loaded; spawning match");
            GameObject match = Instantiate(content.matchManagerPrefab);
            match.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool parsed = ConnectionPayload.TryParse(request.Payload, out ConnectionPayload payload);
            int connected = ConnectionRegistry.Count;
            string reject = ConnectionRegistry.Validate(payload, parsed, connected, _config.maxPlayers);

            response.CreatePlayerObject = false;
            if (reject != null)
            {
                response.Approved = false;
                response.Reason = reject;
                BHLog.Warn("Server", $"Rejected client {request.ClientNetworkId}: {reject}");
                return;
            }

            string name = ConnectionRegistry.SanitizeName(payload.playerName, request.ClientNetworkId);
            ConnectionRegistry.Register(request.ClientNetworkId, name);
            response.Approved = true;
        }

        public void NotifyMatchCompleted()
        {
            HostingServices.Adapter.OnMatchCompleted();
            _matchesCompleted++;
            if (_config.quitAfterMatches > 0 && _matchesCompleted >= _config.quitAfterMatches)
            {
                BHLog.Info("Server", $"Completed {_matchesCompleted} match(es); quitting as requested.");
                Quit(0);
            }
        }

        // ───────────── Client ─────────────

        public async void StartClient(ServerEndpoint endpoint, string playerName)
        {
            _session = new DirectSessionConnector(endpoint.Address, endpoint.Port);
            SetClientState(ClientConnectionState.Connecting, "");
            ServerEndpoint target;
            try
            {
                target = await _session.FindMatchAsync(playerName);
            }
            catch (Exception e)
            {
                SetClientState(ClientConnectionState.Disconnected, $"Matchmaking failed: {e.Message}");
                return;
            }

            LastEndpoint = target;
            _transport.SetConnectionData(true, target.Address, target.Port);
            _network.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                protocol = ConnectionPayload.CurrentProtocol,
                playerName = playerName,
            }.ToBytes();

            BHLog.Info("Client", $"Connecting to {target} as '{playerName}'");
            Player.SnapshotClock.Reset();
            ConfigureSocket(server: false);
            if (!_network.StartClient())
                SetClientState(ClientConnectionState.Disconnected, "Could not start the network client.");
        }

        public void Disconnect()
        {
            if (_network.IsListening)
            {
                BHLog.Info("Client", "Disconnecting");
                _network.Shutdown();
            }
            _ = _session?.LeaveAsync();
            ConnectionRegistry.Clear();
            SetClientState(ClientConnectionState.Offline, "");
            LaunchConfig.Current.role = LaunchRole.Menu;
            if (SceneManager.GetActiveScene().name != MenuScene)
                SceneManager.LoadScene(MenuScene);
        }

        void OnClientConnected(ulong clientId)
        {
            if (_network.IsServer)
            {
                BHLog.Info("Server", $"Client {clientId} '{ConnectionRegistry.NameOf(clientId)}' connected");
                HostingServices.Adapter.OnPlayerCountChanged(ConnectionRegistry.Count);
            }
            if (clientId == _network.LocalClientId && _network.IsClient)
            {
                BHLog.Info("Client", $"Connected as client {clientId}");
                SetClientState(ClientConnectionState.Connected, "");
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (_network.IsServer && clientId != NetworkManager.ServerClientId)
            {
                BHLog.Info("Server", $"Client {clientId} '{ConnectionRegistry.NameOf(clientId)}' disconnected");
                ConnectionRegistry.Unregister(clientId);
                HostingServices.Adapter.OnPlayerCountChanged(ConnectionRegistry.Count);
                return;
            }

            if (!_network.IsServer)
            {
                string reason = string.IsNullOrEmpty(_network.DisconnectReason)
                    ? (ClientState == ClientConnectionState.Connecting ? "Could not reach the server." : "Connection to the server was lost.")
                    : _network.DisconnectReason;
                BHLog.Warn("Client", $"Disconnected: {reason}");
                ReturnToMenu(reason);
            }
        }

        void OnTransportFailure()
        {
            BHLog.Error("Network", "Transport failure");
            if (IsDedicatedServer) Quit(1);
            else ReturnToMenu("Network transport failure.");
        }

        void ReturnToMenu(string reason)
        {
            if (_network.IsListening) _network.Shutdown();
            _ = _session?.LeaveAsync();
            ConnectionRegistry.Clear();
            SetClientState(ClientConnectionState.Disconnected, reason);
            LaunchConfig.Current.role = LaunchRole.Menu;
            if (LaunchConfig.Current.bot)
            {
                // Automated test clients exit when the server goes away.
                Quit(0);
                return;
            }
            if (SceneManager.GetActiveScene().name != MenuScene)
                SceneManager.LoadScene(MenuScene);
        }

        void SetClientState(ClientConnectionState state, string reason)
        {
            ClientState = state;
            LastDisconnectReason = reason;
            StateChanged?.Invoke();
        }

        // ───────────── Lifetime ─────────────

        void QuitFromTimer()
        {
            BHLog.Info("Boot", $"quitAfter {_config.quitAfterSeconds}s reached");
            Quit(0);
        }

        public void Quit(int exitCode)
        {
            if (IsDedicatedServer) HostingServices.Adapter.OnShutdown();
            if (_network != null && _network.IsListening) _network.Shutdown();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }
    }
}
