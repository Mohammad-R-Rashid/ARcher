using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ithappy
{
    [System.Serializable]
    public class TeleportPointElement
    {
        public Transform StartPoint;
        public Transform StartTeleportPoint;
        public Transform EndTeleportPoint;
        public Transform TargetTeleportPoint;
    }
    
    [Serializable]
    public class TeleportObstacleInfo : ObstacleInfo
    {
        [SerializeField] private TeleportPointElement _teleportPoint;
        [SerializeField] private AudioSource _inSound;
        [SerializeField] private AudioSource _outSound;

        public TeleportPointElement TeleportPoint
        {
            get => _teleportPoint;
            set => _teleportPoint = value;
        }

        public AudioSource InSound => _inSound;
        public AudioSource OutSound => _outSound;
    }

    public class TeleportObstacle : ObstacleBase
    {
        [SerializeField] private TeleportObstacleInfo _teleportObstacleInfo;
        
        public override Transform StartPoint => _teleportObstacleInfo.TeleportPoint.StartPoint;
        public override Transform EndPoint => _teleportObstacleInfo.TeleportPoint.StartTeleportPoint;
        public override ObstacleInfo ObstacleInfo => _teleportObstacleInfo;
        public override ObstacleType ObstacleType => ObstacleType.Teleport;
        
        public TeleportObstacleInfo TeleportObstacleInfo
        {
            get => _teleportObstacleInfo;
            set => _teleportObstacleInfo = value;
        }
        
        private void Awake()
        {
            if (_teleportObstacleInfo?.TeleportPoint == null) return;
            
            if (_teleportObstacleInfo.TeleportPoint.StartPoint != null)
            {
                if (Physics.Raycast(_teleportObstacleInfo.TeleportPoint.StartPoint.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit startHit))
                {
                    _teleportObstacleInfo.TeleportPoint.StartPoint.SetParent(startHit.transform);
                }
            }
            
            if (_teleportObstacleInfo.TeleportPoint.StartTeleportPoint != null)
            {
                if (Physics.Raycast(_teleportObstacleInfo.TeleportPoint.StartTeleportPoint.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit endHit))
                {
                    _teleportObstacleInfo.TeleportPoint.StartTeleportPoint.SetParent(endHit.transform);
                }
            }
            
            if (_teleportObstacleInfo.TeleportPoint.EndTeleportPoint != null)
            {
                if (Physics.Raycast(_teleportObstacleInfo.TeleportPoint.EndTeleportPoint.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit endHit))
                {
                    _teleportObstacleInfo.TeleportPoint.EndTeleportPoint.SetParent(endHit.transform);
                }
            }
            
            if (_teleportObstacleInfo.TeleportPoint.TargetTeleportPoint != null)
            {
                if (Physics.Raycast(_teleportObstacleInfo.TeleportPoint.TargetTeleportPoint.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit endHit))
                {
                    _teleportObstacleInfo.TeleportPoint.TargetTeleportPoint.SetParent(endHit.transform);
                }
            }
        }
        
        private void OnDrawGizmos()
        {
            // if (_teleportObstacleInfo?.TeleportPoint == null) return;
            // if (_teleportObstacleInfo.TeleportPoint.StartPoint == null || 
            //     _teleportObstacleInfo.TeleportPoint.TargetPoint == null) return;
            //
            // Vector3 start = _teleportObstacleInfo.TeleportPoint.StartPoint.position;
            // Vector3 end = _teleportObstacleInfo.TeleportPoint.TargetPoint.position;
            // Vector3 mid = Vector3.Lerp(start, end, 0.5f);
            // mid.y += _teleportObstacleInfo.TeleportPoint.JumpHeight;
            //
            // Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.5f);
            // Gizmos.DrawLine(start, mid);
            // Gizmos.DrawLine(mid, end);
            //
            // Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.7f);
            // Gizmos.DrawSphere(start, 0.2f);
            //
            // Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
            // Gizmos.DrawSphere(end, 0.2f);
        }
    }
}