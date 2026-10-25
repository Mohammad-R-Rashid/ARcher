#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ithappy.Adventure_Land
{
    [CustomEditor(typeof(JumpObstacle))]
    public class JumpObstacleEditor : UnityEditor.Editor
    {
        private const float ArrowSize = 1f;
        private const float SphereRadius = 0.3f;
        private const float HandleSize = 0.2f;

        private static readonly Color StartColor = new Color(0.2f, 1f, 0.2f, 0.8f);
        private static readonly Color TargetColor = new Color(1f, 0.2f, 0.2f, 0.8f);
        private static readonly Color CurveColor = new Color(0.4f, 0.6f, 1f, 0.9f);
        private static readonly Color HandleColor = new Color(1f, 0.8f, 0.4f, 0.9f);

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            JumpObstacle obstacle = (JumpObstacle)target;
            if (obstacle.JumpObstacleInfo?.JumpPoint == null) return;

            EditorGUILayout.Space(10);
            GUILayout.Label("Editor Tools", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Swap Start/Target Points"))
            {
                SwapPoints(obstacle.JumpObstacleInfo.JumpPoint);
            }

            if (GUILayout.Button("Snap StartPoint to Ground"))
            {
                SnapPointToGround(obstacle.JumpObstacleInfo.JumpPoint, isStartPoint: true);
            }
            
            if (GUILayout.Button("Snap TargetPoint to Ground"))
            {
                SnapPointToGround(obstacle.JumpObstacleInfo.JumpPoint, isStartPoint: false);
            }
        }
        
        private void OnSceneGUI()
        {
            JumpObstacle obstacle = (JumpObstacle)target;
            if (obstacle.JumpObstacleInfo?.JumpPoint == null) return;

            JumpPointElement jumpPoint = obstacle.JumpObstacleInfo.JumpPoint;

            if (jumpPoint.StartPoint == null || jumpPoint.TargetPoint == null) return;

            DrawJumpCurve(jumpPoint);
            DrawPoints(jumpPoint);
            DrawHandles(jumpPoint);
        }
        
        private void SwapPoints(JumpPointElement jumpPoint)
        {
            Undo.RecordObjects(new Object[] { jumpPoint.StartPoint, jumpPoint.TargetPoint }, "Swap Start/Target Points");
            Transform temp = jumpPoint.StartPoint;
            jumpPoint.StartPoint = jumpPoint.TargetPoint;
            jumpPoint.TargetPoint = temp;
            EditorUtility.SetDirty(target);
        }
        
        private void SnapPointToGround(JumpPointElement jumpPoint, bool isStartPoint)
        {
            Transform point = isStartPoint ? jumpPoint.StartPoint : jumpPoint.TargetPoint;
            if (point == null) return;
            
            Undo.RecordObject(point, "Snap Point to Ground");
    
            if (Physics.Raycast(point.position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f))
            {
                Undo.SetTransformParent(point, hit.transform, "Change Parent");
                point.position = hit.point;
                EditorUtility.SetDirty(point);
            }
            else
            {
                Debug.LogWarning("No ground found below the point!");
            }
        }

        private void DrawJumpCurve(JumpPointElement jumpPoint)
        {
            Vector3 start = jumpPoint.StartPoint.position;
            Vector3 end = jumpPoint.TargetPoint.position;
            Vector3 mid = Vector3.Lerp(start, end, 0.5f);
            mid.y += jumpPoint.JumpHeight;

            Handles.color = CurveColor;
            Handles.DrawBezier(start, end,
                start + (mid - start) * 0.5f,
                end + (mid - end) * 0.5f,
                CurveColor, null, 3f);
            
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Handles.DrawDottedLine(start, new Vector3(end.x, start.y, end.z), 5f);
        }

        private void DrawPoints(JumpPointElement jumpPoint)
        {
            Handles.color = StartColor;
            Handles.SphereHandleCap(0, jumpPoint.StartPoint.position,
                Quaternion.identity,
                SphereRadius, EventType.Repaint);
            
            Handles.color = TargetColor;
            Handles.SphereHandleCap(0, jumpPoint.TargetPoint.position,
                Quaternion.identity,
                SphereRadius, EventType.Repaint);
            
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.5f));

            Handles.Label(jumpPoint.StartPoint.position + Vector3.up * 0.5f,
                "Start", labelStyle);
            Handles.Label(jumpPoint.TargetPoint.position + Vector3.up * 0.5f,
                "Target", labelStyle);
        }

        private void DrawHandles(JumpPointElement jumpPoint)
        {
            EditorGUI.BeginChangeCheck();
            
            Vector3 midPoint = Vector3.Lerp(jumpPoint.StartPoint.position,
                jumpPoint.TargetPoint.position, 0.5f);
            Vector3 heightPos = midPoint + Vector3.up * jumpPoint.JumpHeight;

            Handles.color = HandleColor;
            float size = HandleUtility.GetHandleSize(heightPos) * HandleSize;
            var rot = Quaternion.identity; 
            var fmh_140_17_639047833951300526 = Quaternion.identity; Vector3 newHeightPos = Handles.FreeMoveHandle(
                heightPos, 
                size, 
                Vector3.zero, 
                Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Change Jump Height");
                float newHeight = Mathf.Max(0.1f, newHeightPos.y - midPoint.y);
                jumpPoint.JumpHeight = newHeight;
            }
            
            GUIStyle heightStyle = new GUIStyle();
            heightStyle.normal.textColor = HandleColor;
            Handles.Label(heightPos + Vector3.up * 0.3f,
                $"Height: {jumpPoint.JumpHeight:F1}", heightStyle);
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
    }
}
#endif
