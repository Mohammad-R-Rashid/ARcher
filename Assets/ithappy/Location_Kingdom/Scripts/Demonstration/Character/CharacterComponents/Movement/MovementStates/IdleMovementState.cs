using System;
using UnityEngine;

namespace ithappy
{
    public class IdleMovementState : CharacterMovementStateBase
    {
        public IdleMovementState(Movement context) : base(context)
        {
        }

        public override void Enter(Vector3 target)
        {
            SetAnimationSpeed(0);
        }

        public override void Enter(Transform target)
        {
        }

        public override void Exit()
        {
            base.Exit();
            
            OnCompleteInvoke(true);
        }
    }
}
