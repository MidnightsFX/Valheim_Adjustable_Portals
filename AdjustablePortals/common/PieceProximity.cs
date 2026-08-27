using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.common {

    /// <summary>
    /// Cell bucketed snapshot of every loaded piece position, refreshed at most once every
    /// <see cref="SnapshotLifetimeSeconds"/> and shared by every caller inside that window.
    /// </summary>
    /// <remarks>
    /// Piece.GetAllPiecesInRadius walks the whole process global Piece.s_allPieces and reads three
    /// Unity properties per element - gameObject, transform and transform.position are each a
    /// managed to native call - plus a square root, and the radius only decides whether a piece is
    /// added, never how far the loop runs. It has no vanilla callers at all, so nobody upstream
    /// ever had a reason to index it. Asking it once per portal costs portals x loaded_pieces
    /// interop calls every interval; asking it here costs loaded_pieces once, and every query
    /// after that is float maths over a list of structs.
    ///
    /// A count from here can lag reality by up to SnapshotLifetimeSeconds, and the caller's own
    /// per portal cache adds its own interval on top. Both are fine for a gate on "are there
    /// hundreds of building pieces here", which is not a thing that changes in twenty seconds.
    ///
    /// This deliberately does not patch Piece.GetAllPiecesInRadius. Serving that signature would
    /// mean holding Piece references rather than positions, and every other mod calling it would
    /// silently inherit this snapshot's staleness.
    /// </remarks>
    internal static class PieceProximity {

        // Half a zone. Large enough that a 100m query walks under 64 buckets, small enough that the
        // square of buckets covering the query only overshoots the circle by about 2x. A power of
        // two so the bucket index falls out of a floor rather than a real divide.
        private const float CellSize = 32f;
        // Deliberately the same as ActivationRequirements' per portal scan interval, so the two
        // staleness windows compose to at most twice this. Halving it would double how often the
        // rebuild - the single most expensive frame this class produces - runs, to buy accuracy
        // nobody can observe.
        private const float SnapshotLifetimeSeconds = 10f;

        // Positions, not Piece references: a Vector3 cannot rot into a MissingReferenceException
        // between rebuilds, and it does not pin a destroyed GameObject's managed wrapper alive.
        // Keyed on the packed x/z cell, see CellKey.
        private static readonly Dictionary<long, List<Vector3>> cells = new Dictionary<long, List<Vector3>>();

        private static float nextRebuildTime = 0f;
        private static bool built = false;

        /// <summary>
        /// Number of loaded pieces within <paramref name="radius"/> of <paramref name="center"/>,
        /// refreshing the shared snapshot first if it has aged out.
        /// </summary>
        internal static int CountWithin(Vector3 center, float radius) {
            RebuildIfStale();

            // Vanilla's test is "distance < radius", which nothing can satisfy at zero. Guarding
            // rather than leaning on the comparison, because squaring would turn a negative radius
            // into a positive threshold that matches everything.
            if (radius <= 0f) {
                return 0;
            }

            float radiusSq = radius * radius;
            int minX = CellIndex(center.x - radius);
            int maxX = CellIndex(center.x + radius);
            int minZ = CellIndex(center.z - radius);
            int maxZ = CellIndex(center.z + radius);

            int count = 0;
            for (int cellX = minX; cellX <= maxX; cellX++) {
                for (int cellZ = minZ; cellZ <= maxZ; cellZ++) {
                    // Buckets are x/z only but the test below stays 3D, matching vanilla. That is
                    // sound because anything inside a sphere of radius r is also inside the
                    // horizontal circle of radius r, so no bucket outside this square can hold a
                    // hit.
                    if (cells.TryGetValue(CellKey(cellX, cellZ), out List<Vector3> bucket) == false) {
                        continue;
                    }
                    for (int i = 0; i < bucket.Count; i++) {
                        Vector3 offset = bucket[i] - center;
                        // Squared, so there is no square root per element. This disagrees with
                        // vanilla's Vector3.Distance only for a piece sitting within about one ULP
                        // of the radius - eight micrometres at radius 100 - and then only by one,
                        // against a threshold in the hundreds.
                        if (offset.sqrMagnitude < radiusSq) {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Drops the snapshot outright. Time.time keeps running across a world change, so without
        /// this the geometry of the world just left would stay valid into the first seconds of the
        /// next one.
        /// </summary>
        internal static void Clear() {
            cells.Clear();
            nextRebuildTime = 0f;
            built = false;
        }

        /// <summary>
        /// Rebuilds the snapshot if it has aged out. Lazy on purpose: with piece requirements
        /// switched off, or no portal loaded, nothing in this class ever runs.
        /// </summary>
        private static void RebuildIfStale() {
            if (built && Time.time < nextRebuildTime) {
                return;
            }

            foreach (List<Vector3> bucket in cells.Values) {
                // Cleared rather than dropped, so the lists keep their capacity. A rebuild in a
                // base that is not actively being built in then allocates nothing at all.
                bucket.Clear();
            }

            // Read once into locals so the loop does not touch a static per element.
            // Piece.s_ghostLayer is assigned lazily by the first Piece.Awake, so it can still be
            // zero here - but Piece.Awake is also what appends to s_allPieces, so when it is zero
            // the loop below has nothing to iterate and the value is never used.
            int ghostLayer = Piece.s_ghostLayer;
            List<Piece> allPieces = Piece.s_allPieces;
            for (int i = 0; i < allPieces.Count; i++) {
                Piece piece = allPieces[i];
                // The same predicate Piece.GetAllPiecesInRadius uses. The placement ghost the
                // player is holding lives in s_allPieces too - Player.SetupPlacementGhost
                // instantiates the real prefab and only moves it onto the ghost layer afterwards -
                // and counting it would make every count near a player jitter by one whenever a
                // hammer is out.
                if (piece.gameObject.layer == ghostLayer) {
                    continue;
                }

                Vector3 position = piece.transform.position;
                long key = CellKey(CellIndex(position.x), CellIndex(position.z));
                if (cells.TryGetValue(key, out List<Vector3> bucket) == false) {
                    bucket = new List<Vector3>();
                    cells[key] = bucket;
                }
                bucket.Add(position);
            }

            nextRebuildTime = Time.time + SnapshotLifetimeSeconds;
            built = true;

            // Rebuilding in one pass rather than spreading it over frames is deliberate:
            // Piece.OnDestroy swap removes from s_allPieces, so a partial walk of a list being
            // reordered underneath it can count a piece twice or miss one entirely.
        }

        private static int CellIndex(float world) {
            return Mathf.FloorToInt(world / CellSize);
        }

        // Packs the two signed cell coordinates into one key. The cast through uint stops a
        // negative z from sign extending across the x half of the key and colliding with a
        // different cell entirely.
        private static long CellKey(int cellX, int cellZ) {
            return ((long)cellX << 32) | (uint)cellZ;
        }
    }
}
