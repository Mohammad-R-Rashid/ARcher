using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class RotateMovementState : CharacterMovementStateBase
    {
        private float _rotateTime = .5f;
        
        public RotateMovementState(Movement context, float rotateSpeed) : base(context)
        {
        }

        public override void Enter(Vector3 target)
        {
            SetAnimationSpeed(0);
            
            _context.StartCoroutine(RotateToTarget(target));
        }

        public override void Enter(Transform target)
        {
            _context.StartCoroutine(RotateToTarget(target.position));
        }

        public override void Exit()
        {
        }
        
        public IEnumerator RotateToTarget(Vector3 target)
        {
            Quaternion startRotation = _context.MoveParent.rotation;
            Quaternion endRotation = Quaternion.LookRotation(GetHorizontalDirection(_context.MoveParent.position, target));
            float elapsed = 0f;
        
            while (elapsed < _rotateTime)
            {
                elapsed += Time.deltaTime;
                _context.MoveParent.rotation = Quaternion.Slerp(startRotation, endRotation, Mathf.Clamp01(elapsed / _rotateTime));
                yield return null;
            }
        
            _context.MoveParent.rotation = endRotation;
            
            OnCompleteInvoke(true);
        }
        
        private Vector3 GetHorizontalDirection(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            dir.y = 0;
            return dir.normalized;
        }
    }
}
