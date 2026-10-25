using System;
using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class JumpMovementState : CharacterMovementStateBase
    {
        private JumpObstacleInfo _jumpObstacleInfo;
        private Transform _jumpTarget;
        
        public JumpMovementState(Movement context) : base(context)
        {
        }

        public override void Enter(Vector3 target)
        {
            SetAnimationSpeed(0);
            _context.StartCoroutine(ExecuteParabolicJump(target));
        }

        public override void Enter(Transform target)
        {
            SetAnimationSpeed(0);
            _jumpTarget = target;
            _context.StartCoroutine(ExecuteParabolicJump(_jumpTarget.position));
        }

        public override void Exit()
        {
            base.Exit();
            
            _jumpTarget = null;
        }

        private IEnumerator ExecuteParabolicJump(Vector3 target)
        {
            yield return ExecuteJumpInternal(_context.MoveParent.position, target);
        }

        private IEnumerator ExecuteJumpInternal(Vector3 startPos, Vector3 target)
        {
            CharacterAnimator.JumpAnimationInfo jumpAnimationInfo = _context.CharacterBase.CharacterAnimator.Jump();
            yield return new WaitForSeconds(jumpAnimationInfo.PreparationTime);
            
            if (_jumpTarget != null)
            {
                _context.MoveParent.parent = _context.StartParent;
            }
            Vector3 endPos = target;
            float calculatedHeight = CalculateJumpHeight(_context.JumpHeight, startPos, endPos);

            float elapsed = 0f;
            while (elapsed < jumpAnimationInfo.JumpTime)
            {
                if (_jumpTarget != null)
                {
                    endPos = _jumpTarget.position;
                }
                
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / jumpAnimationInfo.JumpTime);

                UpdateJumpPosition(_context.MoveParent, startPos, endPos, calculatedHeight, t);

                yield return null;
            }

            _context.MoveParent.position = endPos;
            if (_jumpTarget != null)
            {
                _context.MoveParent.parent = _jumpTarget;
            }
            yield return new WaitForSeconds(jumpAnimationInfo.LandTime);
            
            OnCompleteInvoke(true);
        }
        
        private void UpdateJumpPosition(Transform target, Vector3 start, Vector3 end, float height, float t)
        {
            target.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * height);
        }

        private float CalculateJumpHeight(float baseHeight, Vector3 start, Vector3 end)
        {
            float verticalDiff = end.y - start.y;
            return verticalDiff > 0
                ? baseHeight + verticalDiff * 0.5f
                : Mathf.Max(baseHeight * 0.3f, baseHeight + verticalDiff * 0.2f);
        }
    }
}
