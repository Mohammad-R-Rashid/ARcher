using System;
using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class JumpObstacleState : IObstacleMovementState
    {
        private Action<bool> _callback;
        
        private JumpObstacleInfo _jumpObstacleInfo;
        private MovementBase _movement;

        public JumpObstacleState(MovementBase movement)
        {
            _movement = movement;
        }
        
        public void Overcome(ObstacleInfo obstacles, Action<bool> callback)
        {
            _callback = callback;
            _jumpObstacleInfo = obstacles as JumpObstacleInfo;
            if (_jumpObstacleInfo == null)
            {
                _callback(false);
                return;
            }
            
            _movement.StartCoroutine(JumpProcess());
        }

        private IEnumerator JumpProcess()
        {
            bool result = false;
            bool inProcess = true;
            _movement.RotateToTarget(_jumpObstacleInfo.JumpPoint.TargetPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);
            
            _movement.SetJumpHeight(_jumpObstacleInfo.JumpPoint.JumpHeight);
            
            if (_jumpObstacleInfo.JumpPoint.Door != null)
            {
                yield return WaitForDoorState(_jumpObstacleInfo.JumpPoint.Door, _jumpObstacleInfo.JumpPoint.DoorActivationMode);
            }
            
            if (_jumpObstacleInfo.JumpObstacleSound != null)
            {
                _jumpObstacleInfo.JumpObstacleSound.Play();
            }
            inProcess = true;
            
            _movement.JumpToTarget(_jumpObstacleInfo.JumpPoint.TargetPoint, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            
            yield return new WaitUntil(() => !inProcess);
            
            _callback.Invoke(result);
        }
        
        private IEnumerator WaitForDoorState(DoorComponentBase door, DoorActivationMode requiredMode)
        {
            bool IsDoorInRequiredState()
            {
                switch (requiredMode)
                {
                    case DoorActivationMode.TopOnly:
                        return door.IsAtTop;
                    case DoorActivationMode.BottomOnly:
                        return door.IsAtBottom;
                    case DoorActivationMode.Both:
                        return door.IsAtTop || door.IsAtBottom;
                    case DoorActivationMode.AnyPosition:
                        return true;
                    default:
                        return true;
                }
            }
        
            while (!IsDoorInRequiredState())
            {
                yield return null;
            }
        }
    }
}
