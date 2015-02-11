using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics.Common.PolygonManipulation;
using FarseerPhysics.Common;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

[ExecuteInEditMode]
public class MeshCreator : EditorWindow {
    private SpriteRenderer spriteRenderer;

    private float baseSelectDistance = 0.1f;

    private Color ghostSegmentColor = Color.blue;
    private Color nearSegmentColor = Color.red;
    private Color definedSegmentColor = Color.green;
    private Color vertexColor = Color.green;
    private Color selectedVertexColor = Color.blue;
    private Color holeColor = Color.red;

    private List<VertexIndex> verts = new List<VertexIndex>();
    private List<Vector2> holes = new List<Vector2>();

    private int selectedVertex = -1;

    private float simplify = 1f;
    private string meshName = "GeneratedMesh";
    private bool previewMode = false;

    private Mesh generatedMesh = null;
    private Vector3[] meshVertices = null;
    private bool meshDirty = true;

    private GameObject previewObject = null;
    private MeshFilter previewMF = null;

    [MenuItem("Sprites And Bones/Mesh Creator")]
    protected static void ShowSkinMeshEditor() {
        var wnd = GetWindow<MeshCreator>();
        wnd.title = "Mesh Creator";


        if(Selection.activeGameObject != null) {
            GameObject o = Selection.activeGameObject;
            wnd.spriteRenderer = o.GetComponent<SpriteRenderer>();
            wnd.meshName = o.name;
        }

        wnd.Show();

        SceneView.onSceneGUIDelegate += wnd.OnSceneGUI;
    }

    public void OnGUI() {
        GUILayout.Label("Sprite", EditorStyles.boldLabel);

        spriteRenderer = (SpriteRenderer)EditorGUILayout.ObjectField(spriteRenderer, typeof(SpriteRenderer), true);


        if(spriteRenderer == null) return;

        #region Auto mesh creation buttons
        GUI.enabled = !previewMode;

        simplify = EditorGUILayout.FloatField("Vertex Dist.", simplify);

        if(GUILayout.Button("Generate Polygon from Texture")) {
            LoadPolygonFromSprite();

            EditorUtility.SetDirty(this);
            SceneView.currentDrawingSceneView.Repaint();
        }

        EditorGUILayout.Separator();

        // TODO: Add mesh creation from polygon collider 2d?

        GUI.enabled = true;
        #endregion

        #region Custom mesh creation
        baseSelectDistance = EditorGUILayout.FloatField("Handle Size", baseSelectDistance);

        EditorGUILayout.Separator();

        GUILayout.Label("Ctrl/Shift + Click to Add/Remove Point, Ctrl/Shift + Click to Add/Remove Edge, Alt + Click to Add/Remove Holes", EditorStyles.whiteLabel);

        #endregion

        #region Preview Mode Button
        GUI.enabled = true;
        GUI.color = (previewMode) ? Color.green : Color.white;
        if(GUILayout.Button("Preview Mode")) {
            previewMode = !previewMode;
            if(previewMode) {
                GeneratePreviewObject();
            }
            else {
                DestroyPreviewObject();
            }

            EditorUtility.SetDirty(this);
        }
        GUI.color = Color.white;
        #endregion

        #region Save mesh button
        meshName = EditorGUILayout.TextField("Mesh Name", meshName);

        if(GUILayout.Button("Save Mesh")) {
            previewMode = false;
            Mesh mesh = GetMesh();

            DirectoryInfo meshDir = new DirectoryInfo("Assets/Meshes");
            if(Directory.Exists(meshDir.FullName) == false) {
                Directory.CreateDirectory(meshDir.FullName);
            }
            ScriptableObjectUtility.CreateAsset(mesh, "Meshes/" + meshName + ".Mesh");
        }
        #endregion
    }

    public void OnDestroy() {
        SceneView.onSceneGUIDelegate -= OnSceneGUI;
        DestroyPreviewObject();
    }

    public void OnSceneGUI(SceneView sceneView) {
        if(previewMode) {
            PreviewMode();
            return;
        }
        Event e = Event.current;
        Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Vector2 mousePos = r.origin; //- spriteRenderer.transform.position;
        float selectDistance = HandleUtility.GetHandleSize(mousePos) * baseSelectDistance;

        VisualizePolygon(sceneView);

        if(e.type == EventType.MouseDown && (e.button == 0 || e.button == 1)) {
            meshDirty = true;
            EditorUtility.SetDirty(this);

            #region Hole operations
            if(e.alt) {
                // If near to any hole, remove that hole
                for(int i = 0; i < holes.Count; i++) {
                    if(Vector2.Distance(mousePos, holes[i]) < selectDistance) {
                        holes.RemoveAt(i);
                        return;
                    }
                }
                // Else add hole at mouse position
                holes.Add(mousePos);
                return;
            }
            #endregion

            #region Vertex operations
            float minSelectDistance = Mathf.Sqrt(selectDistance);
            int minIndex = -1;
            float minValue = float.MaxValue;
            float distance = 0;

            // Find the vertex with minimum distance from mouse
            for(int i = 0; i < verts.Count; i++) {
                if(i == selectedVertex) continue;
                distance = Vector2.Distance(mousePos, verts[i].position);
                if(distance < minValue) {
                    minValue = distance;
                    minIndex = i;
                }
            }

            if(minIndex >= 0 && minValue < minSelectDistance) {
                if(e.shift) {
                    verts[minIndex].deleted = true;
                    verts.RemoveAt(minIndex);
                    selectedVertex = -1;
                    return;
                }
                else if(!(e.control || e.alt)) {
                    selectedVertex = minIndex;
                    return;
                }
            }
            #endregion

            #region Segment operations
            if(selectedVertex >= 0) {
                minSelectDistance = Mathf.Sqrt(selectDistance);
                minIndex = -1;
                minValue = float.MaxValue;
                distance = 0;

                // Find the segment with minimum distance from mouse
                for(int i = 0; i < verts.Count; i++) {
                    if(i == selectedVertex) continue;
                    distance = HandleUtility.DistancePointToLineSegment(mousePos, verts[i].position, verts[selectedVertex].position);
                    if(distance < minValue) {
                        minValue = distance;
                        minIndex = i;
                    }
                }

                if(minIndex >= 0 && minValue < minSelectDistance) {
                    if(e.shift) {
                        // Lazy deletion
                        verts[selectedVertex].segments.RemoveAll(x => x.first == verts[minIndex] || x.second == verts[minIndex]);
                        verts[minIndex].segments.RemoveAll(x => x.first == verts[selectedVertex] || x.second == verts[selectedVertex]);
                        return;
                    }
                    else if(e.control) {
                        var seg = new Segment(verts[minIndex], verts[selectedVertex]);
                        verts[selectedVertex].segments.Add(seg);
                        return;
                    }
                }
            }
            #endregion

            // Adding a point if control is pressed
            if(e.control) {
                verts.Add(new VertexIndex(mousePos));
                selectedVertex = verts.Count - 1;
                return;
            }
            // If nothing is done, deselect the vertex
            else {
                selectedVertex = -1;
            }
        }
    }

    void VisualizePolygon(SceneView sceneView) {
        Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Vector2 mousePos = r.origin;

        float selectDistance = HandleUtility.GetHandleSize(mousePos) * baseSelectDistance;

        #region Draw vertex handles
        Handles.color = vertexColor;
        for(int i = 0; i < verts.Count; i++) {
            verts[i].position = Handles.FreeMoveHandle(
                        verts[i].position,
                        Quaternion.identity,
                        selectDistance,
                        Vector3.zero,
                        Handles.CircleCap
                    );
        }
        #endregion

        #region Draw holes
        Handles.color = holeColor;
        foreach(var hole in holes)
            Handles.RectangleCap(0, hole, Quaternion.identity, selectDistance);
        #endregion

        #region Draw ghost segments
        if(selectedVertex >= 0) {
            bool foundClosestSegment = false;
            for(int i = 0; i < verts.Count; i++) {
                if(i == selectedVertex) continue;
                if(!foundClosestSegment && HandleUtility.DistancePointToLineSegment(mousePos, verts[i].position, verts[selectedVertex].position) < selectDistance) {
                    Handles.color = nearSegmentColor;
                    Handles.DrawLine(verts[i].position, verts[selectedVertex].position);
                    foundClosestSegment = true;
                }
                else {
                    Handles.color = ghostSegmentColor;
                    Handles.DrawLine(verts[i].position, verts[selectedVertex].position);
                }
            }
        }
        #endregion

        #region Draw defined segments
        Handles.color = definedSegmentColor;
        foreach(var vertex in verts) {
            foreach(var seg in vertex.segments) {
                if(!seg.IsDeleted())
                    Handles.DrawLine(seg.first.position, seg.second.position);
            }
        }
        #endregion
    }

    void PreviewMode() {
        if(generatedMesh == null) {
            Debug.Log("Mesh was not generated");
            previewMode = false;
            DestroyPreviewObject();
            return;
        }
        Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Vector2 mousePos = r.origin;

        float selectDistance = HandleUtility.GetHandleSize(mousePos) * baseSelectDistance;

        Handles.color = vertexColor;
        for(int i = 0; i < meshVertices.Length; i++) {
            meshVertices[i] = Handles.FreeMoveHandle(
                        meshVertices[i],
                        Quaternion.identity,
                        selectDistance,
                        Vector3.zero,
                        Handles.CircleCap
                    );
            generatedMesh.vertices[i] = spriteRenderer.transform.InverseTransformPoint(meshVertices[i]);
        }

        generatedMesh.vertices = meshVertices.Select(x => spriteRenderer.transform.InverseTransformPoint(x)).ToArray();
        generatedMesh.RecalculateBounds();
        previewMF.sharedMesh = generatedMesh;

        meshDirty = true;

        SceneView.currentDrawingSceneView.Repaint();
        EditorUtility.SetDirty(previewObject);
    }

    public Mesh GetMesh() {
        if(meshDirty || generatedMesh == null) {
            meshDirty = false;
            return generatedMesh = TriangulateMesh();
        }
        else return generatedMesh;
    }

    public Mesh TriangulateMesh() {
        var tnMesh = new TriangleNet.Mesh();
        var input = new TriangleNet.Geometry.InputGeometry();

        var localVertices = verts.Select(v => spriteRenderer.transform.InverseTransformPoint(v.position)).ToArray();

        for(int i = 0; i < verts.Count; i++) {
            verts[i].index = i;
            input.AddPoint(verts[i].position.x, verts[i].position.y);
        }

        foreach(var vertex in verts) {
            foreach(var seg in vertex.segments) {
                if(!seg.IsDeleted())
                    input.AddSegment(seg.first.index, seg.second.index);
            }
        }

        foreach(var hole in holes) {
            input.AddHole(hole.x, hole.y);
        }

        tnMesh.Triangulate(input);

        try {
            Mesh mesh = new Mesh();
            mesh.vertices = localVertices;
            mesh.triangles = tnMesh.Triangles.ToUnityMeshTriangleIndices();
            mesh.uv = genUV(mesh.vertices);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
        catch {
            Debug.LogError("Mesh topology was wrong. Make sure you dont have intersecting edges.");
            throw;
        }
    }

    public Vector2[] genUV(Vector3[] vertices) {
        if(spriteRenderer != null) {
            var prevRot = spriteRenderer.transform.rotation;

            float texHeight = (float)(spriteRenderer.sprite.texture.height);
            float texWidth = (float)(spriteRenderer.sprite.texture.width);

            Vector3 botLeft = spriteRenderer.transform.InverseTransformPoint(new Vector3(spriteRenderer.bounds.min.x, spriteRenderer.bounds.min.y, 0));

            Vector2 spriteTextureOrigin;
            spriteTextureOrigin.x = (float)spriteRenderer.sprite.rect.x;
            spriteTextureOrigin.y = (float)spriteRenderer.sprite.rect.y;

            float pixelsToUnits = spriteRenderer.sprite.rect.width / spriteRenderer.sprite.bounds.size.x;

            Vector2[] uv = new Vector2[vertices.Length];
            for(int i = 0; i < vertices.Length; i++) {
                float x = (vertices[i].x - botLeft.x) * spriteRenderer.sprite.pixelsPerUnit;
                float y = (vertices[i].y - botLeft.y) * spriteRenderer.sprite.pixelsPerUnit;

                uv[i] = new Vector2(((x + spriteTextureOrigin.x) / texWidth), ((y + spriteTextureOrigin.y) / texHeight));
            }

            spriteRenderer.transform.rotation = prevRot;
            return uv;
        }
        else {
            return null;
        }
    }

    public void GeneratePreviewObject() {
        DestroyPreviewObject();
        spriteRenderer.enabled = false;

        previewObject = new GameObject();
        Selection.activeGameObject = previewObject;
        previewMF = previewObject.AddComponent<MeshFilter>();
        var mr = previewObject.AddComponent<MeshRenderer>();
        previewObject.transform.position = spriteRenderer.transform.position;
        previewObject.transform.rotation = spriteRenderer.transform.rotation;
        previewObject.transform.localScale = spriteRenderer.transform.localScale;

        previewMF.mesh = GetMesh();
        mr.material = new Material(Shader.Find("Unlit/Transparent"));
        mr.sharedMaterial.mainTexture = spriteRenderer.sprite.texture;

        meshVertices = previewMF.sharedMesh.vertices.Select(x => spriteRenderer.transform.TransformPoint(x)).ToArray();
    }

    public void DestroyPreviewObject() {
        Selection.activeGameObject = spriteRenderer.gameObject;
        spriteRenderer.enabled = true;
        if(previewObject != null) GameObject.DestroyImmediate(previewObject);
        previewObject = null;
    }

    private void LoadMesh(Mesh loadMesh) {
        verts = loadMesh.vertices.Select(x => new VertexIndex(spriteRenderer.transform.TransformPoint(x))).ToList();

        // TODO: Only distinct edges should be added, otherwise behavior is unknown
        for(int i = 0; i < loadMesh.triangles.Length; i += 3) {
            verts[loadMesh.triangles[i]].segments.Add(new Segment(verts[loadMesh.triangles[i + 1]], verts[loadMesh.triangles[i]]));
            verts[loadMesh.triangles[i + 1]].segments.Add(new Segment(verts[loadMesh.triangles[i + 2]], verts[loadMesh.triangles[i + 1]]));
            verts[loadMesh.triangles[i + 2]].segments.Add(new Segment(verts[loadMesh.triangles[i]], verts[loadMesh.triangles[i + 2]]));
        }

        holes = new List<Vector2>();

        EditorUtility.SetDirty(this);
    }

    private void LoadPolygonFromSprite() {
        Rect r = spriteRenderer.sprite.rect;
        Texture2D tex = spriteRenderer.sprite.texture;
        IBitmap bmp = ArrayBitmap.CreateFromTexture(tex, new Rect(r.x, r.y, r.width, r.height));
        var polygon = BitmapHelper.CreateFromBitmap(bmp);
        polygon = SimplifyTools.DouglasPeuckerSimplify(new Vertices(polygon), simplify).ToArray();

        Rect bounds = GetBounds(polygon);

        float scalex = spriteRenderer.sprite.bounds.size.x / bounds.width;
        float scaley = spriteRenderer.sprite.bounds.size.y / bounds.height;

        polygon = polygon.Select(v => new Vector2(v.x * scalex, v.y * scaley) - (bounds.center * scalex) + (Vector2)spriteRenderer.sprite.bounds.center).ToArray();
        verts = polygon.Select(v => new VertexIndex(spriteRenderer.transform.TransformPoint(v))).ToList();

        verts[0].segments.Add(new Segment(verts[verts.Count - 1], verts[0]));
        for(int i=1; i < verts.Count; i++) {
            verts[i].segments.Add(new Segment(verts[i - 1], verts[i]));
        }
    }

    private static Rect GetBounds(IEnumerable<Vector2> poly) {
        float bx1 = poly.Min(p => p.x);
        float by1 = poly.Min(p => p.y);
        float bx2 = poly.Max(p => p.x);
        float by2 = poly.Max(p => p.y);

        return new Rect(bx1, by1, bx2 - bx1, by2 - by1);
    }

    public class Segment {
        public VertexIndex first;
        public VertexIndex second;
        public bool deleted = false;

        public Segment(VertexIndex fst, VertexIndex snd) {
            first = fst;
            second = snd;
        }

        public bool IsDeleted() {
            return first.deleted || second.deleted || deleted;
        }
    }

    public class VertexIndex {
        public bool deleted = false;
        public Vector2 position;
        public int index = -1;
        public VertexIndex(Vector2 pos) { position = pos; }
        public List<Segment> segments = new List<Segment>();
    }
}