using System;
using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class LadderObstacleState : IObstacleMovementState
    {
        private Action<bool> _callback;
        private MovementBase _movement;
        private LadderObstacleInfo _ladderObstacleInfo;
        
        public LadderObstacleState(MovementBase movement)
        {
            _movement = movement;
        }
        
        public void Overcome(ObstacleInfo obstacles, Action<bool> callback)
        {
            _callback = callback;
            _ladderObstacleInfo = obstacles as LadderObstacleInfo;
            
            _movement.StartCoroutine(ClimbProcess());
        }
        
        private IEnumerator ClimbProcess()
        {
            bool isAscending = _movement.MoveParent.position.y < _ladderObstacleInfo.EndPoint.position.y;

            bool result;
            bool inProcess = true;
            _movement.RotateToTarget(_ladderObstacleInfo.StartLadderPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);
                
            inProcess = true;
            _movement.MoveToTarget(_ladderObstacleInfo.StartLadderPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);

            if (!isAscending)
            {
                inProcess = true;
                _movement.RotateToTarget(_ladderObstacleInfo.StartPoint.position, (isComplete) =>
                {
                    result = isComplete;
                    inProcess = false;
                });
                yield return new WaitUntil(() => !inProcess);
            }
            else
            {
                inProcess = true;
                _movement.RotateToTarget(_ladderObstacleInfo.EndPoint.position, (isComplete) =>
                {
                    result = isComplete;
                    inProcess = false;
                });
                yield return new WaitUntil(() => !inProcess);
            }

            _movement.StartCoroutine(RotateObjectCoroutine(_movement.LadderRotationOffset.x, .3f));
            
            inProcess = true;
            _movement.ClimbToTarget(_ladderObstacleInfo.MiddlePoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);
            
            _movement.StartCoroutine(RotateObjectCoroutine(0, .3f));
            
            inProcess = true;
            _movement.RotateToTarget(_ladderObstacleInfo.EndPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);
            
            inProcess = true;
            _movement.MoveToTarget(_ladderObstacleInfo.EndPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);

            _callback?.Invoke(true);
        }
        
        private IEnumerator RotateObjectCoroutine(float targetAngle, float duration)
        {
            Quaternion startRotation = _movement.ModelParent.localRotation;
            Quaternion targetRotation = Quaternion.Euler(targetAngle, _movement.ModelParent.localRotation.eulerAngles.y, _movement.ModelParent.localRotation.eulerAngles.z);
        
            float elapsedTime = 0f;
        
            while (elapsedTime < duration)
            {
                _movement.ModelParent.localRotation = Quaternion.Slerp(startRotation, targetRotation, elapsedTime / duration);
                elapsedTime += Time.deltaTime;
                yield return null;
            }
        
            _movement.ModelParent.localRotation = targetRotation;
        }
    }
}
