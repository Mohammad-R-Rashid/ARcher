using UnityEngine;

namespace ithappy
{
    public enum DoorActivationMode
    {
        TopOnly,
        BottomOnly,
        Both,
        AnyPosition,
        Custom
    }

    public class DoorComponentBase : MonoBehaviour
    {
        public bool IsAtTop { get; protected set; }
        public bool IsAtBottom { get; protected set; }
    }
}
