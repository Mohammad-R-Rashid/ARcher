using UnityEngine;

namespace ithappy
{
    public class CubePlacerManager : MonoBehaviour
    {
        [SerializeField] private GameObject cubePrefab;
        [SerializeField] private LayerMask placementLayer = 1;
        
        private bool _isPlacing = false;
        private Vector3 _firstPoint;
        private Vector3 _secondPoint;
        private int _pointsPlaced = 0;

        public bool IsPlacing() => _isPlacing;
        public int PointsPlaced() => _pointsPlaced;
        public Vector3 FirstPoint() => _firstPoint;
        public LayerMask PlacementLayer => placementLayer;

        public void StartCubePlacement()
        {
            _isPlacing = true;
            _pointsPlaced = 0;
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
                _secondPoint = point;
                CreateCubeBetweenPoints(_firstPoint, _secondPoint);
                _isPlacing = false;
                _pointsPlaced = 0;
            }
        }

        private void CreateCubeBetweenPoints(Vector3 startPos, Vector3 endPos)
        {
            GameObject cube;
            
            if (cubePrefab != null)
            {
                cube = Instantiate(cubePrefab);
            }
            else
            {
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "PlacedCube";
            }

            UpdateCubeTransform(startPos, endPos, cube.transform);
        }

        private void UpdateCubeTransform(Vector3 startPos, Vector3 endPos, Transform cubeTransform)
        {
            Vector3 middlePoint = (startPos + endPos) / 2f;
            
            float distance = Vector3.Distance(startPos, endPos);
            
            Vector3 direction = (endPos - startPos).normalized;
            
            middlePoint.y -= 0.5f;
            
            cubeTransform.position = middlePoint;
            
            if (direction != Vector3.zero)
            {
                cubeTransform.rotation = Quaternion.LookRotation(direction);
                
                cubeTransform.Rotate(0, 90, 0, Space.Self);
            }
            
            cubeTransform.localScale = new Vector3(distance, 1f, 1f);
        }

        public void CancelPlacement()
        {
            _isPlacing = false;
            _pointsPlaced = 0;
        }
    }
}