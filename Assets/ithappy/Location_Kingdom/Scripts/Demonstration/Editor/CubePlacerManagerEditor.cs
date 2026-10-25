#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ithappy
{
    [CustomEditor(typeof(CubePlacerManager))]
    public class CubePlacerManagerEditor : UnityEditor.Editor
    {
        private CubePlacerManager _manager;
        private Vector3 _currentMousePosition;
        private int _controlID;
        private bool _isMouseOnSurface = false;

        private void OnEnable()
        {
            _manager = (CubePlacerManager)target;
            SceneView.duringSceneGui += OnSceneGUI;
            _controlID = GUIUtility.GetControlID(FocusType.Passive);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_manager.IsPlacing()) return;

            Event e = Event.current;
            UpdateMousePosition(e);

            DrawVisualGuides();
            HandlePointPlacement(e);
        }

        private void UpdateMousePosition(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            RaycastHit hit;
            _isMouseOnSurface = Physics.Raycast(ray, out hit, Mathf.Infinity, _manager.PlacementLayer);
            
            if (_isMouseOnSurface)
            {
                _currentMousePosition = hit.point;
            }
            else
            {
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                float distance;
                if (groundPlane.Raycast(ray, out distance))
                {
                    _currentMousePosition = ray.GetPoint(distance);
                }
            }
        }

        private void DrawVisualGuides()
        {
            if (!_isMouseOnSurface)
            {
                Handles.color = Color.yellow;
                Handles.Label(_currentMousePosition, "No surface found!");
                return;
            }

            if (_manager.PointsPlaced() == 0)
            {
                Handles.color = Color.green;
                Handles.SphereHandleCap(0, _currentMousePosition, Quaternion.identity, 0.3f, EventType.Repaint);
                Handles.Label(_currentMousePosition + Vector3.up * 0.5f, "First Point (Click)");
            }
            else
            {
                Handles.color = Color.green;
                Handles.SphereHandleCap(0, _manager.FirstPoint(), Quaternion.identity, 0.3f, EventType.Repaint);
                Handles.Label(_manager.FirstPoint() + Vector3.up * 0.5f, "First Point");
                
                Handles.color = Color.red;
                Handles.SphereHandleCap(0, _currentMousePosition, Quaternion.identity, 0.3f, EventType.Repaint);
                Handles.Label(_currentMousePosition + Vector3.up * 0.5f, "Second Point (Click)");
                
                Handles.color = Color.blue;
                Handles.DrawDottedLine(_manager.FirstPoint(), _currentMousePosition, 5f);
                
                float distance = Vector3.Distance(_manager.FirstPoint(), _currentMousePosition);
                Vector3 midPoint = (_manager.FirstPoint() + _currentMousePosition) / 2f;
                Handles.Label(midPoint + Vector3.up * 0.3f, $"Distance: {distance:F2}");
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
                    if (_isMouseOnSurface)
                    {
                        GUIUtility.hotControl = _controlID;
                        e.Use();
                        
                        _manager.AddPoint(_currentMousePosition);
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseUp:
                    GUIUtility.hotControl = 0;
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    _manager.CancelPlacement();
                    e.Use();
                    SceneView.RepaintAll();
                    break;
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            if (!_manager.IsPlacing())
            {
                if (GUILayout.Button("Start Placing Cube"))
                {
                    _manager.StartCubePlacement();
                    SceneView.RepaintAll();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    _manager.PointsPlaced() == 0
                        ? "Click on surface to place First Point"
                        : "Click on surface to place Second Point",
                    MessageType.Info);

                if (GUILayout.Button("Cancel Placement"))
                {
                    _manager.CancelPlacement();
                    SceneView.RepaintAll();
                }
            }
        }
    }
}
#endif