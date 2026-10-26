using UnityEngine;

namespace BlueHorizon.Core
{
    /// <summary>
    /// Physics layer indices. The names are written into the TagManager by the project setup tool,
    /// so the indices here and in ProjectSettings/TagManager.asset must stay in sync.
    /// </summary>
    public static class GameLayers
    {
        public const int Default = 0;
        public const int IgnoreRaycast = 2;
        public const int Player = 8;
        public const int Hitbox = 9;
        public const int Deployable = 10;
        public const int Trigger = 11;
        public const int Projectile = 12;

        public const string PlayerName = "Player";
        public const string HitboxName = "Hitbox";
        public const string DeployableName = "Deployable";
        public const string TriggerName = "Trigger";
        public const string ProjectileName = "Projectile";

        /// <summary>Everything a bullet can stop on: world geometry, hitboxes and deployables.</summary>
        public const int ShotMask = (1 << Default) | (1 << Hitbox) | (1 << Deployable);

        /// <summary>Geometry that blocks line of sight and placement (no characters).</summary>
        public const int WorldMask = (1 << Default) | (1 << Deployable);

        public const int PlayerMask = 1 << Player;

        /// <summary>
        /// Applies the collision matrix at runtime as well, so builds never depend on editor-saved physics settings.
        /// Hitboxes, triggers and projectiles are query-only and never collide.
        /// </summary>
        public static void ConfigureCollisionMatrix()
        {
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(Hitbox, i, true);
                Physics.IgnoreLayerCollision(Trigger, i, true);
                Physics.IgnoreLayerCollision(Projectile, i, true);
            }
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
