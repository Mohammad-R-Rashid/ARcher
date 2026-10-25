using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class LadderMovementState : CharacterMovementStateBase
    {
        public LadderMovementState(Movement context) : base(context)
        {
        }

        public override void Enter(Vector3 target)
        {
            StartClimb(target);
        }

        public override void Enter(Transform target)
        {
            SetAnimationSpeed(0);
            StartClimb(target.position);
        }
        
        private void StartClimb(Vector3 target)
        {
            _context.StartCoroutine(ClimbSequence(target));
        }

        private IEnumerator ClimbSequence(Vector3 target)
        {
            bool isAscending = _context.MoveParent.position.y < target.y;
            
            if (isAscending)
            {
                _context.CharacterBase.CharacterAnimator.ClimbUp(true);
            }
            else
            {
                _context.CharacterBase.CharacterAnimator.ClimbDown(true);
            }
            
            yield return MoveAlongStairs(_context.MoveParent.position, target);
            
            if (isAscending)
            {
                _context.CharacterBase.CharacterAnimator.ClimbUp(false);
            }
            else
            {
                _context.CharacterBase.CharacterAnimator.ClimbDown(false);
            }
            
            OnCompleteInvoke(true);
        }

        private IEnumerator MoveAlongStairs(Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(from, to);
            float duration = distance / _context.LadderSpeed;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _context.MoveParent.position = Vector3.Lerp(from, to, t);

                yield return null;
            }

            _context.MoveParent.position = to;
        }
    }
}
