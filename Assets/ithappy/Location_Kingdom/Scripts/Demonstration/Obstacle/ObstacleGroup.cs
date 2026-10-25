using UnityEngine;
using UnityEngine.AI;

namespace ithappy
{
    public class ObstacleGroup : MonoBehaviour
    {
        [SerializeField] private ObstaclePath[] _obstaclePaths;

        private int _currentFreeJumpPointCount = 0;

        private void Start()
        {
            foreach (ObstaclePath jumpPoint in _obstaclePaths)
            {
                jumpPoint.OnJumpPointStateChange += JumpPointOnOnJumpPointStateChange;
            }

            _currentFreeJumpPointCount = _obstaclePaths.Length;
        }

        private void JumpPointOnOnJumpPointStateChange(OffMeshLink offMeshLink, bool status)
        {
            if (status)
            {
                _currentFreeJumpPointCount--;

                if (_currentFreeJumpPointCount != 0)
                {
                    offMeshLink.activated = false;
                }
                else
                {
                    offMeshLink.costOverride = 1000;
                }
            }
            else
            {
                _currentFreeJumpPointCount++;
                offMeshLink.activated = true;
                offMeshLink.costOverride = -1;
            }
        }
    }
}
