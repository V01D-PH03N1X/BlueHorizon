using UnityEngine;

namespace BlueHorizon.Objectives
{
    /// <summary>The Capture-the-Area zone. Oriented-box containment so the dedicated server needs no triggers.</summary>
    public sealed class CaptureZone : MonoBehaviour
    {
        public static CaptureZone Main { get; private set; }

        public Vector3 size = new(12f, 4f, 7f);

        void OnEnable() => Main = this;

        void OnDisable()
        {
            if (Main == this) Main = null;
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && local.y >= -0.5f && local.y <= size.y && Mathf.Abs(local.z) <= half.z;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
        }
    }
}
