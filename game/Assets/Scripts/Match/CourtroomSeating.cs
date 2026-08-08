using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// Where each role stands when the trial opens, and the fallback that puts them
    /// there if they did not walk.
    ///
    /// WHY A TELEPORT EXISTS AT ALL. The walk to court is a courtesy — players get
    /// ~18 seconds to arrive under their own steam, which is better theatre than a
    /// cut. But a match that waits indefinitely for one person who has wandered off
    /// or is being deliberately obstructive is a match nobody finishes. The deadline
    /// is what makes the courtesy safe to offer.
    ///
    /// Positions mirror the courtroom greybox: judge north, counsel opposed across
    /// the aisle, defendant beside their attorney, investigator behind the bar.
    /// </summary>
    public static class CourtroomSeating
    {
        // Courtroom centre is 40 m north of the atrium (RoomDistance in the builder).
        private const float CourtCentreZ = 40f;

        /// <summary>Standing spots, in courtroom-local terms.</summary>
        private static readonly Dictionary<PlayerRole, Vector3> Spots = new()
        {
            { PlayerRole.Prosecutor,       new Vector3(-4.0f, 0.3f, CourtCentreZ - 4.0f) },
            { PlayerRole.Investigator,     new Vector3(-4.0f, 0.3f, CourtCentreZ - 7.0f) },
            { PlayerRole.DefenseAttorney,  new Vector3( 4.0f, 0.3f, CourtCentreZ - 4.0f) },
            { PlayerRole.Defendant,        new Vector3( 4.0f, 0.3f, CourtCentreZ - 6.0f) },
        };

        /// <summary>Fallback for an unseated or unknown role.</summary>
        private static readonly Vector3 Gallery = new(0f, 0.3f, CourtCentreZ - 9.5f);

        public static Vector3 SpotFor(PlayerRole role) =>
            Spots.TryGetValue(role, out var spot) ? spot : Gallery;

        /// <summary>How far from their spot still counts as "they walked there".</summary>
        public const float ArrivedRadius = 6f;

        public static bool HasArrived(Transform player, PlayerRole role)
        {
            if (player == null) return false;
            Vector3 spot = SpotFor(role);
            Vector3 flat = new(player.position.x - spot.x, 0f, player.position.z - spot.z);
            return flat.sqrMagnitude <= ArrivedRadius * ArrivedRadius;
        }

        /// <summary>
        /// SERVER ONLY. Puts everyone who has not arrived into their seat and returns
        /// how many had to be moved.
        ///
        /// The CharacterController is disabled around the write — a live one
        /// overrides direct transform assignment, which is the same trap the player
        /// respawn path already documents.
        /// </summary>
        public static int ServerSeatAll()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer) return 0;

            var roster = PlayerRoster.Instance;
            int moved = 0;

            foreach (var client in manager.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null) continue;

                var role = roster != null ? roster.RoleOf(client.ClientId) : PlayerRole.Unassigned;
                if (HasArrived(player.transform, role)) continue;

                var controller = player.GetComponent<CharacterController>();
                bool had = controller != null && controller.enabled;
                if (had) controller.enabled = false;

                player.transform.position = SpotFor(role);
                player.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

                if (had) controller.enabled = true;
                moved++;
            }

            return moved;
        }
    }
}
