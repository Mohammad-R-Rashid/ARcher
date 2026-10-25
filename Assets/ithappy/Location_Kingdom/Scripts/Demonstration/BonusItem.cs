using System.Collections;
using UnityEngine;

namespace ithappy
{
    public class BonusItem : MonoBehaviour
    {
        [SerializeField] private float _reload = 15f;

        private MeshRenderer _meshRenderer;
        private Collider _collider;
        private AudioSource _audioSource;

        private void Start()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            _collider = GetComponent<Collider>();
            _audioSource = GetComponent<AudioSource>();
        }

        private void OnTriggerEnter(Collider collider)
        {
            CharacterBase bonusSeeker = collider.GetComponent<CharacterBase>();
            if (bonusSeeker != null)
            {
                _meshRenderer.enabled = false;
                _collider.enabled = false;
                if (_audioSource != null)
                {
                    _audioSource.Play();
                }
                StartCoroutine(ReloadProgress());
            }
        }

        private IEnumerator ReloadProgress()
        {
            yield return new WaitForSeconds(_reload);

            _collider.enabled = true;
            _meshRenderer.enabled = true;
        }
    }
}
