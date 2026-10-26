using System.Collections.Generic;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Objectives
{
    /// <summary>Waypoints the automated test bot follows from a side's spawn towards a bomb site.</summary>
    public sealed class BotRoute : MonoBehaviour
    {
        public static readonly List<BotRoute> All = new();

        public Side side = Side.Attack;
        public int siteIndex;
        public Vector3[] points = System.Array.Empty<Vector3>();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = side == Side.Attack ? Color.red : Color.cyan;
            for (int i = 0; i + 1 < points.Length; i++)
                Gizmos.DrawLine(points[i] + Vector3.up * 0.2f, points[i + 1] + Vector3.up * 0.2f);
        }
    }
}
