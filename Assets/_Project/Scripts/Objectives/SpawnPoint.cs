using System.Collections.Generic;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Objectives
{
    /// <summary>A spawn location for one side. The transform's forward sets the initial facing.</summary>
    public sealed class SpawnPoint : MonoBehaviour
    {
        public Side side = Side.Attack;

        void OnDrawGizmos()
        {
            Gizmos.color = side == Side.Attack ? new Color(1f, 0.5f, 0.2f) : new Color(0.2f, 0.6f, 1f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward);
        }
    }
}
