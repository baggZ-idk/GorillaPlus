using UnityEngine;
using UnityEngine.Events;

namespace GorillaInteraction
{
    public class GorillaButton : MonoBehaviour
    {
        public UnityEvent onPressed;
        public string PlayerTag = "Player";

        private bool isPressed;
        private int touchingColliders;

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log(
                $"[GorillaButton] Trigger entered by: {other.name}, " +
                $"tag: {other.tag}, layer: {LayerMask.LayerToName(other.gameObject.layer)}",
                this
            );

            if (!other.CompareTag(PlayerTag))
            {
                Debug.Log("[GorillaButton] Rejected: collider tag doesn't match.", this);
                return;
            }

            touchingColliders++;

            if (isPressed)
                return;

            isPressed = true;
            Debug.Log("[GorillaButton] BUTTON PRESSED!", this);
            onPressed?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(PlayerTag))
                return;

            touchingColliders = Mathf.Max(0, touchingColliders - 1);

            if (touchingColliders == 0)
                isPressed = false;
        }
    }
}