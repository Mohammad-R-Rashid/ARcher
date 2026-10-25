#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ithappy
{
    [CustomEditor(typeof(PatrolCharacter))]
    public class PatrolCharacterEditor : UnityEditor.Editor
    {
        private PatrolCharacter _patrolCharacter;
        private bool _isPlacingPoints = false;
        private Vector3 _currentMousePosition;
        private int _controlID;
        private bool _isMouseOnSurface = false;
        private GameObject _patrolParent;
        
        private PatrolWayPoint _currentDraggingWayPoint;
        private Vector3 _dragStartPosition;
        private bool _isDragging = false;
        private float _minDragDistance = 0.5f;

        private void OnEnable()
        {
            _patrolCharacter = (PatrolCharacter)target;
            SceneView.duringSceneGui += OnSceneGUI;
            _controlID = GUIUtility.GetControlID(FocusType.Passive);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_isPlacingPoints) return;

            Event e = Event.current;
            UpdateMousePosition(e);

            DrawVisualGuides();
            HandlePointPlacementAndRotation(e);
        }

        private void UpdateMousePosition(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            RaycastHit hit;
            _isMouseOnSurface = Physics.Raycast(ray, out hit, Mathf.Infinity, 1);
            
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
            
            Handles.color = _isDragging ? Color.red : Color.green;
            Handles.SphereHandleCap(0, _currentMousePosition, Quaternion.identity, 0.3f, EventType.Repaint);
            
            if (_isDragging && _currentDraggingWayPoint != null)
            {
                float dragDistance = Vector3.Distance(_currentDraggingWayPoint.transform.position, _currentMousePosition);
                bool shouldActivateRotation = dragDistance >= _minDragDistance;
                
                Handles.color = shouldActivateRotation ? Color.magenta : Color.red;
                
                Vector3 direction = (_currentMousePosition - _currentDraggingWayPoint.transform.position).normalized;
                direction.y = 0;
                if (direction != Vector3.zero)
                {
                    Vector3 arrowEnd = _currentDraggingWayPoint.transform.position + direction * 2f;
                    Handles.DrawLine(_currentDraggingWayPoint.transform.position, arrowEnd);
                    Handles.ArrowHandleCap(0, arrowEnd, Quaternion.LookRotation(direction), 0.5f, EventType.Repaint);
                    
                    if (!shouldActivateRotation)
                    {
                        Vector3 thresholdPoint = _currentDraggingWayPoint.transform.position + direction * _minDragDistance;
                        Handles.color = Color.yellow;
                        Handles.DrawWireDisc(thresholdPoint, Vector3.up, 0.1f);
                        Handles.Label(thresholdPoint, "Activation Distance");
                    }
                }
                
                string statusText = shouldActivateRotation ? 
                    "DRAG ACTIVE - ShouldRotate = TRUE" : 
                    $"Drag further to activate ({(dragDistance):F2}/{_minDragDistance:F2})";
                
                Handles.Label(_currentMousePosition + Vector3.up * 0.5f, statusText);
            }
            else
            {
                Handles.Label(_currentMousePosition + Vector3.up * 0.5f, "Click to Place Point");
            }
            
            var wayPoints = _patrolCharacter.GetWayPoints();
            if (wayPoints != null && wayPoints.Count > 0)
            {
                for (int i = 0; i < wayPoints.Count; i++)
                {
                    if (wayPoints[i] == null) continue;
                    
                    DrawWayPointDirection(wayPoints[i]);
                    
                    Color pointColor;
                    if (wayPoints[i] == _currentDraggingWayPoint)
                    {
                        pointColor = Color.red;
                    }
                    else if (wayPoints[i].ShouldRotate)
                    {
                        pointColor = Color.magenta;
                    }
                    else
                    {
                        pointColor = Color.yellow;
                    }

                    Handles.color = pointColor;
                    Handles.SphereHandleCap(0, wayPoints[i].transform.position, Quaternion.identity, 0.3f, EventType.Repaint);
                    

                    if (wayPoints[i].ShouldRotate)
                    {
                        Handles.color = Color.magenta;
                        Handles.DrawWireDisc(wayPoints[i].transform.position, Vector3.up, 0.4f);
                    }
                    
                    Handles.Label(wayPoints[i].transform.position + Vector3.up * 0.8f, $"Point {i + 1}");
                    
                    if (i == wayPoints.Count - 1 && !_isDragging)
                    {
                        Handles.color = Color.blue;
                        Handles.DrawDottedLine(wayPoints[i].transform.position, _currentMousePosition, 3f);
                    }
                }
            }
        }

        private void DrawWayPointDirection(PatrolWayPoint wayPoint)
        {
            if (wayPoint.transform.forward != Vector3.forward && wayPoint.transform.forward != Vector3.zero)
            {
                Handles.color = wayPoint.ShouldRotate ? Color.magenta : Color.red;
                Vector3 start = wayPoint.transform.position;
                Vector3 end = start + wayPoint.transform.forward * 1.5f;
                Handles.DrawLine(start, end);
                Handles.ArrowHandleCap(0, end, Quaternion.LookRotation(wayPoint.transform.forward), 0.5f, EventType.Repaint);
            }
        }

        private void HandlePointPlacementAndRotation(Event e)
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
                        
                        if (!_isDragging)
                        {
                            StartPointPlacementOrDrag(_currentMousePosition);
                        }
                    }
                    break;

                case EventType.MouseDrag when e.button == 0 && _isDragging:
                    if (_isDragging && _currentDraggingWayPoint != null)
                    {
                        e.Use();
                        
                        RotateWayPointTowardsCursor(_currentDraggingWayPoint, _currentMousePosition);
                        
                        float dragDistance = Vector3.Distance(_currentDraggingWayPoint.transform.position, _currentMousePosition);
                        if (dragDistance >= _minDragDistance && !_currentDraggingWayPoint.ShouldRotate)
                        {
                            Undo.RecordObject(_currentDraggingWayPoint, "Set ShouldRotate");
                            _currentDraggingWayPoint.ShouldRotate = true;
                            _currentDraggingWayPoint.WaitTime = 3f;
                        }
                        
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseUp when e.button == 0:
                    if (_isDragging)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        
                        FinalizeDrag();
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    if (_isDragging)
                    {
                        CancelDrag();
                    }
                    else
                    {
                        FinishPlacement();
                    }
                    e.Use();
                    SceneView.RepaintAll();
                    break;
            }
        }

        private void StartPointPlacementOrDrag(Vector3 position)
        {
            var wayPoints = _patrolCharacter.GetWayPoints();
            
            PatrolWayPoint clickedWayPoint = GetWayPointAtPosition(position);
            
            if (clickedWayPoint != null)
            {
                _currentDraggingWayPoint = clickedWayPoint;
                _isDragging = true;
                _dragStartPosition = clickedWayPoint.transform.position;
            }
            else
            {
                AddWayPoint(position);
                _isDragging = true;
            }
        }

        private void FinalizeDrag()
        {
            if (_currentDraggingWayPoint != null)
            {
                float finalDragDistance = Vector3.Distance(_currentDraggingWayPoint.transform.position, _currentMousePosition);
                if (finalDragDistance < _minDragDistance && _currentDraggingWayPoint.ShouldRotate)
                {
                    // Revert if distance min
                }
            }
            
            _isDragging = false;
            _currentDraggingWayPoint = null;
        }

        private void CancelDrag()
        {
            if (_currentDraggingWayPoint != null)
            {
            }
            
            _isDragging = false;
            _currentDraggingWayPoint = null;
        }

        private PatrolWayPoint GetWayPointAtPosition(Vector3 position)
        {
            var wayPoints = _patrolCharacter.GetWayPoints();
            if (wayPoints == null) return null;

            float pickSize = HandleUtility.GetHandleSize(position) * 0.3f;
            
            foreach (var wayPoint in wayPoints)
            {
                if (wayPoint == null) continue;
                
                if (Vector3.Distance(wayPoint.transform.position, position) <= pickSize)
                {
                    return wayPoint;
                }
            }
            
            return null;
        }

        private void RotateWayPointTowardsCursor(PatrolWayPoint wayPoint, Vector3 cursorPosition)
        {
            Vector3 direction = (cursorPosition - wayPoint.transform.position).normalized;
            
            direction.y = 0;
            
            if (direction != Vector3.zero)
            {
                Undo.RecordObject(wayPoint.transform, "Rotate WayPoint");
                wayPoint.transform.forward = direction;
            }
        }

        private void AddWayPoint(Vector3 position)
        {
            GameObject waypointObj = new GameObject($"WayPoint_{_patrolCharacter.GetWayPoints().Count + 1}");
            waypointObj.transform.position = position;
            waypointObj.transform.SetParent(_patrolParent.transform);
            PatrolWayPoint wayPoint = waypointObj.AddComponent<PatrolWayPoint>();
            
            _patrolCharacter.GetWayPoints().Add(wayPoint);
            _currentDraggingWayPoint = wayPoint;
            
            EditorUtility.SetDirty(_patrolCharacter);
            EditorUtility.SetDirty(_patrolParent);
        }

        private void StartPlacement()
        {
            _isPlacingPoints = true;
            
            if (_patrolParent == null)
            {
                _patrolParent = new GameObject("Patrol");
                _patrolParent.transform.SetParent(_patrolCharacter.transform.parent);
                _patrolParent.transform.position = _patrolCharacter.transform.position + Vector3.right * 2f;
            }
            
            if (_patrolCharacter.GetWayPoints() == null)
            {
                var wayPointsField = typeof(PatrolCharacter).GetField("_wayPoints", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                wayPointsField.SetValue(_patrolCharacter, new List<PatrolWayPoint>());
            }
        }

        private void FinishPlacement()
        {
            _isPlacingPoints = false;
            _isDragging = false;
            _currentDraggingWayPoint = null;
        }

        private void ClearAllPoints()
        {
            var wayPoints = _patrolCharacter.GetWayPoints();
            if (wayPoints != null)
            {
                if (_patrolParent != null)
                {
                    foreach (Transform child in _patrolParent.transform)
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
                wayPoints.Clear();
                _currentDraggingWayPoint = null;
                _isDragging = false;
                EditorUtility.SetDirty(_patrolCharacter);
                if (_patrolParent != null)
                {
                    EditorUtility.SetDirty(_patrolParent);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Way Points Management", EditorStyles.boldLabel);

            var wayPoints = _patrolCharacter.GetWayPoints();
            int pointsCount = wayPoints != null ? wayPoints.Count : 0;
            EditorGUILayout.LabelField($"Points Count: {pointsCount}");
            
            if (wayPoints != null && pointsCount > 0)
            {
                int rotateCount = 0;
                foreach (var wp in wayPoints)
                {
                    if (wp != null && wp.ShouldRotate) rotateCount++;
                }
                EditorGUILayout.LabelField($"Points with Rotation: {rotateCount}/{pointsCount}");
            }

            EditorGUILayout.Space();
            _minDragDistance = EditorGUILayout.FloatField("Rotation Activation Distance", _minDragDistance);

            if (!_isPlacingPoints)
            {
                if (GUILayout.Button("Start Placing WayPoints"))
                {
                    StartPlacement();
                    SceneView.RepaintAll();
                }

                if (pointsCount > 0)
                {
                    if (GUILayout.Button("Clear All Points"))
                    {
                        if (EditorUtility.DisplayDialog("Clear WayPoints", 
                            "Are you sure you want to clear all way points?", "Yes", "No"))
                        {
                            ClearAllPoints();
                            SceneView.RepaintAll();
                        }
                    }
                    
                    if (GUILayout.Button("Reset All Rotation Flags"))
                    {
                        if (EditorUtility.DisplayDialog("Reset Rotation Flags", 
                            "Are you sure you want to reset all ShouldRotate flags?", "Yes", "No"))
                        {
                            foreach (var wp in wayPoints)
                            {
                                if (wp != null)
                                {
                                    Undo.RecordObject(wp, "Reset ShouldRotate");
                                    wp.ShouldRotate = false;
                                    Debug.Log("Set ShouldRotate to false");
                                }
                            }
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
            else
            {
                string instruction = _isDragging ? 
                    "Drag mouse to rotate point. Drag beyond activation distance to set ShouldRotate flag." : 
                    "Click to place new point. Click and drag existing points to rotate them.";

                EditorGUILayout.HelpBox(
                    instruction + "\n" +
                    "Points with magenta color have ShouldRotate = true\n" +
                    "Press ESC to finish placement.",
                    MessageType.Info);

                if (GUILayout.Button("Finish Placement"))
                {
                    FinishPlacement();
                    SceneView.RepaintAll();
                }
            }
        }
    }
}
#endif