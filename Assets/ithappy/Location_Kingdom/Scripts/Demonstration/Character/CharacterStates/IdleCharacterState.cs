using UnityEngine;

namespace ithappy
{
    public class IdleCharacterState : CharacterStateBase
    {
        private AnimationType _animationType;
        private float _animFrequency;
        private float _currentAnimTime;
        private float _animStartTime;
        private float _currentAnimStartTime;
        
        public IdleCharacterState(CharacterBase context, AnimationType animationType, float animStartTime, float animFrequency) : base(context)
        {
            _animationType = animationType;
            _animStartTime = animStartTime;
            _animFrequency = animFrequency;
        }

        public override void Enter()
        {
            base.Enter();

            _currentAnimStartTime = _animStartTime;
            _currentAnimTime = _animFrequency;
        }

        public override void Update()
        {
            if (_animationType == AnimationType.None)
            {
                return;
            }

            if (_currentAnimStartTime > 0)
            {
                _currentAnimStartTime -= Time.deltaTime;
                return;
            }

            if (_currentAnimTime > 0)
            {
                _currentAnimTime -= Time.deltaTime;

                if (_currentAnimTime <= 0)
                {
                    CharacterBase.CharacterAnimator.ActivateAnimation(_animationType);
                    _currentAnimTime = _animFrequency;
                }
            }
        }

        public override void Exit()
        {
        }

        public override bool ShouldEnter()
        {
            return true;
        }
    }
}
