using System.Collections.Generic;
using UnityEngine;

namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: distance alone says a player two metres away through a
    /// sealed courtroom wall is as loud as one standing next to you. In a building
    /// made of rooms that is badly wrong — and for COURT it is worse than wrong,
    /// because "can they hear me from in there" is the actual game.
    ///
    /// The measure is deliberately crude: cast a ray from listener to speaker and
    /// count the solid things in the way. One wall muffles, several silence. No
    /// portal graph, no acoustic simulation — those are worth doing only once the
    /// real courthouse geometry exists.
    ///
    /// Results get cached by the caller, because this runs per listener per speaker
    /// and voice packets arrive fifty times a second.
    /// </summary>
    public static class VoiceOcclusion
    {
        // Raycast results reused between calls; occlusion runs often enough that
        // allocating an array each time would be silently expensive.
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // GetComponentInParent walks the hierarchy, so remember the answer per
        // collider. Colliders in a scene are a small, stable set.
        private static readonly Dictionary<Collider, bool> PlayerColliders = new();

        /// <summary>
        /// How many solid surfaces sit between the two points.
        ///
        /// Player bodies are skipped: standing behind someone should not muffle
        /// them, and a speaker's own collider would otherwise count as a wall.
        /// </summary>
        public static int CountBlockers(Vector3 from, Vector3 to, LayerMask layers)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.05f) return 0;

            int hitCount = Physics.RaycastNonAlloc(
                new Ray(from, delta / distance), Hits, distance,
                layers, QueryTriggerInteraction.Ignore);

            int blockers = 0;
            for (int i = 0; i < hitCount; i++)
            {
                if (IsPlayer(Hits[i].collider)) continue;
                blockers++;
            }
            return blockers;
        }

        /// <summary>0 = clear line of sight, 1 = fully blocked.</summary>
        public static float Sample(Vector3 from, Vector3 to, LayerMask layers, int blockersForFull)
        {
            if (blockersForFull <= 0) return 0f;
            return Mathf.Clamp01(CountBlockers(from, to, layers) / (float)blockersForFull);
        }

        private static bool IsPlayer(Collider collider)
        {
            if (collider == null) return true;   // destroyed mid-frame: ignore it

            if (PlayerColliders.TryGetValue(collider, out bool known)) return known;

            bool isPlayer = collider.GetComponentInParent<PlayerVoice>() != null;
            PlayerColliders[collider] = isPlayer;
            return isPlayer;
        }

        /// <summary>Scene changes invalidate the collider cache.</summary>
        public static void ClearCache() => PlayerColliders.Clear();
    }
}
