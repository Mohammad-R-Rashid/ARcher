using System;
using UnityEngine;

public class CharacterAnimatorInvoker : MonoBehaviour
{
    public Action OnCharacterHello;
    public Action OnCharacterLeftStep;
    public Action OnCharacterRightStep;
    public Action OnCharacterJump;
    
    public void Hello()
    {
        OnCharacterHello?.Invoke();
    }
    
    public void LeftStep()
    {
        OnCharacterLeftStep?.Invoke();
    }
    
    public void RightStep()
    {
        OnCharacterRightStep?.Invoke();
    }
    
    public void Jump()
    {
        OnCharacterJump?.Invoke();
    }
}
