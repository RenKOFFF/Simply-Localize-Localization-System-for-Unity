using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    /// <summary>Character-space interaction selection. Camera aim never participates.</summary>
    public static class HorrorInteractionQuery
    {
        public static HorrorInteractable Find(Transform hero, HorrorInteractable[] targets,
            HorrorInteractable previous = null, float reach = 2.1f, float halfAngle = 65f)
        {
            if (hero == null || targets == null) return null;
            var origin = hero.position + Vector3.up * 1.4f;
            HorrorInteractable best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (var target in targets)
            {
                if (target == null || target.kind == HorrorInteraction.Sign || !target.IsAvailable) continue;
                var delta = target.InteractionPoint - origin;
                float distance = delta.magnitude;
                if (distance > reach) continue;
                var flat = Vector3.ProjectOnPlane(delta, Vector3.up);
                if (flat.sqrMagnitude > .001f && Vector3.Dot(hero.forward, flat.normalized) < Mathf.Cos(halfAngle * Mathf.Deg2Rad)) continue;
                if (!HasLineOfSight(origin, target)) continue;
                // Small hysteresis keeps adjacent objects from flickering as the hero turns.
                float score = distance - (target == previous ? .15f : 0);
                if (score >= bestDistance) continue;
                bestDistance = score; best = target;
            }
            return best;
        }

        public static HorrorInteractable FindSign(Transform hero, Camera camera, HorrorInteractable[] targets)
        {
            if (hero == null || camera == null || targets == null) return null;
            HorrorInteractable best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var target in targets)
            {
                if (target == null || target.kind != HorrorInteraction.Sign || !target.IsAvailable) continue;
                if (Vector3.Distance(hero.position + Vector3.up * 1.4f, target.InteractionPoint) > 4.7f) continue;
                var delta = target.InteractionPoint - camera.transform.position;
                if (delta.sqrMagnitude < .001f) continue;
                float alignment = Vector3.Dot(camera.transform.forward, delta.normalized);
                // Read signs by looking generally toward them; no pixel-perfect crosshair required.
                if (alignment < Mathf.Cos(22 * Mathf.Deg2Rad)) continue;
                if (!HasLineOfSight(camera.transform.position, target) || !HasLineOfSight(hero.position + Vector3.up * 1.4f, target)) continue;
                float score = alignment - delta.magnitude * .005f;
                if (score <= bestScore) continue;
                bestScore = score; best = target;
            }
            return best;
        }

        public static bool HasLineOfSight(Vector3 origin, HorrorInteractable target)
        {
            var delta = target.InteractionPoint - origin;
            if (delta.sqrMagnitude < .001f) return true;
            if (!Physics.Raycast(origin, delta.normalized, out var hit, delta.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<HorrorInteractable>() == target;
        }
    }
}
