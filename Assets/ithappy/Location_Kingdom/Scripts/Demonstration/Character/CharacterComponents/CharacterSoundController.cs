using UnityEngine;

public class CharacterSoundController : MonoBehaviour
{
    [SerializeField] private AudioSource _helloAudioSource;
    [SerializeField] private AudioSource _leftStepAudioSource;
    [SerializeField] private AudioSource _rightStepAudioSource;
    [SerializeField] private AudioSource _jumpAudioSource;

    private CharacterAnimatorInvoker _animatorInvoker;
    
    private void Start()
    {
        _animatorInvoker = GetComponentInChildren<CharacterAnimatorInvoker>();

        _animatorInvoker.OnCharacterHello += Hello;
        _animatorInvoker.OnCharacterLeftStep += LeftStep;
        _animatorInvoker.OnCharacterRightStep += RightStep;
        _animatorInvoker.OnCharacterJump += Jump;
    }

    public void Hello()
    {
        _helloAudioSource.Play();
    }
    
    public void LeftStep()
    {
        _leftStepAudioSource.Play();
    }
    
    public void RightStep()
    {
        _rightStepAudioSource.Play();
    }
    
    public void Jump()
    {
        _jumpAudioSource.Play();
    }
}
