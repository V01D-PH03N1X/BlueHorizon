using System;
using System.IO;
using UnityEngine;

namespace BlueHorizon.Core
{
    public enum LaunchRole
    {
        /// <summary>Show the main menu; the player chooses what to do.</summary>
        Menu,
        Client,
        Server,
        /// <summary>Development-only listen server (server + local player in one process).</summary>
        Host,
    }

    /// <summary>
    /// Process-wide launch settings resolved from (in increasing priority):
    /// defaults, an optional JSON file (server.json next to the executable, or -config path), and command-line arguments.
    /// No secrets belong here: this file ships with builds.
    /// </summary>
    [Serializable]
    public sealed class LaunchConfig
    {
        public const ushort DefaultPort = 7777;

        public LaunchRole role = LaunchRole.Menu;
        public string address = "127.0.0.1";
        public string listenAddress = "0.0.0.0";
        public ushort port = DefaultPort;
        public string playerName = "";
        public int maxPlayers = 16;
        /// <summary>Game mode id (heavybomb, duel, deathmatch, capture, weapongame). The mode also caps the player count.</summary>
        public string mode = "heavybomb";

        /// <summary>Run the scripted test bot instead of keyboard/mouse input (automated multiplayer tests).</summary>
        public bool bot;
        /// <summary>Test bot behaviour: "fight" (default), "passive" (objectives only, never shoots), "idle" (does nothing), "strafe" or "ads" (stands still aiming down sights).</summary>
        public string botMode = "fight";
        /// <summary>Test bot: force the attack site (0 = A, 1 = B); -1 alternates by round.</summary>
        public int botSite = -1;
        /// <summary>Test bot loadout: "" (default weapons), "random" or an explicit "p,s,pm,pu,po,sm,su,so" index list.</summary>
        public string botLoadout = "";
        /// <summary>Test bot agent: "" (Striker / the saved default), "random" (any available) or an agent id.</summary>
        public string botAgent = "";
        /// <summary>Open the loadout panel once the local player has spawned (visual review of the UI).</summary>
        public bool showLoadout;
        /// <summary>Allow a match to start with a single connected player (development only).</summary>
        public bool devSoloStart;
        /// <summary>Quit the process after this many seconds (0 = never). Used by automated tests.</summary>
        public float quitAfterSeconds;
        /// <summary>Quit the server after this many matches finish (0 = never). Used by automated tests.</summary>
        public int quitAfterMatches;

        /// <summary>Seconds between performance log lines (0 = off). Defaults on for servers.</summary>
        public float perfLogInterval = -1f;
        /// <summary>Capture a screenshot every N seconds into <see cref="screenshotDir"/> (graphical clients, testing).</summary>
        public float screenshotInterval;
        public string screenshotDir = "";

        /// <summary>Server lag compensation (hitbox rewind). On by default; -noLagComp disables it for comparison.</summary>
        public bool lagCompensation = true;
        /// <summary>Longest rewind the server accepts, milliseconds. Bounds what a client can claim to have seen.</summary>
        public int maxRewindMs = 300;
        /// <summary>Simulated network conditions for testing (both directions, this process only).</summary>
        public int simLatencyMs;
        public int simJitterMs;
        public float simLossPercent;

        public Networking.SimulatedNetworkConditions NetworkConditions => new()
        {
            latencyMs = simLatencyMs,
            jitterMs = simJitterMs,
            lossPercent = simLossPercent,
        };

        // Optional match-rule overrides (negative = use the GameMode asset value).
        public float roundTime = -1f;
        public float preRoundTime = -1f;
        public float postRoundTime = -1f;
        public float plantTime = -1f;
        public float defuseTime = -1f;
        public float fuseTime = -1f;
        public int roundsToWin = -1;
        public int minPlayersPerTeam = -1;
        public float respawnDelay = -1f;
        /// <summary>Downed state override: "on", "off" or "" (mode default).</summary>
        public string downed = "";

        /// <summary>Server: agent ids nobody may pick this session (comma separated).</summary>
        public string banAgents = "";
        /// <summary>Server: locked agents made available ("all" or comma-separated ids). No progression system yet.</summary>
        public string unlockAgents = "";

        static LaunchConfig _current;

        public static LaunchConfig Current => _current ??= Resolve(CommandLineArgs.FromEnvironment());

        /// <summary>Overrides the resolved configuration, e.g. from the main menu.</summary>
        public static void Set(LaunchConfig config) => _current = config;

        public LaunchConfig Clone() => (LaunchConfig)MemberwiseClone();

        public static LaunchConfig Resolve(CommandLineArgs args)
        {
            var config = new LaunchConfig();

            string configPath = args.GetString("config", DefaultConfigPath());
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(configPath), config);
                    BHLog.Info("Config", $"Loaded {configPath}");
                }
                catch (Exception e)
                {
                    BHLog.Error("Config", $"Failed to read {configPath}: {e.Message}");
                }
            }

#if UNITY_SERVER
            config.role = LaunchRole.Server;
#endif
            if (args.Has("server")) config.role = LaunchRole.Server;
            else if (args.Has("host")) config.role = LaunchRole.Host;
            else if (args.Has("connect"))
            {
                config.role = LaunchRole.Client;
                config.address = args.GetString("connect", config.address);
            }

            config.address = args.GetString("address", config.address);
            config.listenAddress = args.GetString("listen", config.listenAddress);
            config.port = (ushort)Mathf.Clamp(args.GetInt("port", config.port), 1, 65535);
            config.playerName = args.GetString("name", config.playerName);
            config.maxPlayers = Mathf.Clamp(args.GetInt("maxPlayers", config.maxPlayers), 1, 16);
            config.mode = args.GetString("mode", config.mode).ToLowerInvariant();
            config.respawnDelay = args.GetFloat("respawnDelay", config.respawnDelay);
            config.bot |= args.Has("bot");
            config.botMode = args.GetString("botMode", config.botMode).ToLowerInvariant();
            config.botSite = args.GetInt("botSite", config.botSite);
            config.botLoadout = args.GetString("botLoadout", config.botLoadout).ToLowerInvariant();
            config.botAgent = args.GetString("botAgent", config.botAgent).ToLowerInvariant();
            config.downed = args.GetString("downed", config.downed).ToLowerInvariant();
            config.banAgents = args.GetString("banAgents", config.banAgents).ToLowerInvariant();
            config.unlockAgents = args.GetString("unlockAgents", config.unlockAgents).ToLowerInvariant();
            config.showLoadout |= args.Has("showLoadout");
            config.devSoloStart |= args.Has("devSoloStart");
            config.quitAfterSeconds = args.GetFloat("quitAfter", config.quitAfterSeconds);
            config.quitAfterMatches = args.GetInt("quitAfterMatches", config.quitAfterMatches);
            config.perfLogInterval = args.GetFloat("perfLogInterval", config.perfLogInterval);
            config.screenshotInterval = args.GetFloat("screenshotInterval", config.screenshotInterval);
            config.screenshotDir = args.GetString("screenshotDir", config.screenshotDir);
            if (args.Has("noLagComp")) config.lagCompensation = false;
            config.maxRewindMs = Mathf.Clamp(args.GetInt("maxRewind", config.maxRewindMs), 0, 1000);
            config.simLatencyMs = Mathf.Max(0, args.GetInt("simLatency", config.simLatencyMs));
            config.simJitterMs = Mathf.Max(0, args.GetInt("simJitter", config.simJitterMs));
            config.simLossPercent = Mathf.Clamp(args.GetFloat("simLoss", config.simLossPercent), 0f, 100f);
            config.roundTime = args.GetFloat("roundTime", args.GetFloat("timeLimit", config.roundTime));
            config.preRoundTime = args.GetFloat("preRoundTime", config.preRoundTime);
            config.postRoundTime = args.GetFloat("postRoundTime", config.postRoundTime);
            config.plantTime = args.GetFloat("plantTime", config.plantTime);
            config.defuseTime = args.GetFloat("defuseTime", config.defuseTime);
            config.fuseTime = args.GetFloat("fuseTime", config.fuseTime);
            config.roundsToWin = args.GetInt("roundsToWin", args.GetInt("scoreLimit", config.roundsToWin));
            config.minPlayersPerTeam = args.GetInt("minPlayersPerTeam", config.minPlayersPerTeam);
            return config;
        }

        static string DefaultConfigPath()
        {
            // Application.dataPath is <exe>_Data in players; server.json sits next to the executable.
            string dir = Path.GetDirectoryName(Application.dataPath);
            return dir == null ? null : Path.Combine(dir, "server.json");
        }
    }
}
