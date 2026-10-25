using System;
using UnityEngine;
using UnityEngine.AI;

namespace ithappy
{
    public class MoveToNavMeshMovementState : CharacterMovementStateBase
    {
        private float _reachedDistance = 0.1f;
        private float _stopPositionToOccupiedLink = 2f;
        private float _speed;
        private Vector3 _target;
        private Transform _targetTransform;
        
        private bool _isLinkOccupied;
        private bool _isTargetReached = true;

        public MoveToNavMeshMovementState(Movement context, float speed) : base(context)
        {
            _speed = speed;
        }

        public bool CheckDestinationPoint(Vector3 target, out Vector3 navMeshTargetPoint)
        {
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                navMeshTargetPoint = hit.position;
                return true;
            }
            navMeshTargetPoint = Vector3.zero;
            return false;
        }

        public override void Enter(Vector3 target)
        {
            StartMove(target);
        }

        public override void Enter(Transform target)
        {
            _targetTransform = target;

            StartMove(_targetTransform.position);
        }

        private void StartMove(Vector3 target)
        {
            if (!CheckDestinationPoint(target, out Vector3 navMeshTargetPoint))
            {
                OnCompleteInvoke(false);
                return;
            }

            SetAnimationSpeed(_speed);
            _shouldStop = false;
            _target = navMeshTargetPoint;
            _isTargetReached = false;
            _context.Agent.updatePosition = true;
            _context.Agent.updateRotation = true;
            _context.Agent.isStopped = false;
            
            _context.Agent.speed = _speed;
            _context.Agent.SetDestination(_target);
        }

        public override void Exit()
        {
            _shouldStop = true;
            _targetTransform = null;
        }

        public override void Update()
        {
            base.Update();
            
            if (_shouldStop)
            {
                CompleteState(false);
            }
            
            if (_isTargetReached)
            {
                return;
            }
            
            if (!_isLinkOccupied && _context.Agent.nextOffMeshLinkData.offMeshLink != null
                                 && _context.Agent.nextOffMeshLinkData.offMeshLink.occupied
                                 && (Vector3.Distance(_context.MoveParent.position,
                                         _context.Agent.nextOffMeshLinkData.offMeshLink.startTransform.position) <= _stopPositionToOccupiedLink ||
                                     Vector3.Distance(_context.MoveParent.position,
                                         _context.Agent.nextOffMeshLinkData.offMeshLink.endTransform.position) <= _stopPositionToOccupiedLink))
            {
                _isLinkOccupied = true;
                _context.Agent.updatePosition = false;
                SetAnimationSpeed(0);
            }
            else if (_isLinkOccupied && _context.Agent.nextOffMeshLinkData.offMeshLink != null &&
                     !_context.Agent.nextOffMeshLinkData.offMeshLink.occupied)
            {
                SetAnimationSpeed(_speed);
                _isLinkOccupied = false;
                _context.Agent.updatePosition = true;
            }
            
            if (_context.Agent.isOnOffMeshLink)
            {
                CompleteState(false);
            }
            
            if (Vector3.Distance(_context.MoveParent.position, _target) <= _reachedDistance)
            {
                CompleteState(true);
            }
        }

        private void CompleteState(bool isSuccess)
        {
            _context.Agent.updatePosition = false;
            _context.Agent.updateRotation = false;
            _context.Agent.isStopped = true;
            _isTargetReached = true;
            OnCompleteInvoke(isSuccess);
        }
    }
}
