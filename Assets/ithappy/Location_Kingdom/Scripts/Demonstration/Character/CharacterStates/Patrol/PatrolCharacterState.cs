using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace ithappy
{
    public class PatrolCharacterState : CharacterStateBase
    {
        private Action<bool> _onReached;
        
        private MovementBase _movement;
        private List<PatrolWayPoint> _wayPoints;
        private int _currentWayPointIndex = -1;
        private bool _isWaiting = false;
        private float _currentTimer;
        
        public PatrolCharacterState(CharacterBase context, MovementBase movement, List<PatrolWayPoint> wayPoints) : base(context)
        {
            _movement = movement;
            _wayPoints = wayPoints;
        }
        
        public override void Enter()
        {
            base.Enter();
            
            if (_currentWayPointIndex == -1)
            {
                FindNearestWayPoint();
            }
            
            MoveToCurrentWayPoint();
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
            
            if (_isWaiting)
            {
                _currentTimer -= Time.deltaTime;
                if (_currentTimer <= 0)
                {
                    _isWaiting = false;
                    MoveToNextWayPoint();
                }
            }
        }

        public override void Exit()
        {
            _onReached = null;
            _isWaiting = false;
        }

        public override bool ShouldEnter()
        {
            return true;
        }
        
        private void FindNearestWayPoint()
        {
            if (_wayPoints == null || _wayPoints.Count == 0)
                return;

            float shortestDistance = float.MaxValue;
            int nearestIndex = 0;

            for (int i = 0; i < _wayPoints.Count; i++)
            {
                if (_wayPoints[i] == null) continue;
                
                NavMeshPath path = new NavMeshPath();
                if (NavMesh.CalculatePath(_movement.MoveParent.position, _wayPoints[i].transform.position, NavMesh.AllAreas, path))
                {
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        float pathLength = CalculatePathLength(path);
                        if (pathLength < shortestDistance)
                        {
                            shortestDistance = pathLength;
                            nearestIndex = i;
                        }
                    }
                }
            }

            _currentWayPointIndex = nearestIndex;
        }

        private float CalculatePathLength(NavMeshPath path)
        {
            float length = 0f;
            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                length += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }
            return length;
        }

        private void MoveToCurrentWayPoint()
        {
            if (_wayPoints == null || _wayPoints.Count == 0 || _currentWayPointIndex == -1)
                return;

            _onReached = (isReached) =>
            {
                if (isReached)
                {
                    _isWaiting = true;
                    _currentTimer = _wayPoints[_currentWayPointIndex].WaitTime;
                    if (_wayPoints[_currentWayPointIndex].ShouldRotate)
                    {
                        _movement.RotateToTarget(_wayPoints[_currentWayPointIndex].transform.position +
                                                 _wayPoints[_currentWayPointIndex].transform.forward);
                    }
                }
            };

            _movement.NavMeshMoveToTarget(_wayPoints[_currentWayPointIndex].transform.position, _onReached);
        }

        private void MoveToNextWayPoint()
        {
            if (_wayPoints == null || _wayPoints.Count == 0)
                return;
            
            _currentWayPointIndex = (_currentWayPointIndex + 1) % _wayPoints.Count;
            MoveToCurrentWayPoint();
        }
    }
}