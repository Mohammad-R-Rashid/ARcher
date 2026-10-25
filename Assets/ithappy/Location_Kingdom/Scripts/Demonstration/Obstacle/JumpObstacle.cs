using System;
using UnityEngine;

namespace ithappy
{
    [Serializable]
    public class JumpPointElement
    {
        public Transform StartPoint;
        public Transform TargetPoint;
        public float JumpHeight = 2f;
        public DoorComponentBase Door;
        public DoorActivationMode DoorActivationMode;
    }

    [Serializable]
    public class JumpObstacleInfo : ObstacleInfo
    {
        [SerializeField] private JumpPointElement _jumpPoint;
        [SerializeField] private AudioSource _jumpObstacleSound;

        public JumpPointElement JumpPoint
        {
            get => _jumpPoint;
            set => _jumpPoint = value;
        }

        public AudioSource JumpObstacleSound => _jumpObstacleSound;
    }

    public class JumpObstacle : ObstacleBase
    {
        [SerializeField] private JumpObstacleInfo _jumpObstacleInfo;
        
        public override Transform StartPoint => _jumpObstacleInfo.JumpPoint.StartPoint;
        public override Transform EndPoint => _jumpObstacleInfo.JumpPoint.TargetPoint;
        public override ObstacleInfo ObstacleInfo => _jumpObstacleInfo;
        public override ObstacleType ObstacleType => ObstacleType.Jump;

        public JumpObstacleInfo JumpObstacleInfo
        {
            get => _jumpObstacleInfo;
            set => _jumpObstacleInfo = value;
        }
        
        private void Awake()
        {
            if (_jumpObstacleInfo?.JumpPoint == null) return;
            
            if (_jumpObstacleInfo.JumpPoint.StartPoint != null)
            {
                if (Physics.Raycast(_jumpObstacleInfo.JumpPoint.StartPoint.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit startHit))
                {
                    _jumpObstacleInfo.JumpPoint.StartPoint.SetParent(startHit.transform);
                }
            }
            
            if (_jumpObstacleInfo.JumpPoint.TargetPoint != null)
            {
                if (Physics.Raycast(_jumpObstacleInfo.JumpPoint.TargetPoint.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit endHit))
                {
                    _jumpObstacleInfo.JumpPoint.TargetPoint.SetParent(endHit.transform);
                }
            }
        }
        
        private void OnDrawGizmos()
        {
            if (_jumpObstacleInfo?.JumpPoint == null) return;
            if (_jumpObstacleInfo.JumpPoint.StartPoint == null || 
                _jumpObstacleInfo.JumpPoint.TargetPoint == null) return;
        
            Vector3 start = _jumpObstacleInfo.JumpPoint.StartPoint.position;
            Vector3 end = _jumpObstacleInfo.JumpPoint.TargetPoint.position;
            Vector3 mid = Vector3.Lerp(start, end, 0.5f);
            mid.y += _jumpObstacleInfo.JumpPoint.JumpHeight;
        
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.5f);
            Gizmos.DrawLine(start, mid);
            Gizmos.DrawLine(mid, end);
        
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.7f);
            Gizmos.DrawSphere(start, 0.2f);
        
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
            Gizmos.DrawSphere(end, 0.2f);
        }
    }
}
