using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    public enum HorrorInteraction { Sign, Radio, Note, Key, Door }
    public sealed class HorrorInteractable : MonoBehaviour
    {
        public HorrorInteraction kind;
        public string textKey;
        public string contentId;
        public string textureKey;
        public Renderer surface;
        public Transform interactionAnchor;
        private Collider interactionCollider;
        private bool consumed;
        private Collider InteractionCollider
        {
            get
            {
                if (interactionCollider == null) interactionCollider = GetComponentInChildren<Collider>();
                return interactionCollider;
            }
        }
        // A door uses the leaf centre, not its hinge; small props use their collider centre.
        public Vector3 InteractionPoint => interactionAnchor != null ? interactionAnchor.position
            : InteractionCollider != null ? InteractionCollider.bounds.center : transform.position;
        public bool IsAvailable => isActiveAndEnabled && !consumed && InteractionCollider != null && InteractionCollider.enabled && InteractionCollider.gameObject.activeInHierarchy;
        public void Consume() { consumed = true; }
    }
}

