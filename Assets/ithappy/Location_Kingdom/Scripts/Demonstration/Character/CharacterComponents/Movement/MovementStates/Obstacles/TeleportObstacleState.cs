using System;
using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class TeleportObstacleState : IObstacleMovementState
    {
        private Action<bool> _callback;
        private MovementBase _movement;
        private TeleportObstacleInfo _teleportObstacleInfo;
        
        public TeleportObstacleState(MovementBase movement)
        {
            _movement = movement;
        }
        
        public void Overcome(ObstacleInfo obstacles, Action<bool> callback)
        {
            _callback = callback;
            _teleportObstacleInfo = obstacles as TeleportObstacleInfo;
            
            _movement.StartCoroutine(TeleportProcess());
        }
        
        private IEnumerator TeleportProcess()
        {
            bool result;
            bool inProcess = true;
            _movement.RotateToTarget(_teleportObstacleInfo.TeleportPoint.StartTeleportPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);
                
            inProcess = true;
            _movement.MoveToTarget(_teleportObstacleInfo.TeleportPoint.StartTeleportPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);

            _movement.MoveParent.position = _teleportObstacleInfo.TeleportPoint.EndTeleportPoint.position;
            _teleportObstacleInfo.InSound.Play();
            _teleportObstacleInfo.OutSound.Play();
            
            inProcess = true;
            _movement.RotateToTarget(_teleportObstacleInfo.TeleportPoint.TargetTeleportPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);
            
            inProcess = true;
            _movement.MoveToTarget(_teleportObstacleInfo.TeleportPoint.TargetTeleportPoint.position, (isComplete) =>
            {
                result = isComplete;
                inProcess = false;
            });
            yield return new WaitUntil(() => !inProcess);

            yield return null;
            _callback?.Invoke(true);
        }
    }
}