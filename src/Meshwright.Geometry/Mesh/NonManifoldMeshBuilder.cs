using g3;

namespace Meshwright.Geometry.Mesh;

/// <summary>
/// Builds a <see cref="DMesh3"/> from a triangle soup without losing triangles that the data
/// structure cannot hold directly.
///
/// <para>
/// <see cref="DMesh3"/> is an indexed, edge-based mesh: an edge belongs to at most two triangles,
/// so <see cref="DMesh3.AppendTriangle(int,int,int,int)"/> refuses a third and returns
/// <see cref="DMesh3.NonManifoldID"/>. It also refuses a triangle whose corners are not three
/// distinct vertex ids. Real 3D-printing files contain both constantly, and simply skipping the
/// refused triangles loses geometry — on the M4-1 corpus that meant two files losing about 73% of
/// their mesh, after which every detector was describing something the user had not opened.
/// </para>
///
/// <para>
/// The fix is the standard one: <b>split the mesh at the offending vertices instead of dropping the
/// triangle</b>. Duplicating a vertex gives the triangle a fresh edge to attach to, so it lands at
/// exactly the right position while the topology stays legal. The geometry is complete; only the
/// connectivity is cut, which is an honest description of a non-manifold junction — there is no
/// single consistent surface there to be connected to.
/// </para>
///
/// <para>
/// This is the representation <c>NonManifoldDetector</c> already looks for: it groups edges by
/// vertex <em>position</em> precisely to find "several distinct edge ids that share the same pair
/// of vertex positions". Until now nothing produced that shape, so the detector could only ever
/// report non-manifold edges the importer had already thrown away. Degenerate triangles survive the
/// same way and are reported by <c>DegenerateTriangleDetector</c>.
/// </para>
/// </summary>
public sealed class NonManifoldMeshBuilder
{
    private readonly DMesh3 _mesh = new();
    private readonly Dictionary<Vector3d, int> _vertexIds = new();

    /// <summary>The mesh built so far.</summary>
    public DMesh3 Mesh => _mesh;

    /// <summary>Triangles appended in total.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>
    /// Triangles that needed a vertex duplicated because they would otherwise have given an edge a
    /// third face — that is, the triangles sitting on a non-manifold junction.
    /// </summary>
    public int NonManifoldTrianglesSplit { get; private set; }

    /// <summary>
    /// Triangles whose corners collapsed onto fewer than three distinct vertices after welding, and
    /// were kept as zero-area triangles by re-separating them. They are genuinely degenerate
    /// geometry present in the file, not an artefact of loading.
    /// </summary>
    public int DegenerateTrianglesSplit { get; private set; }

    /// <summary>
    /// Triangles that could not be added at all. Expected to stay zero — splitting always yields a
    /// legal triangle — but counted rather than assumed, so a future edge case surfaces as a number
    /// instead of as silently missing geometry.
    /// </summary>
    public int TrianglesDropped { get; private set; }

    /// <summary>
    /// Interns a vertex position, returning the shared id for coincident positions. This is the
    /// welding STL needs to recover an indexed mesh from triangle soup; callers that must not weld
    /// (OBJ, which carries its own indexing) should use <see cref="AddVertexUnwelded"/>.
    /// </summary>
    public int AddVertexWelded(Vector3d position)
    {
        if (_vertexIds.TryGetValue(position, out int existing))
        {
            return existing;
        }

        int id = _mesh.AppendVertex(position);
        _vertexIds.Add(position, id);
        return id;
    }

    /// <summary>Appends a vertex without interning it, preserving the file's own indexing.</summary>
    public int AddVertexUnwelded(Vector3d position) => _mesh.AppendVertex(position);

    /// <summary>
    /// Appends a triangle, duplicating whichever of its vertices are needed for it to fit. Returns
    /// true if the triangle is now in the mesh.
    /// </summary>
    public bool AddTriangle(int a, int b, int c)
    {
        TriangleCount++;

        // A triangle whose corners have welded together has no three distinct ids to index. Give it
        // fresh copies so the (zero-area) triangle survives to be reported as degenerate.
        if (a == b || b == c || a == c)
        {
            DegenerateTrianglesSplit++;
            return AppendSeparated(a, b, c);
        }

        int appended = _mesh.AppendTriangle(a, b, c);
        if (appended >= 0)
        {
            return true;
        }

        if (appended != DMesh3.NonManifoldID)
        {
            TrianglesDropped++;
            return false;
        }

        NonManifoldTrianglesSplit++;

        // Duplicate one endpoint of every edge that is already full. A brand-new vertex has no
        // edges, so each blocked edge is replaced by one that cannot exist yet, and the append
        // below is guaranteed a legal position for the triangle.
        int na = a, nb = b, nc = c;
        if (IsBlocked(a, b))
        {
            na = Duplicate(a);
        }

        if (IsBlocked(b, c))
        {
            nb = Duplicate(b);
        }

        if (IsBlocked(c, a))
        {
            nc = Duplicate(c);
        }

        if (na != a || nb != b || nc != c)
        {
            int retry = _mesh.AppendTriangle(na, nb, nc);
            if (retry >= 0)
            {
                return true;
            }
        }

        // Belt and braces: three fresh vertices share no edge with anything, so this always fits.
        return AppendSeparated(a, b, c);
    }

    /// <summary>True when the two vertices already have an edge carrying its full two triangles.</summary>
    private bool IsBlocked(int u, int v)
    {
        int edge = _mesh.FindEdge(u, v);
        return edge != DMesh3.InvalidID && !_mesh.IsBoundaryEdge(edge);
    }

    /// <summary>A new vertex at the same position, carrying no edges.</summary>
    private int Duplicate(int vertexId) => _mesh.AppendVertex(_mesh.GetVertex(vertexId));

    /// <summary>Appends the triangle on three fresh vertices, fully detached from the rest.</summary>
    private bool AppendSeparated(int a, int b, int c)
    {
        int appended = _mesh.AppendTriangle(Duplicate(a), Duplicate(b), Duplicate(c));
        if (appended >= 0)
        {
            return true;
        }

        TrianglesDropped++;
        return false;
    }
}
