using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class MoveMovementState : CharacterMovementStateBase
    {
        private float _moveSpeed;
        
        public MoveMovementState(Movement context, float moveSpeed) : base(context)
        {
            _moveSpeed = moveSpeed;
        }

        public override void Enter(Vector3 target)
        {
            SetAnimationSpeed(_moveSpeed);
            _context.StartCoroutine(MoveAlongStairs(_context.MoveParent.position, target));
        }

        public override void Enter(Transform target)
        {
            SetAnimationSpeed(_moveSpeed);
            
            _context.StartCoroutine(MoveAlongStairs(_context.MoveParent.position, target.position));
        }

        public override void Exit()
        {
        }
        
        private IEnumerator MoveAlongStairs(Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(from, to);
            float duration = distance / _moveSpeed;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _context.MoveParent.position = Vector3.Lerp(from, to, t);

                yield return null;
            }

            _context.MoveParent.position = to;
            
            OnCompleteInvoke(true);
        }
    }
}
