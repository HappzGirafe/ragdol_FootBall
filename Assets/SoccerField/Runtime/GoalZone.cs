using System.Collections.Generic;
using UnityEngine;

namespace RagdollFootball.Field
{
    /// <summary>
    /// Trigger volume inside a goal, behind the goal line. Reports a goal to the parent
    /// <see cref="SoccerField"/> once a ball has wholly crossed the goal line between the
    /// posts and under the crossbar (Law 10).
    /// The ball needs: a Rigidbody with Continuous collision detection (with Discrete, hard
    /// shots pass straight through the 12 cm posts and the net), a SphereCollider, and the tag
    /// in <see cref="ballTag"/>. Give it a physics material whose bounce combine is Average or
    /// Minimum, so the soft net can absorb it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalZone : MonoBehaviour
    {
        [Tooltip("End of the field this goal is at: -1 for the goal at -X, +1 for the goal at +X.")]
        public int end = 1;

        [Tooltip("Tag that marks the ball. Leave empty to treat any rigidbody as a ball.")]
        public string ballTag = "Ball";

        SoccerField field;
        BoxCollider zone;

        // Balls already reported, keyed by Rigidbody (or by collider when there is none), with
        // the collider used to measure them. A ball is forgotten once it is back in front of the
        // goal line, has left the zone, or has been disabled or destroyed, so it can score again.
        readonly Dictionary<Object, Collider> scored = new Dictionary<Object, Collider>();
        readonly List<Object> stale = new List<Object>();
        bool warnedDiscrete;

        void Awake()
        {
            field = GetComponentInParent<SoccerField>();
            zone = GetComponent<BoxCollider>();
            zone.isTrigger = true;
        }

        // No trigger exit arrives for a collider that is disabled, deactivated or teleported
        // away, so the record is pruned here rather than in OnTriggerExit. A ball that leaves
        // the goal is re-armed automatically; there is nothing to reset at kick-off.
        void FixedUpdate()
        {
            // With the trigger collider switched off nothing can score, so keep the record as is.
            if (scored.Count == 0 || !zone.enabled) return;

            stale.Clear();
            foreach (KeyValuePair<Object, Collider> entry in scored)
            {
                Collider c = entry.Value;
                if (entry.Key == null || c == null || !c.enabled || !c.gameObject.activeInHierarchy ||
                    !OverlapsZone(c) || DistancePastGoalLine(c) < 0f)
                    stale.Add(entry.Key);
            }
            foreach (Object key in stale) scored.Remove(key);
        }

        // Exact test against the zone box at any field rotation (world-space bounds are not).
        bool OverlapsZone(Collider ball)
        {
            Vector3 p = ball.bounds.center;
            float r = BallRadius(ball);
            return (zone.ClosestPoint(p) - p).sqrMagnitude <= r * r;
        }

        void OnTriggerStay(Collider other)
        {
            // Trigger messages also reach disabled behaviours; a disabled zone does not score.
            if (!isActiveAndEnabled || field == null || other.isTrigger || !IsBall(other)) return;

            WarnIfDiscrete(other.attachedRigidbody);
            Object key = other.attachedRigidbody != null ? (Object)other.attachedRigidbody : other;
            Collider ball = MainCollider(other);
            float pastLine = DistancePastGoalLine(ball);

            if (scored.ContainsKey(key))
            {
                // More than half the ball is back in front of the line: it may score again.
                if (pastLine < 0f) scored.Remove(key);
                return;
            }

            if (pastLine < BallRadius(ball) || !IsBetweenPostsAndUnderBar(ball)) return;

            scored[key] = ball;
            field.ReportGoal(end, ball);
        }

        bool IsBall(Collider other)
        {
            if (string.IsNullOrEmpty(ballTag)) return other.attachedRigidbody != null;
            if (other.CompareTag(ballTag)) return true;
            return other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(ballTag);
        }

        // A ball built from several colliders is measured by the sphere on its body.
        static Collider MainCollider(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null || other is SphereCollider) return other;
            SphereCollider sphere = body.GetComponent<SphereCollider>();
            return sphere != null && !sphere.isTrigger ? sphere : other;
        }

        // Distance of the ball's centre past the outer edge of the goal line, in world units.
        float DistancePastGoalLine(Collider ball)
        {
            Transform t = field.transform;
            float side = SoccerField.EndSign(end);
            Vector3 lineEdge = t.TransformPoint(new Vector3(side * field.HalfLength, 0f, 0f));
            return Vector3.Dot(ball.bounds.center - lineEdge, t.right * side);
        }

        // Field-local, so a moved or rotated field works too.
        bool IsBetweenPostsAndUnderBar(Collider ball)
        {
            Vector3 local = field.transform.InverseTransformPoint(ball.bounds.center);
            return Mathf.Abs(local.z) < SoccerField.GoalWidth * 0.5f && local.y < SoccerField.GoalHeight;
        }

        static float BallRadius(Collider ball)
        {
            if (ball is SphereCollider sphere)
            {
                Vector3 s = sphere.transform.lossyScale;
                return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            }

            Vector3 e = ball.bounds.extents;
            return Mathf.Max(e.x, Mathf.Max(e.y, e.z));
        }

        void WarnIfDiscrete(Rigidbody body)
        {
            if (warnedDiscrete || body == null || body.collisionDetectionMode != CollisionDetectionMode.Discrete) return;
            warnedDiscrete = true;
            Debug.LogWarning($"[SoccerField] Ball '{body.name}' uses Discrete collision detection. " +
                             "Set it to Continuous so hard shots cannot pass through the posts and the net.", body);
        }
    }
}
