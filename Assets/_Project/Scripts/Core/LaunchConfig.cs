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
        public int maxPlayers = 10;

        /// <summary>Run the scripted test bot instead of keyboard/mouse input (automated multiplayer tests).</summary>
        public bool bot;
        /// <summary>Test bot behaviour: "fight" (default), "passive" (objectives only, never shoots) or "idle" (does nothing).</summary>
        public string botMode = "fight";
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

        // Optional match-rule overrides (negative = use the GameMode asset value).
        public float roundTime = -1f;
        public float preRoundTime = -1f;
        public float postRoundTime = -1f;
        public float plantTime = -1f;
        public float defuseTime = -1f;
        public float fuseTime = -1f;
        public int roundsToWin = -1;
        public int minPlayersPerTeam = -1;

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
            config.maxPlayers = Mathf.Clamp(args.GetInt("maxPlayers", config.maxPlayers), 1, 10);
            config.bot |= args.Has("bot");
            config.botMode = args.GetString("botMode", config.botMode).ToLowerInvariant();
            config.devSoloStart |= args.Has("devSoloStart");
            config.quitAfterSeconds = args.GetFloat("quitAfter", config.quitAfterSeconds);
            config.quitAfterMatches = args.GetInt("quitAfterMatches", config.quitAfterMatches);
            config.perfLogInterval = args.GetFloat("perfLogInterval", config.perfLogInterval);
            config.screenshotInterval = args.GetFloat("screenshotInterval", config.screenshotInterval);
            config.screenshotDir = args.GetString("screenshotDir", config.screenshotDir);
            config.roundTime = args.GetFloat("roundTime", config.roundTime);
            config.preRoundTime = args.GetFloat("preRoundTime", config.preRoundTime);
            config.postRoundTime = args.GetFloat("postRoundTime", config.postRoundTime);
            config.plantTime = args.GetFloat("plantTime", config.plantTime);
            config.defuseTime = args.GetFloat("defuseTime", config.defuseTime);
            config.fuseTime = args.GetFloat("fuseTime", config.fuseTime);
            config.roundsToWin = args.GetInt("roundsToWin", config.roundsToWin);
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
