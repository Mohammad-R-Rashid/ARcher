using System;
using System.Collections.Generic;
using UnityEngine;

namespace ithappy
{
    public class StaticCharacter : CharacterBase
    {
        [SerializeField] private AnimationType _animationType = AnimationType.None;
        [SerializeField] private float _animStartTime = 2f;
        [SerializeField] private float _animationFrequency = 10f;
        
        protected Dictionary<Type, CharacterStateBase> _states;

        protected override Dictionary<Type, CharacterStateBase> States => _states;

        public override void Initialize()
        {
            base.Initialize();
            
            _states = new Dictionary<Type, CharacterStateBase>
            {
                {
                    typeof(IdleCharacterState), new IdleCharacterState(this, _animationType, _animStartTime, _animationFrequency)
                },
            };
            
            _states[typeof(IdleCharacterState)].SetStatesToTransition(new List<CharacterStateBase>());

            TransitionToState(typeof(IdleCharacterState));
        }
    }
}
