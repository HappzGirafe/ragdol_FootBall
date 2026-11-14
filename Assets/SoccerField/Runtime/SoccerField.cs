using System;
using UnityEngine;
using UnityEngine.Events;

namespace RagdollFootball.Field
{
    /// <summary>
    /// Root of a generated soccer field. Holds the field settings (IFAB Law 1 by default)
    /// and gives gameplay code the key points of the pitch.
    /// The field is centred on this transform: local X runs along the length (goal to goal),
    /// local Z along the width, and Y is up. All distances are in metres.
    /// Regenerate the geometry with the Rebuild button in the inspector or
    /// Tools > Ragdoll Football. The generated meshes are stored in the scene, so a prefab of a
    /// built field would lose them: to reuse the field elsewhere, copy the object into the other
    /// scene and rebuild it there.
    /// Ball requirements for goal detection are listed on <see cref="GoalZone"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SoccerField : MonoBehaviour
    {
        // Fixed by Law 1, whatever the size of the field.
        public const float GoalWidth = 7.32f;           // between the inner edges of the posts
        public const float GoalHeight = 2.44f;          // ground to the lower edge of the crossbar
        public const float GoalAreaSize = 5.5f;         // from the inside of each post, and into the field
        public const float PenaltyAreaSize = 16.5f;     // from the inside of each post, and into the field
        public const float PenaltyMarkDistance = 11f;   // from the goal line, midway between the posts
        public const float CenterCircleRadius = 9.15f;  // also the radius of the penalty arcs
        public const float CornerArcRadius = 1f;
        public const float CornerDistanceMark = 9.15f;  // optional marks off the field, measured from the corner arc
        public const float CornerFlagHeight = 1.5f;     // minimum flagpost height

        [Header("Field of play (Law 1)")]
        [Tooltip("Touchline length, measured to the outer edges of the goal lines. Law 1: 90-120 m; international matches 100-110 m.")]
        [Range(90f, 120f)] public float length = 105f;

        [Tooltip("Goal line length, measured to the outer edges of the touchlines. Law 1: 45-90 m; international matches 64-75 m.")]
        [Range(45f, 90f)] public float width = 68f;

        [Tooltip("Width of every line. Law 1: at most 12 cm. The goalposts and crossbar are as wide as the goal lines.")]
        [Range(0.05f, 0.12f)] public float lineWidth = 0.12f;

        [Tooltip("Diameter of the centre mark and the penalty marks (Law 1 does not fix it).")]
        [Range(0.1f, 0.3f)] public float spotDiameter = 0.22f;

        [Tooltip("Draw the optional marks off the field, 9.15 m from each corner arc.")]
        public bool cornerDistanceMarks = true;

        [Header("Goals")]
        [Tooltip("Depth of the goal nets behind the goal line (Law 1 does not fix it).")]
        [Range(1f, 3f)] public float goalDepth = 2f;

        [Header("Surroundings")]
        [Tooltip("Grass beyond each touchline.")]
        [Range(0f, 30f)] public float sideRunoff = 6f;

        [Tooltip("Grass beyond each goal line. Always at least 1 m more than the goal depth.")]
        [Range(2f, 30f)] public float endRunoff = 8f;

        [Tooltip("Invisible walls at the edge of the grass, with a ceiling on top, keep the ball and players in the arena.")]
        public bool boundaryWalls = true;

        [Tooltip("Height of the invisible walls and of the ceiling that closes the arena.")]
        [Range(5f, 100f)] public float wallHeight = 40f;

        [Header("Look")]
        [Tooltip("Number of mowing stripes between the goal lines. Use an even number to put the halfway line on a stripe edge.")]
        [Range(2, 40)] public int stripeCount = 18;

        public Color grassLight = new Color(0.40f, 0.66f, 0.26f);
        public Color grassDark = new Color(0.32f, 0.56f, 0.20f);
        public Color lineColor = new Color(0.96f, 0.96f, 0.96f);

        [Header("Events")]
        [Tooltip("Raised when a ball wholly crosses a goal line between the posts and under the crossbar. " +
                 "Passes the end that was scored in: -1 for the goal at -X, +1 for the goal at +X.")]
        public UnityEvent<int> onGoalScored = new UnityEvent<int>();

        /// <summary>Raised with the end that was scored in (-1 or +1) and the ball's collider.</summary>
        public event Action<int, Collider> GoalScored;

        public float HalfLength => length * 0.5f;
        public float HalfWidth => width * 0.5f;

        /// <summary>Goalposts and crossbar are as wide and deep as the goal lines (Law 1).</summary>
        public float PostThickness => lineWidth;

        /// <summary>Runoff behind the goal lines, never shorter than the nets plus 1 m.</summary>
        public float EffectiveEndRunoff => Mathf.Max(endRunoff, goalDepth + 1f);

        /// <summary>Centre mark in world space.</summary>
        public Vector3 CenterMark => transform.position;

        /// <summary>Midpoint between the goalposts, on the centre of the goal line, in world space.</summary>
        public Vector3 GoalCenter(int end) =>
            transform.TransformPoint(new Vector3(EndSign(end) * (HalfLength - lineWidth * 0.5f), 0f, 0f));

        /// <summary>Penalty mark in front of the goal at the given end, in world space.</summary>
        public Vector3 PenaltyMark(int end) =>
            transform.TransformPoint(new Vector3(EndSign(end) * (HalfLength - PenaltyMarkDistance), 0f, 0f));

        /// <summary>Law 1: touchline 90-120 m, goal line 45-90 m, and the touchline longer than the goal line.</summary>
        public bool MeetsLaw1Dimensions =>
            length > width && length >= 90f && length <= 120f && width >= 45f && width <= 90f;

        /// <summary>True when a world-space point is inside the field of play (lines included).</summary>
        public bool IsInFieldOfPlay(Vector3 worldPoint)
        {
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(p.x) <= HalfLength && Mathf.Abs(p.z) <= HalfWidth;
        }

        /// <summary>-1 for the goal at -X, +1 for the goal at +X.</summary>
        public static float EndSign(int end) => end < 0 ? -1f : 1f;

        internal void ReportGoal(int end, Collider ball)
        {
            GoalScored?.Invoke(end, ball);
            onGoalScored?.Invoke(end);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(length, 0f, width));

            Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
            float runoffX = EffectiveEndRunoff;
            Gizmos.DrawWireCube(new Vector3(0f, wallHeight * 0.5f, 0f),
                new Vector3(length + 2f * runoffX, wallHeight, width + 2f * sideRunoff));
        }
    }
}
