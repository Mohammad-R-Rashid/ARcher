using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Siege
{
    // Detected planes / LiDAR mesh chunks: dotted grid while choosing placements, then an invisible
    // shadow catcher (with colliders) so rubble bounces on and shadows fall onto the real room.
    public class ARSurface : MonoBehaviour
    {
        public Material gridMaterial;
        public Material shadowMaterial;
        public bool isLidarMesh;

        Renderer m_R;
        bool? m_Grid;

        void Awake()
        {
            m_R = GetComponent<Renderer>();
            gameObject.layer = Layers.Environment;
        }

        void Update()
        {
            bool grid = !isLidarMesh && GameManager.Instance && GameManager.Instance.IsPlacing;
            if (m_Grid == grid) return;
            m_Grid = grid;
            if (m_R) m_R.sharedMaterial = grid ? gridMaterial : shadowMaterial;
        }
    }

}
