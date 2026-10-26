using System.Collections.Generic;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.Match
{
    public static class TeamAssignment
    {
        /// <summary>Picks the team with fewer players (Alpha on ties). Returns None when both are full.</summary>
        public static Team ChooseTeam(int alphaCount, int bravoCount, int maxPerTeam)
        {
            bool alphaFull = alphaCount >= maxPerTeam;
            bool bravoFull = bravoCount >= maxPerTeam;
            if (alphaFull && bravoFull) return Team.None;
            if (alphaFull) return Team.Bravo;
            if (bravoFull) return Team.Alpha;
            return bravoCount < alphaCount ? Team.Bravo : Team.Alpha;
        }
    }

    public static class SpawnSelector
    {
        /// <summary>
        /// Returns the index of the first candidate (starting at <paramref name="startIndex"/>, wrapping) that is at least
        /// <paramref name="clearance"/> metres from every occupied position. Falls back to the start index when all are blocked.
        /// Returns -1 when there are no candidates.
        /// </summary>
        public static int Select(IReadOnlyList<Vector3> candidates, IReadOnlyList<Vector3> occupied, int startIndex, float clearance)
        {
            if (candidates == null || candidates.Count == 0) return -1;
            int count = candidates.Count;
            int start = ((startIndex % count) + count) % count;
            float clearanceSqr = clearance * clearance;
            for (int n = 0; n < count; n++)
            {
                int i = (start + n) % count;
                bool free = true;
                for (int j = 0; j < occupied.Count; j++)
                {
                    if ((occupied[j] - candidates[i]).sqrMagnitude < clearanceSqr)
                    {
                        free = false;
                        break;
                    }
                }
                if (free) return i;
            }
            return start;
        }
    }
}
