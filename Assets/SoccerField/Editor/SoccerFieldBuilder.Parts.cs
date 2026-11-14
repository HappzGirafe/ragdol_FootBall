using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RagdollFootball.Field.EditorTools
{
    public static partial class SoccerFieldBuilder
    {
        const float LineLift = 0.01f;         // markings sit 1 cm above the grass to avoid z-fighting
        const float NetCell = 0.12f;          // size of one net square
        const float FrameBar = 0.05f;         // diameter of the net support bars
        const float FlagPoleDiameter = 0.035f;
        const float CornerMarkLength = 0.3f;  // optional marks beside the corners (length not fixed by Law 1)
        const float GrassTile = 8f;           // metres per grass detail tile
        const float SurroundSize = 1000f;     // plain ground beyond the grass, out to where the haze hides it

        static readonly int[] Ends = { -1, 1 };

        /// <summary>Builds each part of one field under its generated root.</summary>
        sealed class Parts
        {
            readonly SoccerField f;
            readonly Transform root;
            Mesh netMesh; // both goals are built in goal space, so they share one net mesh

            readonly Material grassLight, grassDark, surround, lines, goalFrame, netFrame, net, flag, flagPole;
            readonly PhysicsMaterial grassPhysics, framePhysics, netPhysics, wallPhysics;

            public Parts(SoccerField field, Transform root)
            {
                f = field;
                this.root = root;

                Texture2D grassDetail = FieldAssets.GetGrassTexture();
                grassLight = FieldAssets.GetLitMaterial("Grass Light", f.grassLight, 0.15f, grassDetail);
                grassDark = FieldAssets.GetLitMaterial("Grass Dark", f.grassDark, 0.15f, grassDetail);
                surround = FieldAssets.GetLitMaterial("Surround", new Color(0.30f, 0.34f, 0.29f), 0.05f, grassDetail);
                lines = FieldAssets.GetLitMaterial("Field Lines", f.lineColor, 0.1f);
                goalFrame = FieldAssets.GetLitMaterial("Goal Frame", Color.white, 0.6f);
                netFrame = FieldAssets.GetLitMaterial("Net Frame", new Color(0.78f, 0.78f, 0.78f), 0.4f);
                net = FieldAssets.GetLitMaterial("Goal Net", Color.white, 0f, FieldAssets.GetNetTexture());
                FieldAssets.MakeTransparent(net);
                flag = FieldAssets.GetLitMaterial("Corner Flag", new Color(1f, 0.42f, 0.08f), 0.2f);
                flagPole = FieldAssets.GetLitMaterial("Corner Flag Pole", new Color(0.95f, 0.95f, 0.92f), 0.5f);

                grassPhysics = FieldAssets.GetPhysicsMaterial("Grass", 0.6f, 0.7f, 0.3f,
                    PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
                framePhysics = FieldAssets.GetPhysicsMaterial("Goal Frame", 0.3f, 0.3f, 0.6f,
                    PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Maximum);
                netPhysics = FieldAssets.GetPhysicsMaterial("Goal Net", 0.9f, 0.9f, 0f,
                    PhysicsMaterialCombine.Maximum, PhysicsMaterialCombine.Minimum);
                wallPhysics = FieldAssets.GetPhysicsMaterial("Boundary Wall", 0.2f, 0.2f, 0.4f,
                    PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
            }

            float GrassHalfX => f.HalfLength + f.EffectiveEndRunoff;
            float GrassHalfZ => f.HalfWidth + f.sideRunoff;

            public void BuildGround()
            {
                float halfX = GrassHalfX, halfZ = GrassHalfZ;

                // Mowing stripes over all the grass, lined up with the field of play.
                var grass = new FieldMeshBuilder(2, GrassTile);
                float stripe = f.length / Mathf.Max(1, f.stripeCount);
                int first = Mathf.FloorToInt((f.HalfLength - halfX) / stripe);
                int last = Mathf.CeilToInt((f.HalfLength + halfX) / stripe);
                for (int k = first; k < last; k++)
                {
                    float x0 = Mathf.Max(-halfX, -f.HalfLength + k * stripe);
                    float x1 = Mathf.Min(halfX, -f.HalfLength + (k + 1) * stripe);
                    grass.AddRect(x0, -halfZ, x1, halfZ, 0f, ((k % 2) + 2) % 2);
                }
                MeshObject("Grass", root, WriteMesh(grass, "Grass"), new[] { grassLight, grassDark });

                // Plain ground round the grass so the pitch does not float in the sky.
                var ring = new FieldMeshBuilder(1, GrassTile);
                float ox = halfX + SurroundSize, oz = halfZ + SurroundSize;
                ring.AddRect(-ox, -oz, ox, -halfZ, 0f);
                ring.AddRect(-ox, halfZ, ox, oz, 0f);
                ring.AddRect(-ox, -halfZ, -halfX, halfZ, 0f);
                ring.AddRect(halfX, -halfZ, ox, halfZ, 0f);
                MeshObject("Surround", root, WriteMesh(ring, "Surround"), new[] { surround });

                // One collider under the grass and the surround; its top face is the ground plane.
                AddBox(Child("Ground Collider", root), new Vector3(0f, -0.5f, 0f), new Vector3(2f * ox, 1f, 2f * oz), grassPhysics);
            }

            public void BuildMarkings()
            {
                float L2 = f.HalfLength, W2 = f.HalfWidth, w = f.lineWidth, y = LineLift;
                const float R = SoccerField.CenterCircleRadius;
                var b = new FieldMeshBuilder();

                // Touchlines run the full length; goal lines fill the gap between them.
                b.AddRect(-L2, W2 - w, L2, W2, y);
                b.AddRect(-L2, -W2, L2, -W2 + w, y);
                b.AddRect(L2 - w, -W2 + w, L2, W2 - w, y);
                b.AddRect(-L2, -W2 + w, -L2 + w, W2 - w, y);

                // Halfway line, centre circle and centre mark.
                b.AddRect(-w * 0.5f, -W2 + w, w * 0.5f, W2 - w, y);
                b.AddArcBand(Vector2.zero, R - w, R, 0f, 2f * Mathf.PI, 180, y);
                b.AddDisc(Vector2.zero, f.spotDiameter * 0.5f, 32, y);

                float halfGoal = SoccerField.GoalWidth * 0.5f;
                foreach (int end in Ends)
                {
                    float s = end;
                    AddAreaOutline(b, s, SoccerField.GoalAreaSize, halfGoal + SoccerField.GoalAreaSize);
                    AddAreaOutline(b, s, SoccerField.PenaltyAreaSize, halfGoal + SoccerField.PenaltyAreaSize);

                    var mark = new Vector2(s * (L2 - SoccerField.PenaltyMarkDistance), 0f);
                    b.AddDisc(mark, f.spotDiameter * 0.5f, 32, y);

                    // Penalty arc: the part of the 9.15 m circle round the mark that lies outside the
                    // penalty area. Both edges of the band end exactly on the area's outer edge.
                    float toArea = SoccerField.PenaltyAreaSize - SoccerField.PenaltyMarkDistance;
                    float facing = s > 0f ? Mathf.PI : 0f; // towards the halfway line
                    float outerHalf = Mathf.Acos(toArea / R);
                    float innerHalf = Mathf.Acos(toArea / (R - w));
                    b.AddBand(
                        t => mark + FieldMeshBuilder.Polar(R - w, facing + Mathf.Lerp(-innerHalf, innerHalf, t)),
                        t => mark + FieldMeshBuilder.Polar(R, facing + Mathf.Lerp(-outerHalf, outerHalf, t)),
                        64, y);
                }

                // Corner arcs, and the optional marks off the field 9.15 m beyond them.
                foreach (int sx in Ends)
                foreach (int sz in Ends)
                {
                    var corner = new Vector2(sx * L2, sz * W2);
                    float inward = Mathf.Atan2(-sz, -sx);
                    b.AddArcBand(corner, SoccerField.CornerArcRadius - w, SoccerField.CornerArcRadius,
                        inward - Mathf.PI * 0.25f, inward + Mathf.PI * 0.25f, 24, y);

                    if (!f.cornerDistanceMarks) continue;
                    float d = SoccerField.CornerArcRadius + SoccerField.CornerDistanceMark; // from the corner
                    // Behind the goal line, at right angles to it.
                    b.AddRect(sx * L2, sz * (W2 - d), sx * (L2 + CornerMarkLength), sz * (W2 - d - w), y);
                    // Outside the touchline, at right angles to it.
                    b.AddRect(sx * (L2 - d), sz * W2, sx * (L2 - d - w), sz * (W2 + CornerMarkLength), y);
                }

                MeshObject("Markings", root, WriteMesh(b, "Markings"), new[] { lines });
            }

            // Goal area or penalty area. Lines belong to the area they bound, so the outer edges of
            // the lines sit `depth` from the goal line and `halfWidth` from the long axis.
            void AddAreaOutline(FieldMeshBuilder b, float s, float depth, float halfWidth)
            {
                float L2 = f.HalfLength, w = f.lineWidth, y = LineLift;
                float front = L2 - depth;
                b.AddRect(s * front, -halfWidth, s * (front + w), halfWidth, y);
                b.AddRect(s * (front + w), halfWidth - w, s * (L2 - w), halfWidth, y);
                b.AddRect(s * (front + w), -halfWidth, s * (L2 - w), -halfWidth + w, y);
            }

            public void BuildGoal(int end)
            {
                float t = f.PostThickness;
                float halfMouth = SoccerField.GoalWidth * 0.5f;
                float postZ = halfMouth + t * 0.5f;      // post centres
                float top = SoccerField.GoalHeight + t;  // top of the crossbar
                float back = t * 0.5f + f.goalDepth;     // back of the net

                // Goal space: origin on the centre of the goal line midway between the posts,
                // +X pointing away from the field, so both goals share one net mesh.
                Transform goal = Child(end < 0 ? "Goal (-X)" : "Goal (+X)", root).transform;
                goal.localPosition = new Vector3(SoccerField.EndSign(end) * (f.HalfLength - t * 0.5f), 0f, 0f);
                goal.localRotation = Quaternion.Euler(0f, end < 0 ? 180f : 0f, 0f);

                // Posts stand on the goal line; the crossbar's lower edge is 2.44 m up.
                foreach (float z in new[] { -postZ, postZ })
                    Cylinder("Post", goal, new Vector3(0f, top * 0.5f, z), Quaternion.identity, t, top, goalFrame, framePhysics);
                // The crossbar ends on the post axes, so its flat end caps stay hidden inside the round posts.
                Cylinder("Crossbar", goal, new Vector3(0f, SoccerField.GoalHeight + t * 0.5f, 0f),
                    Quaternion.Euler(90f, 0f, 0f), t, 2f * postZ, goalFrame, framePhysics);

                // Net support frame (visual only, so players do not trip on the ground bars). The top
                // bars sit flush with the top of the crossbar, so their front ends vanish into it.
                Transform frame = Child("Net Frame", goal).transform;
                float topBarY = top - FrameBar * 0.5f;
                foreach (float z in new[] { -postZ, postZ })
                {
                    Cylinder("Back Upright", frame, new Vector3(back, top * 0.5f, z), Quaternion.identity, FrameBar, top, netFrame, null);
                    Cylinder("Top Bar", frame, new Vector3(back * 0.5f, topBarY, z), Quaternion.Euler(0f, 0f, 90f), FrameBar, back, netFrame, null);
                    Cylinder("Ground Bar", frame, new Vector3((back + t * 0.5f) * 0.5f, FrameBar * 0.5f, z),
                        Quaternion.Euler(0f, 0f, 90f), FrameBar, back - t * 0.5f, netFrame, null);
                }
                Cylinder("Back Top Bar", frame, new Vector3(back, topBarY, 0f), Quaternion.Euler(90f, 0f, 0f), FrameBar, 2f * postZ, netFrame, null);
                Cylinder("Back Ground Bar", frame, new Vector3(back, FrameBar * 0.5f, 0f), Quaternion.Euler(90f, 0f, 0f), FrameBar, 2f * postZ, netFrame, null);

                if (netMesh == null) netMesh = WriteMesh(NetMesh(postZ, top, back), "Goal Net");
                MeshObject("Net", goal, netMesh, new[] { net });

                // Soft net colliders so the ball drops instead of bouncing back out.
                GameObject netColliders = Child("Net Colliders", goal);
                const float thick = 0.1f;
                AddBox(netColliders, new Vector3(back + thick * 0.5f, top * 0.5f, 0f), new Vector3(thick, top, 2f * postZ + thick), netPhysics);
                foreach (float z in new[] { -postZ, postZ })
                    AddBox(netColliders, new Vector3(back * 0.5f, top * 0.5f, z + Mathf.Sign(z) * thick * 0.5f), new Vector3(back, top, thick), netPhysics);
                // Thin roof slab just under the roof net and behind the crossbar: a ball that clips the
                // top of the bar bounces off the bar, and a ball rising into the roof stops at the
                // visible net. Thin is fine because the ball must use continuous collision detection.
                const float roofThick = 0.03f;
                float roofStart = t * 0.5f;
                AddBox(netColliders, new Vector3((roofStart + back + thick) * 0.5f, top - roofThick * 0.5f, 0f),
                    new Vector3(back + thick - roofStart, roofThick, 2f * (postZ + thick)), netPhysics);

                // Trigger behind the goal line that reports goals to the field.
                GameObject zoneObject = Child("Goal Zone", goal);
                zoneObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                var zoneBox = zoneObject.AddComponent<BoxCollider>();
                zoneBox.isTrigger = true;
                float lineEdge = t * 0.5f;
                zoneBox.center = new Vector3((lineEdge + back) * 0.5f, SoccerField.GoalHeight * 0.5f, 0f);
                zoneBox.size = new Vector3(back - lineEdge, SoccerField.GoalHeight, SoccerField.GoalWidth);
                zoneObject.AddComponent<GoalZone>().end = end;
            }

            static FieldMeshBuilder NetMesh(float postZ, float top, float back)
            {
                var b = new FieldMeshBuilder();
                float c = NetCell;
                // Back
                b.AddPanel(new Vector3(back, 0f, -postZ), new Vector3(back, top, -postZ), new Vector3(back, top, postZ), new Vector3(back, 0f, postZ),
                    new Vector2(-postZ / c, 0f), new Vector2(-postZ / c, top / c), new Vector2(postZ / c, top / c), new Vector2(postZ / c, 0f));
                // Sides
                foreach (float z in new[] { -postZ, postZ })
                    b.AddPanel(new Vector3(0f, 0f, z), new Vector3(0f, top, z), new Vector3(back, top, z), new Vector3(back, 0f, z),
                        new Vector2(0f, 0f), new Vector2(0f, top / c), new Vector2(back / c, top / c), new Vector2(back / c, 0f));
                // Roof
                b.AddPanel(new Vector3(0f, top, -postZ), new Vector3(back, top, -postZ), new Vector3(back, top, postZ), new Vector3(0f, top, postZ),
                    new Vector2(0f, -postZ / c), new Vector2(back / c, -postZ / c), new Vector2(back / c, postZ / c), new Vector2(0f, postZ / c));
                return b;
            }

            public void BuildCornerFlags()
            {
                Transform flags = Child("Corner Flags", root).transform;
                float h = SoccerField.CornerFlagHeight;
                foreach (int sx in Ends)
                foreach (int sz in Ends)
                {
                    // On the outer corner of the lines, which is also the centre of the corner arc (Law 1.7).
                    Transform corner = Child($"Corner Flag ({(sx > 0 ? "+" : "-")}X {(sz > 0 ? "+" : "-")}Z)", flags).transform;
                    corner.localPosition = new Vector3(sx * f.HalfLength, 0f, sz * f.HalfWidth);

                    // Flat-topped pole and a flag pointing away from the field. No colliders: real
                    // corner flags bend out of the way.
                    Cylinder("Pole", corner, new Vector3(0f, h * 0.5f, 0f), Quaternion.identity, FlagPoleDiameter, h, flagPole, null);
                    GameObject cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cloth.name = "Flag";
                    Object.DestroyImmediate(cloth.GetComponent<Collider>());
                    cloth.transform.SetParent(corner, false);
                    cloth.transform.localPosition = new Vector3(sx * (FlagPoleDiameter * 0.5f + 0.225f), h - 0.17f, 0f);
                    cloth.transform.localScale = new Vector3(0.45f, 0.3f, 0.01f);
                    cloth.GetComponent<MeshRenderer>().sharedMaterial = flag;
                }
            }

            public void BuildWalls()
            {
                GameObject walls = Child("Boundary Walls", root);
                walls.layer = LayerMask.NameToLayer("Ignore Raycast"); // camera and mouse rays pass through
                float halfX = GrassHalfX, halfZ = GrassHalfZ, h = f.wallHeight;
                const float thick = 1f;
                foreach (int s in Ends)
                {
                    AddBox(walls, new Vector3(s * (halfX + thick * 0.5f), h * 0.5f, 0f), new Vector3(thick, h, 2f * (halfZ + thick)), wallPhysics);
                    AddBox(walls, new Vector3(0f, h * 0.5f, s * (halfZ + thick * 0.5f)), new Vector3(2f * (halfX + thick), h, thick), wallPhysics);
                }
                // Ceiling, so high clearances cannot leave the arena over the walls.
                AddBox(walls, new Vector3(0f, h + thick * 0.5f, 0f), new Vector3(2f * (halfX + thick), thick, 2f * (halfZ + thick)), wallPhysics);
            }

            // Meshes are stored in the scene rather than as shared assets, so each scene and
            // each copy of a field owns its geometry and rebuilding one never changes another.
            static Mesh WriteMesh(FieldMeshBuilder builder, string name)
            {
                var mesh = new Mesh { name = name };
                builder.WriteTo(mesh);
                Undo.RegisterCreatedObjectUndo(mesh, FieldAssets.UndoName);
                return mesh;
            }

            static GameObject Child(string name, Transform parent)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                return go;
            }

            static void MeshObject(string name, Transform parent, Mesh mesh, Material[] materials)
            {
                GameObject go = Child(name, parent);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off; // flat surfaces and thin nets
                renderer.receiveShadows = true;
            }

            static void Cylinder(string name, Transform parent, Vector3 localPosition, Quaternion localRotation,
                float diameter, float length, Material material, PhysicsMaterial physics)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = name;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPosition;
                go.transform.localRotation = localRotation;
                go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter); // unit cylinder is 2 m tall
                go.GetComponent<MeshRenderer>().sharedMaterial = material;

                var collider = go.GetComponent<Collider>();
                if (physics == null) Object.DestroyImmediate(collider);
                else collider.sharedMaterial = physics;
            }

            static void AddBox(GameObject go, Vector3 center, Vector3 size, PhysicsMaterial physics)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = center;
                box.size = size;
                box.sharedMaterial = physics;
            }
        }
    }
}
