using System;
using System.Collections.Generic;

namespace ithappy
{
    public class Chicken : CharacterBase
    {
        protected Dictionary<Type, CharacterStateBase> _states;
        
        protected override Dictionary<Type, CharacterStateBase> States => _states;

        public override void Initialize()
        {
            base.Initialize();
            
            _states = new Dictionary<Type, CharacterStateBase>
            {
                {
                    typeof(ObstacleOvercomeCharacterState), new ObstacleOvercomeCharacterState(this, _movement)
                },
                {
                    typeof(ChickenBehavior), new ChickenBehavior(this, _movement)
                },
            };
            
            _states[typeof(ChickenBehavior)].SetStatesToTransition(new List<CharacterStateBase>
            {
                _states[typeof(ObstacleOvercomeCharacterState)],
            });
            _states[typeof(ObstacleOvercomeCharacterState)].SetStatesToTransition(new List<CharacterStateBase>());

            TransitionToState(typeof(ChickenBehavior));
        }
    }
}

