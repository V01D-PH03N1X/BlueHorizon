using System.Collections.Generic;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Objectives
{
    /// <summary>A plantable area. Containment is an oriented box test so it works on the dedicated server without triggers.</summary>
    public sealed class BombSite : MonoBehaviour
    {
        public static readonly List<BombSite> All = new();

        public int index;
        public string label = "A";
        public Vector3 size = new(8f, 4f, 8f);
        public Color color = new(1f, 0.75f, 0.2f);

        void OnEnable()
        {
            All.Add(this);
            All.Sort((a, b) => a.index.CompareTo(b.index));
        }

        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && local.y >= -0.5f && local.y <= size.y && Mathf.Abs(local.z) <= half.z;
        }

        public static int SiteAt(Vector3 position)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Contains(position)) return All[i].index;
            }
            return -1;
        }

        public static BombSite ByIndex(int index)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].index == index) return All[i];
            }
            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
        }
    }
}
