using System;
using System.Collections.Generic;
using BlueHorizon.Core;
using UnityEngine;

namespace BlueHorizon.GameModes
{
    /// <summary>Who may damage and score against whom.</summary>
    public static class TeamRules
    {
        /// <summary>Free-for-all: every other player is an enemy. Team modes: players of the other team.</summary>
        public static bool AreEnemies(Team a, Team b, bool freeForAll) =>
            freeForAll || (a != Team.None && b != Team.None && a != b);
    }

    public static class DuelRules
    {
        /// <summary>Round timeout: the player with more remaining health + armor + barrier wins; equal = draw (None).</summary>
        public static Team TimeoutWinner(int alphaEffectiveHealth, int bravoEffectiveHealth)
        {
            if (alphaEffectiveHealth > bravoEffectiveHealth) return Team.Alpha;
            if (bravoEffectiveHealth > alphaEffectiveHealth) return Team.Bravo;
            return Team.None;
        }
    }

    public struct FfaStanding
    {
        public ulong clientId;
        public int score;
        public int deaths;
    }

    public static class FreeForAllRules
    {
        /// <summary>Index of the leader: highest score, then fewest deaths, then lowest client id. -1 when empty.</summary>
        public static int Leader(IReadOnlyList<FfaStanding> standings)
        {
            int best = -1;
            for (int i = 0; i < standings.Count; i++)
            {
                if (best < 0 || Better(standings[i], standings[best])) best = i;
            }
            return best;
        }

        static bool Better(in FfaStanding a, in FfaStanding b)
        {
            if (a.score != b.score) return a.score > b.score;
            if (a.deaths != b.deaths) return a.deaths < b.deaths;
            return a.clientId < b.clientId;
        }
    }

    public static class WeaponGameRules
    {
        /// <summary>A kill with the current level's weapon. Returns the new level and whether the ladder is complete.</summary>
        public static int Advance(int level, int ladderLength, out bool won)
        {
            won = ladderLength > 0 && level >= ladderLength - 1;
            return won ? level : Math.Min(level + 1, Math.Max(0, ladderLength - 1));
        }
    }

    [Serializable]
    public struct ZoneSettings
    {
        public float captureTime;
        public float capturePerExtraPlayer;
        public float maxCaptureMultiplier;
        public float pointsPerSecond;
    }

    /// <summary>
    /// Capture-the-Area zone. Evaluated by the server every tick from the number of living players of each team inside.
    ///   * Both teams inside: contested — no capture progress, no scoring.
    ///   * One team inside that does not own the zone: progress grows (faster with more players, capped); if another
    ///     team had partial progress it first has to be reverted. Full progress flips ownership.
    ///   * Nobody inside, or only the owner: partial progress decays.
    ///   * The owner scores <see cref="ZoneSettings.pointsPerSecond"/> while not contested.
    /// </summary>
    public sealed class ZoneModel
    {
        readonly ZoneSettings _settings;
        float _pointRemainder;

        public Team Owner { get; private set; }
        public Team Capturing { get; private set; }
        public float Progress { get; private set; }
        public bool Contested { get; private set; }

        public ZoneModel(ZoneSettings settings) => _settings = settings;

        public void Reset()
        {
            Owner = Capturing = Team.None;
            Progress = 0f;
            Contested = false;
            _pointRemainder = 0f;
        }

        /// <returns>Points scored by <see cref="Owner"/> this step.</returns>
        public int Step(int alphaInside, int bravoInside, float dt, out bool ownerChanged)
        {
            ownerChanged = false;
            Contested = alphaInside > 0 && bravoInside > 0;
            float unit = dt / Mathf.Max(0.01f, _settings.captureTime);

            if (!Contested)
            {
                Team present = alphaInside > 0 ? Team.Alpha : bravoInside > 0 ? Team.Bravo : Team.None;
                int count = Math.Max(alphaInside, bravoInside);
                if (present != Team.None && present != Owner)
                {
                    if (Capturing != present && Progress > 0f)
                    {
                        Progress = Mathf.Max(0f, Progress - unit * Multiplier(count)); // Revert the other team's progress first.
                        if (Progress <= 0f) Capturing = present;
                    }
                    else
                    {
                        Capturing = present;
                        Progress += unit * Multiplier(count);
                        if (Progress >= 1f)
                        {
                            Owner = present;
                            Capturing = Team.None;
                            Progress = 0f;
                            ownerChanged = true;
                        }
                    }
                }
                else
                {
                    Progress = Mathf.Max(0f, Progress - unit);
                    if (Progress <= 0f) Capturing = Team.None;
                }
            }

            if (Owner == Team.None || Contested) return 0;
            _pointRemainder += _settings.pointsPerSecond * dt;
            int whole = (int)_pointRemainder;
            _pointRemainder -= whole;
            return whole;
        }

        float Multiplier(int players) =>
            Mathf.Min(_settings.maxCaptureMultiplier <= 0f ? 1f : _settings.maxCaptureMultiplier,
                1f + _settings.capturePerExtraPlayer * Mathf.Max(0, players - 1));
    }
}
