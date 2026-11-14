using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RagdollFootball.Field.EditorTools
{
    /// <summary>
    /// Generates a soccer field (grass, Law 1 markings, goals with nets, corner flags,
    /// colliders) under a <see cref="SoccerField"/> component. Generated meshes are stored in
    /// the scene, so every scene and every copy of a field owns its geometry.
    /// Batch mode: Unity -batchmode -quit -projectPath &lt;project&gt;
    ///   -executeMethod RagdollFootball.Field.EditorTools.SoccerFieldBuilder.CreateSceneBatch
    /// </summary>
    public static partial class SoccerFieldBuilder
    {
        public const string ScenePath = "Assets/Scenes/SoccerField.unity";
        public const string GeneratedChildName = "Generated";
        const string Menu = "Tools/Ragdoll Football/";

        // Distance haze. The procedural sky brightens its ground colour, so it is set darker than
        // the haze to match it on screen; together they hide the far edge of the ground.
        static readonly Color HazeColor = new Color(0.62f, 0.68f, 0.74f);
        static readonly Color SkyGroundColor = new Color(0.52f, 0.57f, 0.62f);

        [MenuItem(Menu + "Create Soccer Field Scene", priority = 0)]
        static void CreateSceneFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Create Soccer Field Scene",
                    $"{ScenePath} already exists. Replace it with a freshly generated scene?", "Replace", "Cancel"))
                return;
            CreateScene();
        }

        [MenuItem(Menu + "Rebuild Soccer Fields In Open Scenes", priority = 1)]
        static void RebuildOpenScenesFromMenu()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (SoccerField field in root.GetComponentsInChildren<SoccerField>(true))
                    if (Build(field)) count++;
            }
            Debug.Log($"[SoccerField] Rebuilt {count} field(s).");
        }

        [MenuItem(Menu + "Create Soccer Field Scene", true)]
        [MenuItem(Menu + "Rebuild Soccer Fields In Open Scenes", true)]
        static bool NotInPlayMode() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Entry point for -executeMethod.</summary>
        public static void CreateSceneBatch() => CreateScene();

        /// <summary>Creates (or replaces) the soccer field scene and leaves it open.</summary>
        public static void CreateScene()
        {
            FieldAssets.EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EnsureTag("Ball");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment();
            CreateSun();
            var field = new GameObject("Soccer Field").AddComponent<SoccerField>();
            CreateMainCamera(field);
            Build(field);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SoccerField] Created {ScenePath}");
        }

        /// <summary>
        /// Replaces the field's generated child with freshly built geometry, as one undo step.
        /// Returns false when the field cannot be rebuilt where it is.
        /// </summary>
        public static bool Build(SoccerField field)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SoccerField] Exit Play mode before rebuilding the field.", field);
                return false;
            }
            if (EditorUtility.IsPersistent(field) || PrefabStageUtility.GetPrefabStage(field.gameObject) != null)
            {
                // The generated meshes live in the scene and cannot be saved into a prefab.
                Debug.LogError("[SoccerField] Fields cannot be built inside prefabs. Rebuild the field in a scene.", field);
                return false;
            }
            if (PrefabUtility.IsPartOfPrefabInstance(field.gameObject))
            {
                Debug.LogError("[SoccerField] Unpack the prefab instance before rebuilding the field.", field);
                return false;
            }
            if (!field.MeetsLaw1Dimensions)
            {
                Debug.LogWarning($"[SoccerField] {field.length} x {field.width} m is outside Law 1 (touchline 90-120 m, " +
                                 "goal line 45-90 m, touchline longer than the goal line). Building it anyway.", field);
            }

            Undo.SetCurrentGroupName(FieldAssets.UndoName);
            int undoGroup = Undo.GetCurrentGroup();

            Transform old = field.transform.Find(GeneratedChildName);
            if (old != null)
            {
                Mesh[] oldMeshes = old.GetComponentsInChildren<MeshFilter>(true)
                    .Select(filter => filter.sharedMesh)
                    .Where(mesh => mesh != null && !EditorUtility.IsPersistent(mesh))
                    .Distinct()
                    .ToArray();
                Undo.DestroyObjectImmediate(old.gameObject);

                // Destroy the old meshes in the same undo step, unless a copy of the field
                // (Ctrl+D shares meshes) still uses them.
                foreach (Mesh mesh in oldMeshes)
                    if (!IsUsedInOpenScenes(mesh)) Undo.DestroyObjectImmediate(mesh);
            }

            var root = new GameObject(GeneratedChildName).transform;
            root.SetParent(field.transform, false);

            var parts = new Parts(field, root);
            parts.BuildGround();
            parts.BuildMarkings();
            parts.BuildGoal(-1);
            parts.BuildGoal(+1);
            parts.BuildCornerFlags();
            if (field.boundaryWalls) parts.BuildWalls();

            Undo.RegisterCreatedObjectUndo(root.gameObject, FieldAssets.UndoName);
            Undo.CollapseUndoOperations(undoGroup);

            AssetDatabase.SaveAssets();
            if (field.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
            return true;
        }

        static bool IsUsedInOpenScenes(Mesh mesh)
        {
            foreach (MeshFilter filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
                if (filter.sharedMesh == mesh && !EditorUtility.IsPersistent(filter)) return true;
            return false;
        }

        static void SetupEnvironment()
        {
            RenderSettings.skybox = FieldAssets.GetSkyboxMaterial(SkyGroundColor);

            // Gradient ambient light works without baking lighting first.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.60f, 0.70f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.47f, 0.52f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.27f, 0.20f);

            // Haze only well beyond the far goal, so the pitch itself stays crisp.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HazeColor;
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 900f;
        }

        static void CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.90f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
        }

        static void CreateMainCamera(SoccerField field)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            // The camera sits outside the invisible arena walls, which are on Ignore Raycast.
            // Leaving that layer out of the culling mask also keeps OnMouse events and UI physics
            // raycasts from this camera from stopping at the walls. Nothing on it renders.
            cam.cullingMask &= ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            go.AddComponent<AudioListener>();

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            // Broadcast-style view from above one touchline that shows the whole field of play.
            Transform t = field.transform;
            go.transform.position = t.TransformPoint(new Vector3(0f, 38f, -(field.HalfWidth + 56f)));
            go.transform.LookAt(t.TransformPoint(new Vector3(0f, 0f, -2f)), t.up);
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureTag(string tag)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
