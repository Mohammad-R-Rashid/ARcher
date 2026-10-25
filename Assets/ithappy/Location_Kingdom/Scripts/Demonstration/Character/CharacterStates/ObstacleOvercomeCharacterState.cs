using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class ObstacleOvercomeCharacterState : CharacterStateBase
    {
        private MovementBase _movement;
        private ObstacleBase[] _currentObstacles;
        private ObstaclePath _currentObstaclePath;
        
        public ObstacleOvercomeCharacterState(CharacterBase context, MovementBase movement) : base(context)
        {
            _movement = movement;
        }

        public override void Enter()
        {
            base.Enter();
            
            if (!_movement.Agent.isOnOffMeshLink)
            {
                CharacterBase.NextState();
                return;
            }
            
            _currentObstaclePath = _movement.Agent.currentOffMeshLinkData.offMeshLink.transform.GetComponent<ObstaclePath>();
            _currentObstacles = _currentObstaclePath.GetNearestPath(_movement.MoveParent.position);
            _currentObstaclePath.SetIsUsedPath(true);
            CharacterBase.StartCoroutine(ObstaclesProcess());
        }

        public override void Update()
        {
        }

        public override void Exit()
        {
            _currentObstaclePath.SetIsUsedPath(false);
            _currentObstaclePath = null;
        }

        public override bool ShouldEnter()
        {
            return _movement.Agent.isOnOffMeshLink;
        }

        private IEnumerator ObstaclesProcess()
        {
            bool result;
            bool inProcess = true;
            
            for (int i = 0; i < _currentObstacles.Length; i++)
            {
                _movement.MoveParent.parent = _currentObstacles[i].StartPoint;
                inProcess = true;
                _movement.ObstacleStatesPool[_currentObstacles[i].ObstacleType].Overcome(_currentObstacles[i].ObstacleInfo, (isComplete) =>
                {
                    result = isComplete;
                    inProcess = false;
                });
                
                yield return new WaitUntil(() => !inProcess);
            }
            
            _movement.Agent.CompleteOffMeshLink();
            _movement.MoveParent.parent = _movement.StartParent;
            CharacterBase.NextState();
        }
    }
}
