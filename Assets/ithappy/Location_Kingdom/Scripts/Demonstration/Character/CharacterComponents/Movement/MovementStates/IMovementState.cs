using System;
using UnityEngine;

namespace ithappy
{
    public interface IMovementState
    {
        public event Action<bool> OnComplete;
        
        public void Update();
        public void Exit();
        public void Enter(Vector3 target);
        public void Enter(Transform target);
    }
}
