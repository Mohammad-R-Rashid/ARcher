#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ithappy.Adventure_Land
{
    [CustomEditor(typeof(ObstacleCreateManager))]
    public class ObstacleCreateManagerEditor : UnityEditor.Editor
    {
        private ObstacleCreateManager _manager;
        private Vector3 _currentMousePosition;
        private int _controlID;

        private void OnEnable()
        {
            _manager = (ObstacleCreateManager)target;
            SceneView.duringSceneGui += OnSceneGUI;
            _controlID = GUIUtility.GetControlID(FocusType.Passive);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_manager.IsCreatingPath()) return;

            Event e = Event.current;
            UpdateMousePosition(e);

            DrawVisualGuides();
            HandlePointPlacement(e);
        }

        private void UpdateMousePosition(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                _currentMousePosition = hit.point;
            }
        }

        private void DrawVisualGuides()
        {
            if (_manager.PointsPlaced() == 0)
            {
                Handles.color = Color.green;
                Handles.SphereHandleCap(0, _currentMousePosition, Quaternion.identity, 0.3f, EventType.Repaint);
                Handles.Label(_currentMousePosition + Vector3.up * 0.5f, "Start Point");
            }
            else
            {
                Handles.color = Color.green;
                Handles.SphereHandleCap(0, _manager.FirstPoint(), Quaternion.identity, 0.3f, EventType.Repaint);
                Handles.Label(_manager.FirstPoint() + Vector3.up * 0.5f, "Start");
                
                Handles.color = Color.red;
                Handles.SphereHandleCap(0, _currentMousePosition, Quaternion.identity, 0.3f, EventType.Repaint);
                Handles.Label(_currentMousePosition + Vector3.up * 0.5f, "End Point");
                
                Handles.color = Color.blue;
                Handles.DrawDottedLine(_manager.FirstPoint(), _currentMousePosition, 5f);
            }
        }

        private void HandlePointPlacement(Event e)
        {
            switch (e.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(_controlID);
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt && !e.control && !e.shift:
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        GUIUtility.hotControl = _controlID;
                        e.Use();
                        
                        _manager.AddPoint(hit.point);
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseUp:
                    GUIUtility.hotControl = 0;
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    _manager.CancelCreation();
                    e.Use();
                    SceneView.RepaintAll();
                    break;
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (!_manager.IsCreatingPath())
            {
                if (GUILayout.Button("Create New Obstacle Path"))
                {
                    _manager.StartPathCreation();
                    SceneView.RepaintAll();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    _manager.PointsPlaced() == 0
                        ? "Click to place Start Point"
                        : "Click to place End Point",
                    MessageType.Info);

                if (GUILayout.Button("Cancel"))
                {
                    _manager.CancelCreation();
                    SceneView.RepaintAll();
                }
            }
        }
    }
}
#endif