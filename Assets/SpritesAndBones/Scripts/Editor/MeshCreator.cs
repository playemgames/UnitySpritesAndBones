using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics.Common.PolygonManipulation;
using FarseerPhysics.Common;

[ExecuteInEditMode]
public class MeshCreator : EditorWindow {
    private SpriteRenderer spriteRenderer;

    private const float baseSelectDistance = 0.3f;   // Square distance actually

    private Color ghostSegmentColor = Color.blue;
    private Color nearSegmentColor = Color.cyan;
    private Color definedSegmentColor = Color.green;
    private Color vertexColor = Color.green;
    private Color selectedVertexColor = Color.blue;
    private Color holeColor = Color.red;

    private List<VertexIndex> verts = new List<VertexIndex>();
    private List<Vector2> holes = new List<Vector2>();

    private int selectedVertex = -1;


    [MenuItem("Sprites And Bones/Mesh Creator")]
    protected static void ShowSkinMeshEditor() {
        var wnd = GetWindow<MeshCreator>();
        wnd.title = "Mesh Creator";
        wnd.Show();

        SceneView.onSceneGUIDelegate += wnd.OnSceneGUI;
    }

    public void OnGUI() {
        GUILayout.Label("Sprite", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        spriteRenderer = (SpriteRenderer)EditorGUILayout.ObjectField(spriteRenderer, typeof(SpriteRenderer), true);
        if(Selection.activeGameObject != null) {
            GameObject o = Selection.activeGameObject;
            spriteRenderer = o.GetComponent<SpriteRenderer>();
        }

        if(spriteRenderer != null) {
            if(GUILayout.Button("Save Mesh")) {
                Mesh mesh = TriangulateMesh();

                DirectoryInfo meshDir = new DirectoryInfo("Assets/Meshes");
                if(Directory.Exists(meshDir.FullName) == false) {
                    Directory.CreateDirectory(meshDir.FullName);
                }
                ScriptableObjectUtility.CreateAsset(mesh, "Meshes/" + spriteRenderer.gameObject.name + ".Mesh");
            }
        }
    }


    public void OnDestroy() {
        SceneView.onSceneGUIDelegate -= OnSceneGUI;
    }

    public void OnSceneGUI(SceneView sceneView) {
        Event e = Event.current;
        Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Vector2 mousePos = r.origin; //- spriteRenderer.transform.position;
        float selectDistance = HandleUtility.GetHandleSize(mousePos) * baseSelectDistance;

        VisualizePolygon(sceneView);

        if(e.type == EventType.MouseDown && (e.button == 0 || e.button == 1)) {
            EditorUtility.SetDirty(this);

            // Hole operations are done when alt key is pressed
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

            // Vertex operations have precedence
            for(int i = 0; i < verts.Count; i++) {
                if(Vector2.Distance(mousePos, verts[i].position) < selectDistance) {

                    if(e.shift) {
                        verts[i].deleted = true;
                        verts.RemoveAt(i);
                        selectedVertex = -1;
                        return;
                    }
                    else if(!(e.control || e.alt)) {
                        selectedVertex = i;
                        return;
                    }
                }
            }

            // If a vertex is selected do segment operations
            if(selectedVertex >= 0) {
                for(int i = 0; i < verts.Count; i++) {
                    if(i == selectedVertex) continue;
                    if(HandleUtility.DistancePointToLineSegment(mousePos, verts[i].position, verts[selectedVertex].position) < selectDistance) {
                        if(e.shift) {
                            // Lazy deletion
                            verts[selectedVertex].segments.RemoveAll(x => x.first == verts[i] || x.second == verts[i]);
                            verts[i].segments.RemoveAll(x => x.first == verts[selectedVertex] || x.second == verts[selectedVertex]);
                            return;
                        }
                        else if(e.control) {
                            var seg = new Segment(verts[i], verts[selectedVertex]);
                            verts[selectedVertex].segments.Add(seg);
                            return;
                        }
                    }
                }
            }

            // Adding a point if control is pressed
            if(e.control) {
                verts.Add(new VertexIndex(mousePos));
                selectedVertex = verts.Count - 1;
                return;
            }
        }

        if(e.type == EventType.MouseDown && (e.button == 1))

            sceneView.Repaint();
    }

    void VisualizePolygon(SceneView sceneView) {
        Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Vector2 mousePos = r.origin;

        float selectDistance = HandleUtility.GetHandleSize(mousePos) * baseSelectDistance;


        //EditorGUI.BeginChangeCheck();
        Handles.color = vertexColor;
        for(int i = 0; i < verts.Count; i++) {
            // Draw vertex handles
            verts[i].position = Handles.FreeMoveHandle(
                        verts[i].position,
                        Quaternion.identity,
                        selectDistance,
                        Vector3.zero,
                        Handles.CircleCap
                    );
        }


        //if(EditorGUI.EndChangeCheck()) sceneView.Repaint();

        foreach(var hole in holes) {
            Handles.RectangleCap(0, hole, Quaternion.identity, selectDistance);
        }

        // Draw ghost segments
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

        // Draw defined segments - Make sure they appear above ghost segments
        Handles.color = definedSegmentColor;
        foreach(var vertex in verts) {
            foreach(var seg in vertex.segments) {
                if(!seg.IsDeleted())
                    Handles.DrawLine(seg.first.position, seg.second.position);
            }
        }
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

        Mesh mesh = new Mesh();
        mesh.vertices = localVertices;
        mesh.triangles = tnMesh.Triangles.ToUnityMeshTriangleIndices();
        mesh.uv = genUV(mesh.vertices);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }


    public Vector2[] genUV(Vector3[] vertices) {
        if(spriteRenderer != null) {
            // Get the sprite's texture dimensions as float values
            float texHeight = (float)(spriteRenderer.sprite.texture.height);
            // Debug.Log(texHeight);
            float texWidth = (float)(spriteRenderer.sprite.texture.width);
            // Debug.Log(texWidth);

            // Get the bottom left position of the sprite renderer bounds in local space
            Vector3 botLeft = spriteRenderer.transform.InverseTransformPoint(new Vector3(spriteRenderer.bounds.min.x, spriteRenderer.bounds.min.y, 0));

            // Get the sprite's texture origin from the sprite's rect as float values
            Vector2 spriteTextureOrigin;
            spriteTextureOrigin.x = (float)spriteRenderer.sprite.rect.x;
            spriteTextureOrigin.y = (float)spriteRenderer.sprite.rect.y;

            // Calculate Pixels to Units using the current sprite rect width and the sprite bounds
            float pixelsToUnits = spriteRenderer.sprite.rect.width / spriteRenderer.sprite.bounds.size.x;

            Vector2[] uv = new Vector2[vertices.Length];
            for(int i = 0; i < vertices.Length; i++) {
                // Apply the bottom left and lower left offset values to the vertices before applying the pixels to units 
                // to get the pixel value
                float x = (vertices[i].x - botLeft.x) * spriteRenderer.sprite.pixelsPerUnit;
                float y = (vertices[i].y - botLeft.y) * spriteRenderer.sprite.pixelsPerUnit;

                // Add the sprite's origin on the texture to the vertices and divide by the dimensions to get the UV
                uv[i] = new Vector2(((x + spriteTextureOrigin.x) / texWidth), ((y + spriteTextureOrigin.y) / texHeight));
            }
            return uv;
        }
        else {
            return null;
        }
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