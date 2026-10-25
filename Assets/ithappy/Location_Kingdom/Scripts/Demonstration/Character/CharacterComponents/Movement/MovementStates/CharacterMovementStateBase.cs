using System;
using UnityEngine;

namespace ithappy
{
    public abstract class CharacterMovementStateBase : IMovementState
    {
        public event Action<bool> OnComplete; 
        
        protected Movement _context;
        protected bool _shouldStop;
        private float _accelerationTime = .3f;
        private float _currentAccelerationTime;
        private bool _shouldUpdateSpeed = false;
        private float _startSpeed;
        private float _targetSpeed;

        public CharacterMovementStateBase(Movement context)
        {
            _context = context;
        }

        public abstract void Enter(Vector3 target);
        public abstract void Enter(Transform target);

        public virtual void Update()
        {
            if (_shouldUpdateSpeed)
            {
                _currentAccelerationTime += Time.deltaTime;
                if (_currentAccelerationTime >= _accelerationTime)
                {
                    _currentAccelerationTime = _accelerationTime;
                    _shouldUpdateSpeed = false;
                }
                
                _context.CharacterBase.CharacterAnimator.SetMoveSpeed(Mathf.Lerp(_startSpeed, _targetSpeed, _currentAccelerationTime / _accelerationTime));
            }
        }

        public virtual void Exit()
        {
            _shouldUpdateSpeed = false;
        }

        protected void SetAnimationSpeed(float speed)
        {
            _startSpeed = _context.CharacterBase.CharacterAnimator.GetMoveSpeed();
            _targetSpeed = speed;
            _shouldUpdateSpeed = true;
            _currentAccelerationTime = 0;
        }
        
        protected void OnCompleteInvoke(bool isSuccess)
        {
            OnComplete?.Invoke(isSuccess);
        }
    }
}
