using System;
using System.IO;
using System.Linq;
using BlueHorizon.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BlueHorizon.EditorTools
{
    /// <summary>
    /// Reproducible client and dedicated-server builds.
    /// Headless:
    ///   Unity -batchmode -quit -projectPath . -executeMethod BlueHorizon.EditorTools.BuildTools.BuildAll [-buildOutput Builds] [-development]
    /// Outputs Builds/Client/BlueHorizon.exe and Builds/Server/BlueHorizonServer.exe (+ server.json).
    /// </summary>
    public static class BuildTools
    {
        public const string DefaultOutput = "Builds";

        [MenuItem("Blue Horizon/Build/Client + Dedicated Server", priority = 20)]
        public static void BuildAll()
        {
            bool ok = BuildClientInternal() && BuildServerInternal();
            Finish(ok);
        }

        [MenuItem("Blue Horizon/Build/Windows Client", priority = 21)]
        public static void BuildClient() => Finish(BuildClientInternal());

        [MenuItem("Blue Horizon/Build/Dedicated Server (Windows)", priority = 22)]
        public static void BuildServer() => Finish(BuildServerInternal());

        [MenuItem("Blue Horizon/Build/Dedicated Server (Linux, experimental)", priority = 23)]
        public static void BuildLinuxServer()
        {
            string dir = Path.Combine(OutputRoot(), "ServerLinux");
            Finish(Build("Linux dedicated server", Path.Combine(dir, "BlueHorizonServer.x86_64"), BuildTarget.StandaloneLinux64,
                StandaloneBuildSubtarget.Server, dir));
        }

        static bool BuildClientInternal()
        {
            string dir = Path.Combine(OutputRoot(), "Client");
            return Build("Windows client", Path.Combine(dir, "BlueHorizon.exe"), BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player, null);
        }

        static bool BuildServerInternal()
        {
            string dir = Path.Combine(OutputRoot(), "Server");
            return Build("Windows dedicated server", Path.Combine(dir, "BlueHorizonServer.exe"), BuildTarget.StandaloneWindows64,
                StandaloneBuildSubtarget.Server, dir);
        }

        static bool Build(string label, string path, BuildTarget target, StandaloneBuildSubtarget subtarget, string serverConfigDir)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[BH][Build] No scenes in build settings. Run Blue Horizon ▸ Generate Project Content first.");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                subtarget = (int)subtarget,
                options = HasArg("-development") ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log($"[BH][Build] Building {label} → {path}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BH][Build] {label} FAILED: {summary.result}, {summary.totalErrors} error(s)");
                return false;
            }

            if (serverConfigDir != null)
                WriteDefaultServerConfig(Path.Combine(serverConfigDir, "server.json"));

            Debug.Log($"[BH][Build] {label} succeeded: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:0}s");
            return true;
        }

        /// <summary>Writes an editable default config next to the server. Never contains secrets.</summary>
        static void WriteDefaultServerConfig(string path)
        {
            var config = new LaunchConfig
            {
                role = LaunchRole.Server,
                port = LaunchConfig.DefaultPort,
                maxPlayers = 16, // The selected mode caps this further (HeavyBomb 10, Deathmatch 15, ...).
            };
            File.WriteAllText(path, JsonUtility.ToJson(config, true));
        }

        static string OutputRoot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildOutput");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultOutput;
        }

        static bool HasArg(string arg) => Environment.GetCommandLineArgs().Contains(arg);

        static void Finish(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
