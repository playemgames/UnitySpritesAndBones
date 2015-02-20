﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics.Common.PolygonManipulation;
using FarseerPhysics.Common;
using UnityEditor;
using System.IO;

[ExecuteInEditMode]
public class MeshCreator : EditorWindow {
    //private MeshEditScene sceneWindow;
    private SpriteRenderer spriteRenderer;

    private float baseSelectDistance = 0.1f;
    private float changedBaseSelectDistance = 0.1f;

    #region Gizmo Colors
    private Color ghostSegmentColor = Color.cyan;
    private Color nearSegmentColor = Color.red;
    private Color definedSegmentColor = Color.green;
    private Color vertexColor = Color.green;
    private Color holeColor = Color.red;
    #endregion

    private List<Vertex> verts = new List<Vertex>();
    private List<Segment> segments = new List<Segment>();
    private List<Vector2> holes = new List<Vector2>();

    private bool segmentDefiningDrag = false;
    private int dragStartIndex = -1;

    private float simplify = 1f;
    private string meshName = "GeneratedMesh";
    private bool previewMode = false;
    private bool hideGizmos = false;
    private Material previewMaterial = new Material(Shader.Find("Unlit/Transparent"));

    private Mesh generatedMesh = null;
    private Vector3[] meshVertices = null;
    public bool meshDirty = true;

    private GameObject previewObject = null;
    private MeshFilter previewMF = null;

    private GUIContent[] subdivideContents = new GUIContent[] { new GUIContent("2"), new GUIContent("3"), new GUIContent("4"), new GUIContent("6") };
    private int[] subdivideContentValues = new int[] { 2, 3, 4, 6 };
    private int subdivideValue = 2;

    [MenuItem("Sprites And Bones/Mesh Creator")]
    protected static void ShowSkinMeshEditor() {
        var wnd = GetWindow<MeshCreator>();
        wnd.title = "Mesh Creator";

        if(Selection.activeGameObject != null) {
            GameObject o = Selection.activeGameObject;
            wnd.spriteRenderer = o.GetComponent<SpriteRenderer>();
            wnd.meshName = o.name;
        }

        //SceneView.lastActiveSceneView.FrameSelected();
        wnd.Show();
        wnd.wantsMouseMove = true;
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
            if(Reset())
                LoadPolygonFromSprite();
        }

        EditorGUILayout.Separator();

        if(GUILayout.Button("Reset Points")) {
            Reset();
        }

        EditorGUILayout.Separator();

        subdivideValue = EditorGUILayout.IntPopup(subdivideValue, subdivideContents, subdivideContentValues); 
        if(GUILayout.Button("Subdivide Mesh")) {
            Subdivide(subdivideValue);
        }

        EditorGUILayout.Separator();

        GUI.enabled = true;
        #endregion

        #region Custom mesh creation
        changedBaseSelectDistance = EditorGUILayout.Slider("Handle Size", baseSelectDistance, 0, 1);
        if(baseSelectDistance != changedBaseSelectDistance) {
            baseSelectDistance = changedBaseSelectDistance;
            EditorUtility.SetDirty(this);
            SceneView.currentDrawingSceneView.Repaint();
        }

        EditorGUILayout.Separator();

        GUILayout.Label("[Ctrl + Click] to Add Point", EditorStyles.whiteLabel);
        GUILayout.Label("[Shift + Click] to Remove Point or Edge", EditorStyles.whiteLabel);
        GUILayout.Label("[Right Click + Drag] to Add Edge", EditorStyles.whiteLabel);
        GUILayout.Label("[Alt + Click] to Mark / Demark Area as Hole", EditorStyles.whiteLabel);

        #endregion

        #region Preview Mode Button
        GUI.enabled = true;

        if(hideGizmos != EditorGUILayout.Toggle("Hide gizmos", hideGizmos)) {
            hideGizmos = !hideGizmos;
            EditorUtility.SetDirty(this);
            SceneView.currentDrawingSceneView.Repaint();
        }

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
        EditorGUILayout.Separator();
        #endregion

        #region Save mesh button
        meshName = EditorGUILayout.TextField("Mesh Name", meshName);

        if(GUILayout.Button("Save Mesh")) {
            previewMode = false;
            Mesh mesh = GetMesh();

            mesh.name = string.IsNullOrEmpty(meshName) ? spriteRenderer.name : meshName;
            AssetUtility.CreateOrReplaceAssetAtPath(mesh, "Meshes/" + mesh.name);
        }
        #endregion
    }

    public void OnDestroy() {
        //if(sceneWindow != null) sceneWindow.Close();
        SceneView.onSceneGUIDelegate -= OnSceneGUI;
        DestroyPreviewObject();
    }

    public void OnSceneGUI(SceneView sceneView) {
        Event e = Event.current;
        Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Vector2 mousePos = r.origin; //- spriteRenderer.transform.position;

        if(previewMode) {
            if(!hideGizmos && e.type != EventType.MouseMove) PreviewMode();
            return;
        }

        if(e.type == EventType.MouseDown) {
            meshDirty = true;
            EditorUtility.SetDirty(this);
            //sceneView.Repaint();

            #region Hole operations
            if(e.alt && e.type == EventType.MouseDown) {
                AddOrRemoveHole(mousePos);
                e.Use();
            }
            #endregion

            #region Vertex operations
            else if(e.shift && e.type == EventType.MouseDown) {
                RemoveVertexOrSegment(mousePos);
                e.Use();
            }
            #endregion

            #region Adding vertices
            // Adding a point if control is pressed
            else if(e.control && e.type == EventType.MouseDown) {
                var newVert = new Vertex(mousePos);
                verts.Add(newVert);
                e.Use();

                // Remove old segment and add 2 new segments if new point is added near a segment
                var seg = GetSegmentNearPosition(mousePos);
                if(seg != null) {
                    segments.RemoveAt(seg.index);
                    seg.deleted = true;
                    AddSegment(newVert, seg.first);
                    AddSegment(newVert, seg.second);
                }
            }
            #endregion

            #region Segment Defining
            else if(!segmentDefiningDrag && e.button == 1 && e.type == EventType.MouseDown) {
                segmentDefiningDrag = true;
                var dragStart = GetVertexNearPosition(mousePos);
                dragStartIndex = dragStart == null ? -1 : dragStart.index;
                if(dragStartIndex >= 0) e.Use();        // To prevent scene drag with right mouse drag
            }
            #endregion
        }
        else if(e.type == EventType.MouseUp) {
            if(segmentDefiningDrag && dragStartIndex >= 0) {
                var endVert = GetVertexNearPosition(mousePos);
                if(endVert != null && endVert != verts[dragStartIndex]) {
                    AddSegment(endVert, verts[dragStartIndex]);
                }
            }
            segmentDefiningDrag = false;
        }
        else if(e.type == EventType.MouseMove || e.type == EventType.MouseDrag) {
            sceneView.Repaint();
        }

        if(!hideGizmos) VisualizePolygon(sceneView);
    }

    void VisualizePolygon(SceneView sceneView) {
        Event e = Event.current;
        Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
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

        #region Draw defined segments
        Handles.color = definedSegmentColor;
        foreach(var seg in segments) {
            if(!seg.IsDeleted())
                Handles.DrawLine(seg.first.position, seg.second.position);
        }
        #endregion

        #region Draw Nearest Segment
        Handles.color = nearSegmentColor;
        var nearSeg = GetSegmentNearPosition(mousePos);
        if(nearSeg != null) Handles.DrawLine(nearSeg.first.position, nearSeg.second.position);
        #endregion

        #region Draw currently defining edge
        if(segmentDefiningDrag && dragStartIndex >= 0) {
            Handles.color = ghostSegmentColor;
            var endVert = GetVertexNearPosition(mousePos);
            if(endVert != null) Handles.DrawLine(verts[dragStartIndex].position, endVert.position);
            else Handles.DrawLine(verts[dragStartIndex].position, mousePos);
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

        #region Draw vertex handles
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
        #endregion

        generatedMesh.vertices = meshVertices.Select(x => spriteRenderer.transform.InverseTransformPoint(x)).ToArray();
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

    private Mesh TriangulateMesh() {
        var tnMesh = new TriangleNet.Mesh();
        var input = new TriangleNet.Geometry.InputGeometry();

        var localVertices = verts.Select(v => spriteRenderer.transform.InverseTransformPoint(v.position)).ToArray();

        for(int i = 0; i < verts.Count; i++) {
            verts[i].index = i;
            input.AddPoint(verts[i].position.x, verts[i].position.y);
        }

        foreach(var seg in segments) {
            if(!seg.IsDeleted())
                input.AddSegment(seg.first.index, seg.second.index);
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

    private Vector2[] genUV(Vector3[] vertices) {
        if(spriteRenderer != null) {
            var prevRot = spriteRenderer.transform.rotation;
            spriteRenderer.transform.rotation = Quaternion.identity;

            float texHeight = (float)(spriteRenderer.sprite.texture.height);
            float texWidth = (float)(spriteRenderer.sprite.texture.width);

            Vector3 botLeft = spriteRenderer.transform.InverseTransformPoint(new Vector3(spriteRenderer.bounds.min.x, spriteRenderer.bounds.min.y, 0));

            Vector2 spriteTextureOrigin;
            spriteTextureOrigin.x = (float)spriteRenderer.sprite.rect.x;
            spriteTextureOrigin.y = (float)spriteRenderer.sprite.rect.y;

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

    private void GeneratePreviewObject() {
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
        mr.sharedMaterial = previewMaterial;
        mr.sharedMaterial.mainTexture = spriteRenderer.sprite.texture;

        meshVertices = previewMF.sharedMesh.vertices.Select(x => spriteRenderer.transform.TransformPoint(x)).ToArray();
    }

    private void DestroyPreviewObject() {
        if(spriteRenderer != null) {
            Selection.activeGameObject = spriteRenderer.gameObject;
            spriteRenderer.enabled = true;
        }
        if(previewObject != null)
            GameObject.DestroyImmediate(previewObject);
        previewObject = null;
    }

    private void LoadMesh(Mesh loadMesh) {
        verts = loadMesh.vertices.Select(x => new Vertex(spriteRenderer.transform.TransformPoint(x))).ToList();

        /*
        // TODO: Only distinct edges should be added, otherwise behavior is unknown
        for(int i = 0; i < loadMesh.triangles.Length; i += 3) {
            AddSegment(verts[loadMesh.triangles[i + 1]], verts[loadMesh.triangles[i]]);
            AddSegment(verts[loadMesh.triangles[i + 2]], verts[loadMesh.triangles[i + 1]]);
            AddSegment(verts[loadMesh.triangles[i]], verts[loadMesh.triangles[i + 2]]);
        }*/

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
        verts = polygon.Select(v => new Vertex(spriteRenderer.transform.TransformPoint(v))).ToList();

        segments = new List<Segment>();
        AddSegment(verts[verts.Count - 1], verts[0]);
        for(int i = 1; i < verts.Count; i++) {
            AddSegment(verts[i - 1], verts[i]);
        }
    }

    private static Rect GetBounds(IEnumerable<Vector2> poly) {
        float bx1 = poly.Min(p => p.x);
        float by1 = poly.Min(p => p.y);
        float bx2 = poly.Max(p => p.x);
        float by2 = poly.Max(p => p.y);

        return new Rect(bx1, by1, bx2 - bx1, by2 - by1);
    }

    private Vertex GetVertexNearPosition(Vector2 position) {
        float selectDistance = HandleUtility.GetHandleSize(position) * baseSelectDistance;
        float minSelectDistance = selectDistance * selectDistance;
        int minIndex = -1;
        float minValue = float.MaxValue;

        float distance = 0;
        for(int i = 0; i < verts.Count; i++) {
            distance = (position - verts[i].position).sqrMagnitude;
            if(distance < minValue) {
                minValue = distance;
                minIndex = i;
            }
        }

        if(minValue > minSelectDistance) return null;
        if(minIndex < 0) return null;

        verts[minIndex].index = minIndex;
        return verts[minIndex];
    }

    private Segment GetSegmentNearPosition(Vector2 position) {
        float selectDistance = HandleUtility.GetHandleSize(position) * baseSelectDistance;
        int minIndex = -1;
        float minValue = float.MaxValue;

        float distance = 0;
        for(int i = 0; i < segments.Count; i++) {
            distance = HandleUtility.DistancePointToLineSegment(position, segments[i].first.position, segments[i].second.position);

            if(distance < minValue) {
                minValue = distance;
                minIndex = i;
            }
        }

        if(minValue > selectDistance) return null;
        if(minIndex < 0) return null;

        segments[minIndex].index = minIndex;
        return segments[minIndex];
    }

    private void AddOrRemoveHole(Vector2 position) {
        float selectDistance = HandleUtility.GetHandleSize(position) * baseSelectDistance;
        selectDistance = selectDistance * selectDistance;

        for(int i = 0; i < holes.Count; i++) {
            if((position - holes[i]).sqrMagnitude < selectDistance) {
                holes.RemoveAt(i);
                return;
            }
        }

        holes.Add(position);
        return;
    }

    private void AddSegment(Vertex first, Vertex second) {
        if(!segments.Any(s => (s.first == first || s.second == first) && (s.second == second || s.first == second) && !s.IsDeleted()))
            segments.Add(new Segment(first, second));
    }

    private void RemoveVertexOrSegment(Vector2 position) {
        var seg = GetSegmentNearPosition(position);
        if(seg != null) {
            RemoveSegment(seg.index);
            return;
        }

        var vert = GetVertexNearPosition(position);
        if(vert != null) RemoveVertex(vert.index);
    }

    private void RemoveSegment(int index) {
        segments[index].deleted = true;
        segments.RemoveAt(index);
    }

    private void RemoveVertex(int index) {
        verts[index].deleted = true;
        verts.RemoveAt(index);
    }

    private void Subdivide(int subdivideValue) {
        Mesh mesh = GetMesh();
        MeshHelper.Subdivide(mesh, subdivideValue);
        LoadMesh(mesh);
    }

    private bool Reset() {
        if(verts == null || verts.Count == 0 || EditorUtility.DisplayDialog("Reset points", "Are you sure? This operation will reset points.", "Yes", "Cancel")) {
            verts = new List<Vertex>();
            segments = new List<Segment>();
            holes = new List<Vector2>();

            meshDirty = true;
            generatedMesh = null;
            EditorUtility.SetDirty(this);
            SceneView.currentDrawingSceneView.Repaint();

            return true;
        }
        return false;
    }

    public class Segment {
        public Vertex first;
        public Vertex second;
        public bool deleted = false;
        public int index = -1;

        public Segment(Vertex fst, Vertex snd) {
            first = fst;
            second = snd;
        }

        public bool IsDeleted() {
            return first.deleted || second.deleted || deleted;
        }
    }

    public class Vertex {
        public bool deleted = false;
        public Vector2 position;
        public int index = -1;
        public Vertex(Vector2 pos) { position = pos; }
    }
}