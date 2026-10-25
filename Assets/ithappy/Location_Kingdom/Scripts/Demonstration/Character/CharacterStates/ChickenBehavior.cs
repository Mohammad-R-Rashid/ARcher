using System;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace ithappy
{
    public class ChickenBehavior : CharacterStateBase
    {
        private Action<bool> _onReached;
        
        private MovementBase _movement;
        private Vector3 _randomPoint;
        private float _moveAreaDistance = 5f;
        private Vector2 _eatTime = new Vector2(5, 10);
        private float _currentEatTime;
        private bool _movingToRandomPoint;
        private bool _isEating;
        private Vector3 _initPosition;
        
        public ChickenBehavior(CharacterBase context, MovementBase movement) : base(context)
        {
            _movement = movement;
            _initPosition = _movement.MoveParent.position;
        }
        
        public override void Enter()
        {
            base.Enter();
            
            if (_movingToRandomPoint)
            {
                MoveToFood();
            }
        }

        public override void Update()
        {
            for (int i = 0; i < _statesToTransition.Count; i++)
            {
                if (_statesToTransition[i].ShouldEnter())
                {
                    CharacterBase.TransitionToState(_statesToTransition[i]);
                    break;
                }
            }

            if (_isEating && _currentEatTime > 0)
            {
                _currentEatTime -= Time.deltaTime;
                if (_currentEatTime <= 0)
                {
                    _isEating = false;
                    CharacterBase.CharacterAnimator.Eat(false);
                }
            }
            
            if (!_isEating && !_movingToRandomPoint && GetRandomPointOnNavMesh())
            {
                MoveToFood();
            }
        }

        public override void Exit()
        {
            _onReached = null;
        }

        public override bool ShouldEnter()
        {
            return true;
        }
        
        private void MoveToFood()
        {
            _onReached = (isReached) =>
            {
                if (isReached)
                {
                    _movingToRandomPoint = false;
                    _isEating = true;
                    CharacterBase.CharacterAnimator.Eat(true);
                    _currentEatTime = Random.Range(_eatTime.x, _eatTime.y);
                }
            };
            
            _movingToRandomPoint = true;
            _movement.NavMeshMoveToTarget(_randomPoint, _onReached);
        }
        
        private void MoveToRandomPoint()
        {
            _onReached = (isReached) =>
            {
                if (isReached)
                {
                    _movingToRandomPoint = false;
                }
            };
            
            _movingToRandomPoint = true;
            _movement.NavMeshMoveToTarget(_randomPoint, _onReached);
        }
        
        private bool GetRandomPointOnNavMesh()
        {
            Vector3 origin = _initPosition;
            
            Vector3 randomDirection = Random.insideUnitSphere * _moveAreaDistance;
            randomDirection.y = 0;
    
            Vector3 targetPosition = origin + randomDirection;
            
            if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, _moveAreaDistance, NavMesh.AllAreas))
            {
                return false;
            }
            
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(origin, hit.position, NavMesh.AllAreas, path))
            {
                return false;
            }

            if (path.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }
    
            _randomPoint = hit.position;
            return true;
        }
    }
}

