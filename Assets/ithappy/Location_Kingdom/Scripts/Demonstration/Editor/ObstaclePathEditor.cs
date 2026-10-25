using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

namespace ithappy.Adventure_Land
{
    [CustomEditor(typeof(ObstaclePath))]
    public class ObstaclePathEditor : UnityEditor.Editor
    {
        private bool isPlacingObstacle = false;
        private int pointsPlaced = 0;
        private Transform startPoint;
        private Transform targetPoint;
        private GameObject currentObstacle;
        private Vector3 currentMousePosition;

        private GUIStyle infoStyle;
        private Color startPointColor = new Color(0.2f, 0.8f, 0.2f, 0.5f);
        private Color targetPointColor = new Color(0.8f, 0.2f, 0.2f, 0.5f);
        private Color lineColor = new Color(0.5f, 0.5f, 1f, 0.8f);

        private void OnEnable()
        {
            infoStyle = new GUIStyle();
            infoStyle.normal.textColor = Color.white;
            infoStyle.fontSize = 15;
            infoStyle.fontStyle = FontStyle.Bold;
            infoStyle.alignment = TextAnchor.UpperCenter;
            infoStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.5f));
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            ObstaclePath obstaclePath = (ObstaclePath)target;

            if (!isPlacingObstacle)
            {
                if (GUILayout.Button("Create New Jump Obstacle", GUILayout.Height(30)))
                {
                    StartPlacingObstacle(obstaclePath);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Placing Jump Obstacle:\n1. Click to place Start Point\n2. Click to place Target Point",
                    MessageType.Info);

                GUILayout.Space(10);
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(100, 20), pointsPlaced / 2f,
                    $"Points placed: {pointsPlaced}/2");
                GUILayout.Space(10);

                if (GUILayout.Button("Cancel", GUILayout.Height(25)))
                {
                    CancelPlacing();
                }
            }
        }

        private void StartPlacingObstacle(ObstaclePath obstaclePath)
        {
            isPlacingObstacle = true;
            pointsPlaced = 0;
            startPoint = null;
            targetPoint = null;

            currentObstacle = new GameObject("JumpObstacle (Placing)");
            currentObstacle.transform.SetParent(obstaclePath.transform);
            Undo.RegisterCreatedObjectUndo(currentObstacle, "Create Jump Obstacle");

            var jumpObstacle = currentObstacle.AddComponent<JumpObstacle>();
            jumpObstacle.JumpObstacleInfo = new JumpObstacleInfo();
            jumpObstacle.JumpObstacleInfo.JumpPoint = new JumpPointElement();

            SceneView.duringSceneGui += OnSceneGUI;
            SceneView.RepaintAll();
        }

        private void CancelPlacing()
        {
            isPlacingObstacle = false;
            SceneView.duringSceneGui -= OnSceneGUI;

            if (currentObstacle != null)
            {
                Undo.DestroyObjectImmediate(currentObstacle);
            }

            SceneView.RepaintAll();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            UpdateMousePosition();
            DrawVisualGuides();
            HandlePointPlacement();
        }

        private void UpdateMousePosition()
        {
            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                currentMousePosition = hit.point;
            }
        }

        private void DrawVisualGuides()
        {
            if (pointsPlaced == 0)
            {
                Handles.color = startPointColor;
                Handles.SphereHandleCap(0, currentMousePosition, Quaternion.identity, 0.5f, EventType.Repaint);
                Handles.Label(currentMousePosition + Vector3.up * 0.7f, "Start Point", infoStyle);
            }
            else if (pointsPlaced == 1)
            {
                Handles.color = startPointColor;
                Handles.SphereHandleCap(0, startPoint.position, Quaternion.identity, 0.5f, EventType.Repaint);
                Handles.Label(startPoint.position + Vector3.up * 0.7f, "Start", infoStyle);
                
                Handles.color = targetPointColor;
                Handles.SphereHandleCap(0, currentMousePosition, Quaternion.identity, 0.5f, EventType.Repaint);
                Handles.Label(currentMousePosition + Vector3.up * 0.7f, "Target Point", infoStyle);
                
                Handles.color = lineColor;
                Handles.DrawDottedLine(startPoint.position, currentMousePosition, 5f);
                
                Vector3 direction = (currentMousePosition - startPoint.position).normalized;
                Handles.ArrowHandleCap(0, startPoint.position + direction, Quaternion.LookRotation(direction), 2f,
                    EventType.Repaint);
            }
        }

        private void HandlePointPlacement()
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.control && !e.shift)
            {
                if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out RaycastHit hit))
                {
                    e.Use();
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);

                    if (pointsPlaced == 0)
                    {
                        startPoint = CreatePoint("StartPoint", hit.point);
                        pointsPlaced++;
                    }
                    else if (pointsPlaced == 1)
                    {
                        targetPoint = CreatePoint("TargetPoint", hit.point);
                        pointsPlaced++;
                        FinalizeObstacle();
                    }

                    SceneView.RepaintAll();
                }
            }
    
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelPlacing();
                e.Use();
            }
        }

        private Transform CreatePoint(string name, Vector3 position)
        {
            GameObject point = new GameObject(name);
            point.transform.position = position;
            point.transform.SetParent(currentObstacle.transform);
            Undo.RegisterCreatedObjectUndo(point, "Create Obstacle Point");
            
            var sphere = point.AddComponent<SphereCollider>();
            sphere.radius = 0.2f;

            return point.transform;
        }

        private void FinalizeObstacle()
        {
            var jumpObstacle = currentObstacle.GetComponent<JumpObstacle>();
            jumpObstacle.JumpObstacleInfo.JumpPoint.StartPoint = startPoint;
            jumpObstacle.JumpObstacleInfo.JumpPoint.TargetPoint = targetPoint;
            
            float distance = Vector3.Distance(startPoint.position, targetPoint.position);
            jumpObstacle.JumpObstacleInfo.JumpPoint.JumpHeight = Mathf.Clamp(distance * 0.5f, 1f, 5f);

            currentObstacle.name = $"JumpObstacle ({startPoint.position} -> {targetPoint.position})";

            isPlacingObstacle = false;
            SceneView.duringSceneGui -= OnSceneGUI;

            Selection.activeGameObject = currentObstacle;
            EditorUtility.SetDirty(currentObstacle);

            SceneView.RepaintAll();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }
    }
}
#endif
