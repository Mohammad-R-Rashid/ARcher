using UnityEngine;
using UnityEngine.AI;

namespace ithappy
{
    public class ObstacleCreateManager : MonoBehaviour
    {
        private bool _isCreatingPath = false;
        private Vector3 _firstPoint;
        private GameObject _currentPath;
        private int _pointsPlaced = 0;

        public bool IsCreatingPath() => _isCreatingPath;
        public int PointsPlaced() => _pointsPlaced;
        public Vector3 FirstPoint() => _firstPoint;

        public void StartPathCreation()
        {
            _isCreatingPath = true;
            _pointsPlaced = 0;
            _currentPath = new GameObject("ObstaclePath");
            _currentPath.transform.SetParent(transform);
            _currentPath.AddComponent<ObstaclePath>();

            var offMeshLink = _currentPath.AddComponent<OffMeshLink>();
            offMeshLink.area = 2;
            offMeshLink.biDirectional = true;
        }

        public void AddPoint(Vector3 point)
        {
            if (_pointsPlaced == 0)
            {
                _firstPoint = point;
                _pointsPlaced = 1;
            }
            else
            {
                CreatePoints(_firstPoint, point);
                _isCreatingPath = false;
                _pointsPlaced = 0;
            }
        }

        private void CreatePoints(Vector3 startPos, Vector3 endPos)
        {
            GameObject startObj = new GameObject("StartPoint");
            startObj.transform.position = startPos;
            startObj.transform.SetParent(_currentPath.transform);

            GameObject endObj = new GameObject("EndPoint");
            endObj.transform.position = endPos;
            endObj.transform.SetParent(_currentPath.transform);

            var offMeshLink = _currentPath.GetComponent<OffMeshLink>();
            offMeshLink.startTransform = startObj.transform;
            offMeshLink.endTransform = endObj.transform;
        }

        public void CancelCreation()
        {
            if (_currentPath != null)
            {
                DestroyImmediate(_currentPath);
            }

            _isCreatingPath = false;
            _pointsPlaced = 0;
        }
    }
}
