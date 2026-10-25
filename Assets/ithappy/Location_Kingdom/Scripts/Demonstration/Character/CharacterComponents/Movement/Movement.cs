using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ithappy
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class Movement : MovementBase
    {
        protected override Dictionary<MovementStateName, IMovementState> StatesPool => _statesPool;
        public override Dictionary<ObstacleType, IObstacleMovementState> ObstacleStatesPool => _obstacleStatesPool;

        private Dictionary<MovementStateName, IMovementState> _statesPool;
        private Dictionary<ObstacleType, IObstacleMovementState> _obstacleStatesPool;
        
        public override void Initialize(CharacterBase characterBase)
        {
            _obstacleStatesPool = new Dictionary<ObstacleType, IObstacleMovementState>
            {
                { ObstacleType.Jump , new JumpObstacleState(this)},
                { ObstacleType.Ladder , new LadderObstacleState(this)},
                { ObstacleType.Teleport , new TeleportObstacleState(this)},
            };
            
            _statesPool = new Dictionary<MovementStateName, IMovementState>
            {
                { MovementStateName.Idle , new IdleMovementState(this) },
                { MovementStateName.Move , new MoveMovementState(this, MoveSpeed) },
                { MovementStateName.MoveNavMesh, new MoveToNavMeshMovementState(this, MoveSpeed) },
                { MovementStateName.RunNavMesh, new MoveToNavMeshMovementState(this, RunSpeed) },
                { MovementStateName.Jump, new JumpMovementState(this) },
                { MovementStateName.Rotate , new RotateMovementState(this, RotateSpeed)},
                { MovementStateName.Climb , new LadderMovementState(this)}
            };
            
            base.Initialize(characterBase);
        }
    }
}
