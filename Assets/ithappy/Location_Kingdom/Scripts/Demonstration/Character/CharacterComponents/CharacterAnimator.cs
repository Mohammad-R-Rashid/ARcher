using System;
using UnityEngine;

namespace ithappy
{
    public enum AnimationType
    {
        None = 0,
        Idle = 1,
        Sit = 2,
        Flip = 3,
        Eat = 4,
    }

    public class CharacterAnimator : MonoBehaviour
    {
        [Serializable]
        public class JumpAnimationInfo
        {
            [SerializeField] AnimationClip _animationClip;
            [SerializeField] float _preparationPercent = 0.21f;
            [SerializeField] float _landPercent = 0.31f;

            public AnimationClip AnimationClip => _animationClip;
            public float PreparationTime { get; private set; }
            public float LandTime { get; private set; }
            public float JumpTime { get; private set; }

            public void Init()
            {
                if (AnimationClip == null)
                    return;
            
                PreparationTime = AnimationClip.length * _preparationPercent;
                LandTime = AnimationClip.length * _landPercent;
                JumpTime = AnimationClip.length * (1 - _preparationPercent - _landPercent);
            }
        }
        
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int JumpTrigger = Animator.StringToHash("Jump");
        private static readonly int SitTrigger = Animator.StringToHash("Sit");
        private static readonly int FlipTrigger = Animator.StringToHash("Flip");
        private static readonly int EatTrigger = Animator.StringToHash("Eat");
        
        [SerializeField] private Animator _animator;
        [SerializeField] private JumpAnimationInfo _jumpAnimationinfo;

        private void Awake()
        {
            _jumpAnimationinfo.Init();
        }
        
        public void Initialize()
        {
        }

        public void Dispose()
        {
        }

        public void ActivateAnimation(AnimationType animationType)
        {
            switch (animationType)
            {
                case AnimationType.None:
                    break;
                case AnimationType.Idle:
                    break;
                case AnimationType.Sit:
                    Sit();
                    break;
                case AnimationType.Flip:
                    Flip();
                    break;
                case AnimationType.Eat:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void SetMoveSpeed(float speed)
        {
            _animator.SetFloat(Speed, speed);
        }
        
        public float GetMoveSpeed()
        {
            return _animator.GetFloat(Speed);
        }

        public JumpAnimationInfo Jump()
        {
            _animator.SetTrigger(JumpTrigger);
            return _jumpAnimationinfo;
        }

        public void Sit()
        {
            _animator.SetTrigger(SitTrigger);
        }

        public void Flip()
        {
            _animator.SetTrigger(FlipTrigger);
        }
        
        public void Eat(bool isActive)
        {
            _animator.SetBool(EatTrigger, isActive);
        }

        public void ClimbUp(bool isActive)
        {
            //_animator.SetBool(ClimbUpTrigger, isActive);
        }

        public void ClimbDown(bool isActive)
        {
            //_animator.SetBool(ClimbDownTrigger, isActive);
        }
    }
}
