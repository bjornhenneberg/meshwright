using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Spatial;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// A quadric edge-collapse <see cref="Reducer"/> that will not introduce a defect the mesh did not
/// already have.
///
/// <para>
/// The vendored reducer's own tests are all local: a collapse is refused if it flips a face normal
/// in the edge's one-ring or violates the link condition. Neither can see two sheets of surface
/// being pushed through each other, or a vertex landing exactly on top of a distant one, because
/// neither is a property of the one-ring. So on a clean mesh with thin walls — the Menger sponge
/// sample is the standard case — decimation produced self-intersections, degenerate slivers,
/// duplicate vertex locations and the position-level non-manifold edges those imply, while the
/// panel reported that further collapses "would have created invalid geometry". It had declined
/// exactly the collapses it could see, and made the rest.
/// </para>
///
/// <para>
/// This class supplies the missing half through <see cref="Reducer.CollapseIsGloballyValid"/>:
/// before each collapse, the triangles the collapse reshapes are evaluated in their hypothetical
/// post-collapse form against the rest of the mesh, and a collapse that would add a defect is
/// refused. Each test is asked of the detector that would report the defect
/// (<see cref="DegenerateTriangleDetector.AreaEpsilonFor"/>,
/// <see cref="DuplicateVertexDetector.IsCoincident"/>, <see cref="SelfIntersectionSearch"/>), so the
/// guard and the diagnostics cannot drift apart — the failure mode of the hole-fill bug fixed the
/// day before this one.
/// </para>
///
/// <para>
/// Only <em>new</em> defects are refused. A collapse in a region that already self-intersects, or
/// one that reshapes a triangle that was already degenerate, is allowed: the job is to preserve the
/// validity the mesh had, not to demand validity it never had. Without that, an already-broken scan
/// — the common case for the users this tool exists for — would barely decimate at all.
/// </para>
/// </summary>
public sealed class ValidatingReducer : Reducer
{
    public ValidatingReducer(DMesh3 mesh) : base(mesh)
    {
    }

    /// <summary>How many collapses this guard refused that the reducer's local tests had approved.</summary>
    public int RefusedCollapses { get; private set; }

    // Broadphase over the whole mesh. Rebuilding it per collapse would dominate the run — a
    // 140k-triangle reduction makes tens of thousands of them — so it is built rarely and kept
    // honest a different way: a collapse only moves the vertex it keeps, so every triangle that is
    // *not* in _dirty still has exactly the geometry the tree was built over, and its stored box is
    // exact. The triangles that have moved are held in _dirty and tested directly instead, and the
    // tree is rebuilt once _dirty grows large enough that scanning it stops being cheap.
    private StaleTolerantTree? _tree;
    private readonly HashSet<int> _dirty = new();
    private readonly Dictionary<(int X, int Y, int Z), HashSet<int>> _dirtyCells = new();
    private readonly HashSet<int> _dirtySprawling = new();
    private double _dirtyCellSize = 1.0;
    private int _rebuildAtDirtyCount = 1;
    private double _degenerateAreaEpsilon;

    private readonly HashSet<int> _affectedIds = new();
    private readonly List<int> _affectedOrder = new();
    private readonly List<Index3i> _affectedIndices = new();
    private readonly List<Triangle3d> _affectedAfter = new();
    private readonly List<int> _candidates = new();

    // One reusable traversal, since a per-query closure allocation is measurable at this call rate.
    private AxisAlignedBox3f _queryBox;
    private DMeshAABBTree3.TreeTraversal? _traversal;

    // Reused rather than constructed per test: the guard runs millions of exact triangle-triangle
    // tests on a large mesh, and the allocation dominated them. Assigning either triangle resets the
    // instance's cached result, so a reused instance answers each pair from scratch.
    private readonly IntrTriangle3Triangle3 _exactAfter = new(new Triangle3d(), new Triangle3d());
    private readonly IntrTriangle3Triangle3 _exactBefore = new(new Triangle3d(), new Triangle3d());

    protected override bool CollapseIsGloballyValid(int keepVid, int removeVid, ref Vector3d newPos, int t0, int t1)
    {
        EnsureTree();
        GatherAffected(keepVid, removeVid, newPos, t0, t1);

        if (WouldDegenerate() || WouldSelfIntersectOrDuplicate(keepVid, removeVid, newPos))
        {
            RefusedCollapses++;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Records the triangles whose geometry the collapse just changed. Only the kept vertex moves,
    /// and every triangle the collapse rewrote is in its one-ring afterwards, so that ring is the
    /// whole of this collapse's contribution to <see cref="_dirty"/>.
    /// </summary>
    protected override void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo collapseInfo)
    {
        base.OnEdgeCollapse(edgeID, va, vb, collapseInfo);

        foreach (int tid in mesh.VtxTrianglesItr(va))
        {
            _dirty.Add(tid);
            IndexDirty(tid);
        }
    }

    private void EnsureTree()
    {
        if (_tree is not null && _dirty.Count < _rebuildAtDirtyCount)
        {
            return;
        }

        _tree = new StaleTolerantTree(mesh);
        _dirty.Clear();
        _dirtyCells.Clear();
        _dirtySprawling.Clear();

        // Coarse enough that a one-ring's worth of query box spans a couple of cells, fine enough
        // that a cell holds few triangles. Nothing depends on the choice for correctness — every
        // candidate a cell yields has its real bounds tested afterwards.
        _dirtyCellSize = Math.Max(mesh.CachedBounds.MaxDim / 64.0, MathUtil.Epsilon);

        // Every guard call scans the dirty set, and every rebuild costs a tree build, so the
        // threshold trades one against the other. A few hundred triangles is small enough to scan
        // with a bounds test per entry and large enough that a reduction pays for a bounded number
        // of rebuilds rather than one every few collapses.
        _rebuildAtDirtyCount = Math.Clamp(mesh.TriangleCount / 50, 256, 4096);

        // The degenerate threshold scales with the mesh's average edge length, which grows as
        // decimation proceeds, so it is re-read here rather than computed once up front.
        _degenerateAreaEpsilon = DegenerateTriangleDetector.AreaEpsilonFor(mesh);
    }

    /// <summary>
    /// The triangles that survive the collapse but change shape: everything around either endpoint
    /// except the one or two the collapse removes, each rewritten into the form it will have
    /// afterwards (<paramref name="removeVid"/> rewritten to <paramref name="keepVid"/>, which sits
    /// at <paramref name="newPos"/>).
    /// </summary>
    private void GatherAffected(int keepVid, int removeVid, Vector3d newPos, int t0, int t1)
    {
        _affectedIds.Clear();
        _affectedOrder.Clear();
        _affectedIndices.Clear();
        _affectedAfter.Clear();

        Collect(keepVid);
        Collect(removeVid);

        void Collect(int vid)
        {
            foreach (int tid in mesh.VtxTrianglesItr(vid))
            {
                if (tid == t0 || tid == t1 || !_affectedIds.Add(tid))
                {
                    continue;
                }

                Index3i tri = mesh.GetTriangle(tid);
                var after = new Index3i(
                    tri.a == removeVid ? keepVid : tri.a,
                    tri.b == removeVid ? keepVid : tri.b,
                    tri.c == removeVid ? keepVid : tri.c);

                _affectedOrder.Add(tid);
                _affectedIndices.Add(after);
                _affectedAfter.Add(new Triangle3d(
                    PositionAfter(after.a, keepVid, newPos),
                    PositionAfter(after.b, keepVid, newPos),
                    PositionAfter(after.c, keepVid, newPos)));
            }
        }
    }

    private Vector3d PositionAfter(int vid, int keepVid, Vector3d newPos) =>
        vid == keepVid ? newPos : mesh.GetVertex(vid);

    private bool WouldDegenerate()
    {
        for (int i = 0; i < _affectedAfter.Count; i++)
        {
            Triangle3d after = _affectedAfter[i];
            if (DegenerateTriangleDetector.AreaOf(after.V0, after.V1, after.V2) >= _degenerateAreaEpsilon)
            {
                continue;
            }

            // Already degenerate before the collapse: refusing would cost reduction without
            // removing a defect.
            Triangle3d before = CurrentTriangle(_affectedOrder[i]);
            if (DegenerateTriangleDetector.AreaOf(before.V0, before.V1, before.V2) >= _degenerateAreaEpsilon)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the collapse would either push a reshaped triangle through a triangle it does not
    /// already run through, or land the kept vertex on top of another vertex.
    ///
    /// <para>
    /// Both questions are asked of one broadphase query. Every reshaped triangle has a corner at
    /// <paramref name="newPos"/>, so the tiny box the duplicate-vertex test needs sits inside the
    /// union of their bounds, and a second descent for it would double the cost of the guard for
    /// nothing.
    /// </para>
    /// </summary>
    private bool WouldSelfIntersectOrDuplicate(int keepVid, int removeVid, Vector3d newPos)
    {
        if (_affectedAfter.Count == 0)
        {
            return false;
        }

        AxisAlignedBox3d union = Bounds(_affectedAfter[0]);
        for (int i = 1; i < _affectedAfter.Count; i++)
        {
            union.Contain(Bounds(_affectedAfter[i]));
        }

        CollectCandidates(union);

        foreach (int tid in _candidates)
        {
            Index3i tri = mesh.GetTriangle(tid);
            for (int corner = 0; corner < 3; corner++)
            {
                int vid = tri[corner];
                if (vid != keepVid && vid != removeVid
                    && DuplicateVertexDetector.IsCoincident(mesh.GetVertex(vid), newPos))
                {
                    return true;
                }
            }
        }

        for (int i = 0; i < _affectedAfter.Count; i++)
        {
            Triangle3d after = _affectedAfter[i];
            if (SelfIntersectionSearch.IsExcludedAsDegenerate(after.V0, after.V1, after.V2))
            {
                // The detector never reports a pair involving a zero-area triangle, so neither does
                // this — refusing over one would cost reduction to prevent nothing.
                continue;
            }

            AxisAlignedBox3d afterBounds = Bounds(after);
            Index3i indicesAfter = _affectedIndices[i];

            foreach (int tid in _candidates)
            {
                if (_affectedIds.Contains(tid))
                {
                    continue;
                }

                Index3i other = mesh.GetTriangle(tid);
                if (SelfIntersectionSearch.SharesVertex(indicesAfter, other))
                {
                    continue;
                }

                Triangle3d otherTri = CurrentTriangle(tid);
                if (!afterBounds.Intersects(Bounds(otherTri))
                    || SelfIntersectionSearch.IsExcludedAsDegenerate(otherTri.V0, otherTri.V1, otherTri.V2))
                {
                    continue;
                }

                _exactAfter.Triangle0 = after;
                _exactAfter.Triangle1 = otherTri;
                if (!_exactAfter.Find())
                {
                    continue;
                }

                // The pair intersects afterwards. That is a new defect only if the triangle did not
                // already run through this one in its pre-collapse form.
                Triangle3d before = CurrentTriangle(_affectedOrder[i]);
                _exactBefore.Triangle0 = before;
                _exactBefore.Triangle1 = otherTri;
                if (SelfIntersectionSearch.IsExcludedAsDegenerate(before.V0, before.V1, before.V2)
                    || !_exactBefore.Find())
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Triangle3d CurrentTriangle(int tid)
    {
        var tri = new Triangle3d();
        mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
        return tri;
    }

    private static AxisAlignedBox3d Bounds(Triangle3d tri)
    {
        var box = new AxisAlignedBox3d(tri.V0);
        box.Contain(tri.V1);
        box.Contain(tri.V2);
        return box;
    }

    /// <summary>
    /// Every live triangle whose bounds overlap <paramref name="query"/>. Triangles untouched since
    /// the tree was built come from the tree, whose stored boxes are still exact for them; triangles
    /// the reduction has already moved come from <see cref="_dirty"/>, scanned directly.
    /// </summary>
    private void CollectCandidates(AxisAlignedBox3d query)
    {
        _candidates.Clear();
        _queryBox = (AxisAlignedBox3f)query;

        _traversal ??= new DMeshAABBTree3.TreeTraversal
        {
            NextBoxF = (box, depth) => box.Intersects(_queryBox),
            NextTriangleF = tid =>
            {
                if (!_dirty.Contains(tid) && mesh.IsTriangle(tid))
                {
                    _candidates.Add(tid);
                }
            },
        };

        _tree!.DoTraversal(_traversal);

        CellRange(query, out (int X, int Y, int Z) min, out (int X, int Y, int Z) max);
        if (CellCount(min, max) > CellBudget)
        {
            // Same guard from the query side: a wide box spans more cells than the dirty set has
            // members, so walking the set directly is strictly cheaper.
            AddDirtyOverlapping(_dirty, query);
        }
        else
        {
            for (int x = min.X; x <= max.X; x++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        if (_dirtyCells.TryGetValue((x, y, z), out HashSet<int>? cell))
                        {
                            AddDirtyOverlapping(cell, query);
                        }
                    }
                }
            }

            AddDirtyOverlapping(_dirtySprawling, query);
        }

        // A dirty triangle spanning several cells can be collected more than once. Sorting and
        // de-duplicating a handful of ids is cheaper than a per-query hash set.
        DeduplicateCandidates();
    }

    private void DeduplicateCandidates()
    {
        if (_candidates.Count < 2)
        {
            return;
        }

        _candidates.Sort();
        int write = 1;
        for (int read = 1; read < _candidates.Count; read++)
        {
            if (_candidates[read] != _candidates[write - 1])
            {
                _candidates[write++] = _candidates[read];
            }
        }

        _candidates.RemoveRange(write, _candidates.Count - write);
    }

    /// <summary>
    /// Files a moved triangle into every grid cell its current bounds touch. Re-filed on every
    /// collapse that reshapes it, so a triangle is always present at its latest position; the
    /// entries it leaves behind at older positions only ever make a query over-inclusive, and the
    /// grid is cleared whenever the tree is rebuilt.
    /// </summary>
    private void IndexDirty(int tid)
    {
        CellRange(mesh.GetTriBounds(tid), out (int X, int Y, int Z) min, out (int X, int Y, int Z) max);
        if (CellCount(min, max) > CellBudget)
        {
            // A collapse to a quadric-optimal point can leave a triangle stretched across a large
            // part of the model. Filing one of those cell by cell costs more than every query it
            // would ever accelerate, so it goes in a small always-scanned set instead.
            _dirtySprawling.Add(tid);
            return;
        }

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var key = (x, y, z);
                    if (!_dirtyCells.TryGetValue(key, out HashSet<int>? cell))
                    {
                        cell = new HashSet<int>();
                        _dirtyCells[key] = cell;
                    }

                    cell.Add(tid);
                }
            }
        }
    }

    private void AddDirtyOverlapping(IEnumerable<int> ids, AxisAlignedBox3d query)
    {
        foreach (int tid in ids)
        {
            if (mesh.IsTriangle(tid) && mesh.GetTriBounds(tid).Intersects(query))
            {
                _candidates.Add(tid);
            }
        }
    }

    /// <summary>
    /// How many grid cells are worth touching before a linear scan of the dirty set wins. The set is
    /// capped in the low thousands, so a few hundred cell probes is already the wrong trade.
    /// </summary>
    private const long CellBudget = 256;

    private static long CellCount((int X, int Y, int Z) min, (int X, int Y, int Z) max) =>
        (long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);

    private void CellRange(AxisAlignedBox3d box, out (int X, int Y, int Z) min, out (int X, int Y, int Z) max)
    {
        min = (Cell(box.Min.x), Cell(box.Min.y), Cell(box.Min.z));
        max = (Cell(box.Max.x), Cell(box.Max.y), Cell(box.Max.z));
    }

    private int Cell(double value) => (int)Math.Floor(value / _dirtyCellSize);

    /// <summary>
    /// A broadphase tree that tolerates being queried after the mesh has changed. The stock tree
    /// refuses outright, reasonably — some of its stored boxes are then wrong — but rebuilding per
    /// collapse is exactly what this scheme exists to avoid. What makes the stale tree safe here is
    /// that <see cref="CollectCandidates"/> takes the moved triangles from <see cref="_dirty"/>
    /// instead, and re-reads every candidate's geometry from the live mesh before testing it, so
    /// nothing downstream trusts a stored box.
    /// </summary>
    private sealed class StaleTolerantTree : DMeshAABBTree3
    {
        internal StaleTolerantTree(DMesh3 mesh) : base(mesh, autoBuild: true)
        {
        }

        public override void DoTraversal(TreeTraversal traversal) => tree_traversal(root_index, 0, traversal);
    }
}
