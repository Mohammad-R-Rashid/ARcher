using System;
using UnityEngine;
using UnityEngine.AI;

namespace ithappy
{
    public class ObstaclePath : MonoBehaviour
    {
        public event Action<OffMeshLink, bool> OnJumpPointStateChange;
        
        [SerializeField] private ObstacleBase[] _frontObstacles;
        [SerializeField] private ObstacleBase[] _backObstacles;
        
        private OffMeshLink _offMeshLink;

        private void Awake()
        {
            _offMeshLink = GetComponent<OffMeshLink>();
        }

        public ObstacleBase[] GetNearestPath(Vector3 characterPos)
        {
            if (_backObstacles.Length == 0)
            {
                return _frontObstacles;
            }

            if (_frontObstacles.Length == 0)
            {
                return _frontObstacles;
            }

            if (Vector3.Distance(characterPos, _frontObstacles[0].StartPoint.position) <
                Vector3.Distance(characterPos, _backObstacles[0].StartPoint.position))
            {
                return _frontObstacles;
            }

            return _backObstacles;
        }

        public void SetIsUsedPath(bool isUsed)
        {
            OnJumpPointStateChange?.Invoke(_offMeshLink, isUsed);
        }
    }
}
