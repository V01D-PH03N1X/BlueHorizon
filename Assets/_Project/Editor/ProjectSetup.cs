using System;
using System.Reflection;
using BlueHorizon.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BlueHorizon.EditorTools
{
    /// <summary>
    /// One-click, repeatable generation of the vertical slice content. Run from the menu
    /// (Blue Horizon ▸ Generate Project Content) or headless:
    ///   Unity -batchmode -quit -projectPath . -executeMethod BlueHorizon.EditorTools.ProjectSetup.GenerateAll
    /// Data assets (weapons, agent, abilities, game mode) are created only when missing so balance edits survive;
    /// prefabs, materials and scenes are regenerated from code every time.
    /// </summary>
    public static class ProjectSetup
    {
        public const string Root = "Assets/_Project";

        [MenuItem("Blue Horizon/Generate Project Content", priority = 0)]
        public static void GenerateAll()
        {
            try
            {
                ConfigureProjectSettings();
                GameContent content = ContentFactory.CreateOrUpdateContent();
                MapBuilder.BuildScenes(content);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[BH][Setup] Project content generated.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Blue Horizon/Rebuild Map Scenes Only", priority = 1)]
        public static void RebuildScenes()
        {
            GameContent content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentFactory.ContentPath);
            if (content == null)
            {
                GenerateAll();
                return;
            }
            MapBuilder.BuildScenes(content);
        }

        public static void ConfigureProjectSettings()
        {
            SetLayer(GameLayers.Player, GameLayers.PlayerName);
            SetLayer(GameLayers.Hitbox, GameLayers.HitboxName);
            SetLayer(GameLayers.Deployable, GameLayers.DeployableName);
            SetLayer(GameLayers.Trigger, GameLayers.TriggerName);
            SetLayer(GameLayers.Projectile, GameLayers.ProjectileName);

            // Hitboxes and triggers are query-only; they never take part in collision.
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(GameLayers.Hitbox, i, true);
                Physics.IgnoreLayerCollision(GameLayers.Trigger, i, true);
                Physics.IgnoreLayerCollision(GameLayers.Projectile, i, true);
            }
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Player, false);
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Deployable, false);

            PlayerSettings.companyName = "Sebastian Schindler";
            PlayerSettings.productName = "Blue Horizon";
            PlayerSettings.bundleVersion = "0.1.0";
            // Several clients on one machine must keep simulating when unfocused.
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);

            DisableNetcodeDefaultPrefabGeneration();
        }

        static void SetLayer(int index, string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (layer.stringValue == name) return;
            if (!string.IsNullOrEmpty(layer.stringValue))
                Debug.LogWarning($"[BH][Setup] Replacing layer {index} '{layer.stringValue}' with '{name}'");
            layer.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// We register network prefabs through our own NetworkPrefabsList; stop Netcode from generating a second list.
        /// The settings type exposes the flag publicly but its save method is internal, hence reflection.
        /// </summary>
        static void DisableNetcodeDefaultPrefabGeneration()
        {
            Type type = Type.GetType("Unity.Netcode.Editor.Configuration.NetcodeForGameObjectsProjectSettings, Unity.Netcode.Editor");
            if (type == null) return;
            object instance = type.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            if (instance == null) return;
            FieldInfo flag = type.GetField("GenerateDefaultNetworkPrefabs");
            if (flag == null || !(bool)flag.GetValue(instance)) return;
            flag.SetValue(instance, false);
            type.GetMethod("SaveSettings", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
        }
    }
}
