using System;
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

        // What a rebuild needs from one piece, so that the two properties which resolve a native
        // object back to its managed wrapper are paid for once per piece rather than once per
        // rebuild. See the point of use for why holding these stays correct.
        private struct PieceRefs {
            public Piece Piece;
            public GameObject GameObject;
            public Transform Transform;
        }

        // Indexed in lockstep with Piece.s_allPieces, which is the whole trick: an entry is only
        // trusted while the piece at that index is still the piece it was built from.
        private static PieceRefs[] known = Array.Empty<PieceRefs>();
        private static int knownCount = 0;

        private static float nextRebuildTime = 0f;
        private static bool built = false;

        /// <summary>
        /// Number of loaded pieces within <paramref name="radius"/> of <paramref name="center"/>,
        /// refreshing the shared snapshot first if it has aged out.
        /// </summary>
        internal static int CountWithin(Vector3 center, float radius) {
            // Guarded ahead of the rebuild rather than after it, because at a non-positive radius
            // the answer is zero whatever the snapshot holds, so there is nothing to refresh it
            // for. Vanilla's test is "distance < radius", which nothing can satisfy at zero, and
            // leaning on the comparison instead would turn a negative radius into a positive
            // squared threshold that matches everything.
            if (radius <= 0f) {
                return 0;
            }

            RebuildIfStale();

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
            // Nulled rather than dropped, so the array keeps its capacity into the next world - but
            // nulled rather than merely forgotten, because a live reference to a destroyed piece's
            // wrapper is the thing the buckets store bare positions to avoid.
            Array.Clear(known, 0, knownCount);
            knownCount = 0;
            nextRebuildTime = 0f;
            built = false;
        }

        /// <summary>
        /// Rebuilds the snapshot if it has aged out. Lazy on purpose: with piece requirements
        /// switched off, or no portal loaded, nothing in this class ever runs.
        /// </summary>
        private static void RebuildIfStale() {
            float now = Time.time;
            if (built && now < nextRebuildTime) {
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
            int count = allPieces.Count;

            if (known.Length < count) {
                // Resized rather than replaced: the entries are only worth anything while they stay
                // lined up with s_allPieces' indices, so they have to survive the growth.
                Array.Resize(ref known, Mathf.Max(count, known.Length * 2));
            }

            // s_allPieces is filled in zone load order, so runs of consecutive pieces usually land
            // in the same cell. Holding on to the last bucket turns most of the dictionary probes
            // into a long compare.
            long lastKey = 0;
            List<Vector3> lastBucket = null;

            for (int i = 0; i < count; i++) {
                Piece piece = allPieces[i];

                // Component.gameObject and Component.transform each have to map a native object
                // back to its managed wrapper, and at one of each per piece per rebuild that was
                // the bulk of what a rebuild cost. Neither can change over a Piece's lifetime, so
                // the pair is worth keeping; the only question is whether this slot still describes
                // this piece, and ReferenceEquals answers it without going near UnityEngine.Object's
                // ==, which runs its own native liveness check and would hand the cost straight
                // back.
                //
                // Churn is cheap here rather than ruinous: Piece.OnDestroy swap removes, writing
                // one slot and shortening the list, so a destroyed piece invalidates exactly one
                // entry. New pieces append past the end and are resolved once.
                GameObject gameObject;
                Transform transform;
                if (ReferenceEquals(known[i].Piece, piece)) {
                    gameObject = known[i].GameObject;
                    transform = known[i].Transform;
                } else {
                    gameObject = piece.gameObject;
                    transform = piece.transform;
                    known[i].Piece = piece;
                    known[i].GameObject = gameObject;
                    known[i].Transform = transform;
                }

                // The same predicate Piece.GetAllPiecesInRadius uses, and still read off the object
                // every rebuild rather than cached beside the wrappers, so a piece whose layer
                // changes under us is picked up exactly as it was before. The placement ghost the
                // player is holding lives in s_allPieces too - Player.SetupPlacementGhost
                // instantiates the real prefab and only moves it onto the ghost layer afterwards -
                // and counting it would make every count near a player jitter by one whenever a
                // hammer is out.
                if (gameObject.layer == ghostLayer) {
                    continue;
                }

                Vector3 position = transform.position;
                long key = CellKey(CellIndex(position.x), CellIndex(position.z));
                if (lastBucket != null && key == lastKey) {
                    lastBucket.Add(position);
                    continue;
                }

                if (cells.TryGetValue(key, out List<Vector3> bucket) == false) {
                    // Sized past the 1-2-4 growth an empty list would walk through, because the
                    // rebuilds that create cells are the ones running while a zone streams in,
                    // which are the rebuilds least able to afford the copies.
                    bucket = new List<Vector3>(16);
                    cells[key] = bucket;
                }
                bucket.Add(position);
                lastKey = key;
                lastBucket = bucket;
            }

            // Slots past the live count describe pieces that have since been destroyed, and each
            // one holds their GameObject and Transform wrappers alive for nothing. Same reason the
            // buckets hold positions instead of Pieces.
            for (int i = count; i < knownCount; i++) {
                known[i] = default;
            }
            knownCount = count;

            nextRebuildTime = now + SnapshotLifetimeSeconds;
            built = true;

            // Rebuilding in one pass rather than spreading it over frames is deliberate:
            // Piece.OnDestroy swap removes from s_allPieces, so a partial walk of a list being
            // reordered underneath it can count a piece twice or miss one entirely.
            //
            // Steering which frame that one pass lands on - holding it back off frames that are
            // already running long, so it does not stack onto a zone streaming in - was tried and
            // is not worth it. A rebuild is one frame in ten seconds, so even with a tenth of the
            // wall clock inside a streaming burst only about a tenth of rebuilds coincide with one,
            // and deferring buys back well under half of those while pushing the worst snapshot age
            // from eight seconds to eleven. Making the pass itself cheaper, which is what the
            // per piece caching above does, is the part that pays.
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
