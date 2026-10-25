using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ithappy
{
    public enum MovementStateName
    {
        None = 0,
        Idle = 1,
        Move = 2,
        Run = 3,
        Rotate = 4,
        Jump = 5,
        MoveNavMesh = 6,
        RunNavMesh = 7,
        Climb = 8,
    }
    
    public abstract class MovementBase : MonoBehaviour
    {
        protected class NextStateElementInfo
        {
            public Action NextStateInitAction;
            public Action<bool> Callback;

            public void Clear()
            {
                NextStateInitAction = null;
                Callback = null;
            }
        }
        
        protected class CurrentStateElementInfo
        {
            public IMovementState State;
            public Action<bool> Callback;

            public void Clear()
            {
                State = null;
                Callback = null;
            }
        }
        
        [SerializeField] protected Transform _moveParent;
        [SerializeField] protected float _moveSpeed = 1f;
        [SerializeField] protected float _runSpeed = 2f;
        [SerializeField] protected float _ladderSpeed = 2f;
        [SerializeField] protected float _jumpTime = 1f;
        [SerializeField] protected float _rotateSpeed = 120f;
        [SerializeField] protected float _jumpHeight = 0f;
        [SerializeField] protected Transform _modelParent;
        [SerializeField] protected Vector3 _ladderRotationOffset; 
        
        [Header("References")]
        [SerializeField] protected NavMeshAgent _agent;

        protected abstract Dictionary<MovementStateName, IMovementState> StatesPool { get; }
        public abstract Dictionary<ObstacleType, IObstacleMovementState> ObstacleStatesPool { get; }
        
        private Transform _startParent;
        private CurrentStateElementInfo _currentStateInfo = new CurrentStateElementInfo();
        private NextStateElementInfo _nextStateInfo = new NextStateElementInfo();
        private float _startJumpHeight;

        public float MoveSpeed => _moveSpeed;
        public float RunSpeed => _runSpeed;
        public float LadderSpeed => _ladderSpeed;
        public float RotateSpeed => _rotateSpeed;
        public float JumpHeight => _jumpHeight;
        public CharacterBase CharacterBase { get; private set; }
        public NavMeshAgent Agent => _agent;
        public Transform MoveParent => _moveParent;
        public Transform StartParent => _startParent;
        public Vector3 LadderRotationOffset => _ladderRotationOffset;
        public Transform ModelParent => _modelParent;

        private void Update()
        {
            _currentStateInfo.State.Update();
        }
        
        public virtual void Initialize(CharacterBase characterBase)
        {
            CharacterBase = characterBase;
            _startParent = MoveParent.parent;
            _startJumpHeight = _jumpHeight;
            
            TrySwitchStateVector3(MovementStateName.Idle, Vector3.zero);
        }
        
        public virtual void Dispose()
        {
        }

        public void SetJumpHeight(float jumpHeight)
        {
            _jumpHeight = jumpHeight;
        }

        public void SetDefaultJumpHeight()
        {
            _jumpHeight = _startJumpHeight;
        }
        
        public void Stop()
        {
            TrySwitchStateVector3(MovementStateName.Idle, Vector3.zero);
        }
        
        public void NavMeshMoveToTarget(Vector3 target, Action<bool> callback = null)
        {
            TrySwitchStateVector3(MovementStateName.MoveNavMesh, target, callback);
        }
        
        public void NavMeshRunToTarget(Vector3 target, Action<bool> callback = null)
        {
            TrySwitchStateVector3(MovementStateName.RunNavMesh, target, callback);
        }
        
        public void ClimbToTarget(Vector3 target, Action<bool> callback = null)
        {
            TrySwitchStateVector3(MovementStateName.Climb, target, callback);
        }
        
        public void MoveToTarget(Vector3 target, Action<bool> callback = null)
        {
            TrySwitchStateVector3(MovementStateName.Move, target, callback);
        }
        
        public void JumpToTarget(Vector3 target, Action<bool> callback = null)
        {
            TrySwitchStateVector3(MovementStateName.Jump, target, callback);
        }
        
        public void JumpToTarget(Transform target, Action<bool> callback = null)
        {
            TrySwitchStateTransform(MovementStateName.Jump, target, callback);
        }
        
        public void RotateToTarget(Vector3 target, Action<bool> callback = null)
        {
            TrySwitchStateVector3(MovementStateName.Rotate, target, callback);
        }

        protected void TrySwitchStateVector3(MovementStateName stateName, Vector3 target, Action<bool> callback = null)
        {
            _nextStateInfo.Callback?.Invoke(false);
            
            Action nextStateInitAction = () =>
            {
                _currentStateInfo.Callback = callback;
                IMovementState state = StatesPool[stateName];
                _currentStateInfo.State = state;
                _currentStateInfo.State.OnComplete += OnStateComplete;
                state.Enter(target);
            };
            
            TrySwitchState(nextStateInitAction, callback);
        }
        
        protected void TrySwitchStateTransform(MovementStateName stateName, Transform target, Action<bool> callback = null)
        {
            _nextStateInfo.Callback?.Invoke(false);
            
            Action nextStateInitAction = () =>
            {
                _currentStateInfo.Callback = callback;
                IMovementState state = StatesPool[stateName];
                _currentStateInfo.State = state;
                _currentStateInfo.State.OnComplete += OnStateComplete;
                state.Enter(target);
            };

            TrySwitchState(nextStateInitAction, callback);
        }

        protected void TrySwitchState(Action initAction, Action<bool> callback = null)
        {
            _nextStateInfo.Callback?.Invoke(false);

            Action nextStateInitAction = initAction;
            
            if (_currentStateInfo.State == null)
            {
                nextStateInitAction.Invoke();
                return;
            }
            
            _nextStateInfo.Callback = callback;
            _nextStateInfo.NextStateInitAction = nextStateInitAction;
            
            if (_currentStateInfo.State != null)
            {
                _currentStateInfo.State.Exit();
            }
        }

        protected void SwitchState(Action nextStateInit)
        {
            nextStateInit.Invoke();
        }

        private void OnStateComplete(bool isSuccess)
        {
            _currentStateInfo.State.OnComplete -= OnStateComplete;
            _currentStateInfo.Callback?.Invoke(isSuccess);
            _currentStateInfo.Clear();

            if (_nextStateInfo.NextStateInitAction != null)
            {
                SwitchState(_nextStateInfo.NextStateInitAction);
                _nextStateInfo.Clear();
            }
            else
            {
                TrySwitchStateVector3(MovementStateName.Idle, Vector3.zero);
            }
            
        }
    }
}
