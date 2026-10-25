using UnityEngine;

namespace Siege
{
    // Editor/no-AR fallback camera: right-drag to look around, scroll to dolly.
    public class SimCameraRig : MonoBehaviour
    {
        public Transform cam;
        float m_Yaw, m_Pitch = 18f;

        void Start()
        {
            var e = cam.localEulerAngles;
            m_Yaw = e.y;
            m_Pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        void Update()
        {
            if (Input.GetMouseButton(1))
            {
                m_Yaw += Input.GetAxis("Mouse X") * 3f;
                m_Pitch = Mathf.Clamp(m_Pitch - Input.GetAxis("Mouse Y") * 3f, -30f, 70f);
            }
            cam.localRotation = Quaternion.Euler(m_Pitch, m_Yaw, 0);
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) cam.localPosition += cam.localRotation * Vector3.forward * scroll * 0.05f;
        }

        public void LookAt(Vector3 world)
        {
            var d = world - cam.position;
            var rot = Quaternion.LookRotation(d);
            var e = rot.eulerAngles;
            m_Yaw = e.y;
            m_Pitch = e.x > 180 ? e.x - 360 : e.x;
        }
    }
}
