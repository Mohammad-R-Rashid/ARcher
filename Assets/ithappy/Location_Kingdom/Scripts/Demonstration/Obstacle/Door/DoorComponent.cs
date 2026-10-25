using UnityEngine;
using System.Collections;

namespace ithappy
{
    public class DoorComponent : DoorComponentBase
    {
        [Header("Movement Settings")] [SerializeField]
        private Transform _topPoint;

        [SerializeField] private Transform _bottomPoint;
        [SerializeField] private float _moveSpeed = 1f;
        [SerializeField] private float _waitTimeAtTop = 2f;
        [SerializeField] private float _waitTimeAtBottom = 2f;

        [Header("State Settings")] [SerializeField]
        private float _positionThreshold = 0.1f;

        private Coroutine _movementRoutine;

        private void Start()
        {
            transform.position = _bottomPoint.position;
            _movementRoutine = StartCoroutine(DoorMovementCycle());
        }

        private IEnumerator DoorMovementCycle()
        {
            while (true)
            {
                yield return MoveToPosition(_topPoint.position);
                IsAtTop = true;
                IsAtBottom = false;
                yield return new WaitForSeconds(_waitTimeAtTop);
                IsAtTop = false;
                
                yield return MoveToPosition(_bottomPoint.position);
                IsAtTop = false;
                IsAtBottom = true;
                yield return new WaitForSeconds(_waitTimeAtBottom);
                IsAtBottom = false;
            }
        }

        private IEnumerator MoveToPosition(Vector3 target)
        {
            while (Vector3.Distance(transform.position, target) > _positionThreshold)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, _moveSpeed * Time.deltaTime);
                yield return null;
            }

            transform.position = target;
        }

        private void OnDisable()
        {
            if (_movementRoutine != null)
                StopCoroutine(_movementRoutine);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_topPoint && _bottomPoint)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(_topPoint.position, 0.1f);
                Gizmos.DrawSphere(_bottomPoint.position, 0.1f);
                Gizmos.DrawLine(_topPoint.position, _bottomPoint.position);
            }
        }
#endif
    }
}
