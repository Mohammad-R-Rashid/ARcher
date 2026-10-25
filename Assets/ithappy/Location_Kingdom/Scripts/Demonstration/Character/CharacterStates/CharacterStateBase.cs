using System.Collections.Generic;
using UnityEngine;

namespace ithappy
{
    public abstract class CharacterStateBase
    {
        protected CharacterBase CharacterBase;
        protected List<CharacterStateBase> _statesToTransition;
        
        public CharacterStateBase(CharacterBase context)
        {
            CharacterBase = context;
        }

        public virtual void Enter()
        {
            //Debug.Log(GetType().Name + " " + CharacterBase.name);
        }
        public abstract void Update();
        public abstract void Exit();
        public abstract bool ShouldEnter();

        public void SetStatesToTransition(List<CharacterStateBase> statesToTransition)
        {
            _statesToTransition = statesToTransition;
        }
        public bool CanTransitionTo(CharacterStateBase nextState)
        {
            for (int i = 0; i < _statesToTransition.Count; i++)
            {
                if (nextState == _statesToTransition[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
