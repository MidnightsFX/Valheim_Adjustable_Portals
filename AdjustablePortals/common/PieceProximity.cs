using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.common {

    /// <summary>
    /// Counts building pieces around a point from the world's ZDOs, which only the server holds in
    /// full. Piece positions are cached per sector and shared by every count inside
    /// <see cref="SectorLifetimeSeconds"/>, so the portals of one hub pay for their sectors once.
    /// </summary>
    /// <remarks>
    /// Counting loaded Piece instances instead gives every client its own answer: a client only
    /// instantiates the zones its own simulation distance reaches, and those stream in over several
    /// seconds. A player at the edge of a base, or on a lower simulation distance, sees part of it.
    /// </remarks>
    internal static class PieceProximity {

        // A count can lag reality by this plus the caller's recount interval. Fine for a gate on
        // "are there hundreds of building pieces here", which does not change in twenty seconds.
        private const float SectorLifetimeSeconds = 10f;
        private const float PruneIntervalSeconds = 60f;

        private class SectorPieces {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public float ExpiresAt;
        }

        private static readonly Dictionary<Vector2s, SectorPieces> sectors = new Dictionary<Vector2s, SectorPieces>();
        private static readonly Dictionary<int, bool> pieceByPrefab = new Dictionary<int, bool>();
        private static readonly List<ZDO> sectorBuffer = new List<ZDO>();
        private static readonly List<Vector2s> pruneBuffer = new List<Vector2s>();
        // No neighbours either way: FindSectorObjects then returns the one sector it is given.
        private static readonly SimulationDistance SingleSector = new SimulationDistance(0, 0);
        private static float nextPruneTime = 0f;

        /// <summary>
        /// Number of building pieces within <paramref name="radius"/> of <paramref name="center"/>.
        /// </summary>
        internal static int CountWithin(Vector3 center, float radius) {
            // Vanilla's test is "distance < radius", which nothing passes at zero, and a negative
            // radius would square into a threshold that matches everything.
            if (radius <= 0f) {
                return 0;
            }

            float now = Time.time;
            float radiusSq = radius * radius;
            Vector2s min = ZoneSystem.GetZone(new Vector3(center.x - radius, 0f, center.z - radius));
            Vector2s max = ZoneSystem.GetZone(new Vector3(center.x + radius, 0f, center.z + radius));

            int count = 0;
            for (int x = min.x; x <= max.x; x++) {
                for (int z = min.y; z <= max.y; z++) {
                    // Sectors are x/z only but the test stays 3D, matching vanilla's
                    // GetAllPiecesInRadius. Anything inside the sphere is inside the square.
                    List<Vector3> positions = PiecesIn(new Vector2s(x, z), now);
                    for (int i = 0; i < positions.Count; i++) {
                        if ((positions[i] - center).sqrMagnitude < radiusSq) {
                            count++;
                        }
                    }
                }
            }
            return count;
        }

        private static List<Vector3> PiecesIn(Vector2s sector, float now) {
            if (sectors.TryGetValue(sector, out SectorPieces cached) == false) {
                cached = new SectorPieces();
                sectors[sector] = cached;
            } else if (now < cached.ExpiresAt) {
                return cached.Positions;
            }

            cached.Positions.Clear();
            sectorBuffer.Clear();
            // Includes the sector's portals, which ZDOMan keeps apart from everything else and
            // which are pieces too.
            ZDOMan.instance.FindSectorObjects(sector, SingleSector, sectorBuffer);
            for (int i = 0; i < sectorBuffer.Count; i++) {
                ZDO zdo = sectorBuffer[i];
                if (IsPiece(zdo.GetPrefab())) {
                    cached.Positions.Add(zdo.GetPosition());
                }
            }
            sectorBuffer.Clear();
            cached.ExpiresAt = now + SectorLifetimeSeconds;
            return cached.Positions;
        }

        private static bool IsPiece(int prefabHash) {
            if (pieceByPrefab.TryGetValue(prefabHash, out bool isPiece)) {
                return isPiece;
            }
            GameObject prefab = prefabHash == 0 ? null : ZNetScene.instance.GetPrefab(prefabHash);
            if (prefab == null) {
                // Not cached: a mod can still register the prefab later in the session.
                return false;
            }
            isPiece = prefab.GetComponent<Piece>() != null;
            pieceByPrefab[prefabHash] = isPiece;
            return isPiece;
        }

        /// <summary>
        /// Drops sectors nobody has counted in a while, so the cache holds the areas around
        /// portals people are using rather than every area counted since the server started.
        /// </summary>
        internal static void PruneUnused() {
            float now = Time.time;
            if (now < nextPruneTime) {
                return;
            }
            nextPruneTime = now + PruneIntervalSeconds;

            pruneBuffer.Clear();
            foreach (KeyValuePair<Vector2s, SectorPieces> entry in sectors) {
                if (entry.Value.ExpiresAt < now - PruneIntervalSeconds) {
                    pruneBuffer.Add(entry.Key);
                }
            }
            for (int i = 0; i < pruneBuffer.Count; i++) {
                sectors.Remove(pruneBuffer[i]);
            }
            pruneBuffer.Clear();
        }

        internal static void Clear() {
            sectors.Clear();
            // Prefabs can change between sessions as mods are added or removed.
            pieceByPrefab.Clear();
            sectorBuffer.Clear();
            pruneBuffer.Clear();
            nextPruneTime = 0f;
        }
    }
}
