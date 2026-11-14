using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RagdollFootball.Field.EditorTools
{
    /// <summary>
    /// Renders preview images of the soccer field from fixed viewpoints, through the scene's main camera.
    /// Batch mode (needs a GPU, so no -nographics):
    ///   Unity -batchmode -quit -projectPath &lt;project&gt; -executeMethod
    ///   RagdollFootball.Field.EditorTools.SoccerFieldCapture.CaptureBatch [-captureOut &lt;dir&gt;] [-captureScene &lt;scene&gt;]
    /// BuildAndCaptureBatch regenerates the scene first.
    /// </summary>
    public static class SoccerFieldCapture
    {
        const int Width = 1920, Height = 1080;

        [MenuItem("Tools/Ragdoll Football/Capture Field Previews...", priority = 20)]
        static void CaptureFromMenu()
        {
            string dir = EditorUtility.SaveFolderPanel("Save field previews", "", "FieldPreviews");
            if (string.IsNullOrEmpty(dir)) return;
            CaptureViews(dir);
            EditorUtility.RevealInFinder(dir);
        }

        public static void CaptureBatch()
        {
            EditorSceneManager.OpenScene(GetArg("-captureScene") ?? SoccerFieldBuilder.ScenePath, OpenSceneMode.Single);
            CaptureViews(OutputDirectory());
        }

        public static void BuildAndCaptureBatch()
        {
            SoccerFieldBuilder.CreateScene();
            CaptureViews(OutputDirectory());
        }

        static string OutputDirectory() =>
            GetArg("-captureOut") ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs", "FieldPreviews");

        /// <summary>Renders every preview of the first soccer field in the active scene into a folder.</summary>
        public static void CaptureViews(string outDir)
        {
            Directory.CreateDirectory(outDir);
            SoccerField field = FindField() ?? throw new InvalidOperationException("No SoccerField in the active scene.");
            Camera cam = Camera.main ?? throw new InvalidOperationException("The scene needs a camera tagged MainCamera.");

            Transform ft = field.transform;
            float L2 = field.HalfLength, W2 = field.HalfWidth;
            float halfGrassX = L2 + field.EffectiveEndRunoff, halfGrassZ = W2 + field.sideRunoff;

            var saved = new CameraState(cam);
            try
            {
                // Right after a scene loads in batch mode the first frames can come out washed out
                // while the GPU state settles, so render a few throwaway frames first.
                for (int i = 0; i < 3; i++) Render(cam, null, null);

                Render(cam, outDir, "01_overview");

                TopDown(cam, ft, outDir, "02_top_down", Vector3.zero,
                    Mathf.Max(halfGrassZ, halfGrassX * Height / Width) + 2f);
                TopDown(cam, ft, outDir, "03_penalty_area_top", new Vector3(L2 - 11f, 0f, 0f), 13f);
                TopDown(cam, ft, outDir, "04_center_circle_top", Vector3.zero, 10.5f);
                TopDown(cam, ft, outDir, "05_corner_top", new Vector3(L2 - 4f, 0f, W2 - 4f), 7f);

                Perspective(cam, ft, outDir, "06_goal_front", new Vector3(L2 - 16f, 3.2f, 3f), new Vector3(L2, 1.2f, 0f), 40f);
                Perspective(cam, ft, outDir, "07_goal_side", new Vector3(L2 - 4f, 2.2f, -10f), new Vector3(L2 + 0.8f, 1.2f, 0f), 50f);
                Perspective(cam, ft, outDir, "08_behind_goal", new Vector3(L2 + 6.5f, 3f, 4f), new Vector3(L2, 1f, 0f), 55f);
                Perspective(cam, ft, outDir, "09_corner_flag", new Vector3(L2 - 5f, 1.6f, W2 - 3.5f), new Vector3(L2, 0.8f, W2), 50f);
                Perspective(cam, ft, outDir, "10_behind_goal_wide", new Vector3(L2 + 7f, 14f, 0f), new Vector3(-10f, 0f, 0f), 60f);
            }
            finally
            {
                saved.Restore(cam);
            }

            Debug.Log($"[SoccerField] Previews written to {outDir}");
        }

        static void TopDown(Camera cam, Transform field, string dir, string name, Vector3 localCenter, float halfHeight)
        {
            cam.orthographic = true;
            cam.orthographicSize = halfHeight;
            cam.transform.position = field.TransformPoint(localCenter + Vector3.up * 200f);
            cam.transform.rotation = field.rotation * Quaternion.Euler(90f, 0f, 0f); // +X right, +Z up
            Render(cam, dir, name);
        }

        static void Perspective(Camera cam, Transform field, string dir, string name, Vector3 localPosition, Vector3 localTarget, float fov)
        {
            cam.orthographic = false;
            cam.fieldOfView = fov;
            cam.transform.position = field.TransformPoint(localPosition);
            cam.transform.LookAt(field.TransformPoint(localTarget), field.up);
            Render(cam, dir, name);
        }

        static void Render(Camera cam, string dir, string name)
        {
            // URP takes the MSAA sample count from the target texture, so give it the pipeline's
            // setting to match what players see; the multisampled image is resolved by the Blit.
            int msaa = UniversalRenderPipeline.asset != null
                ? Mathf.Max(1, UniversalRenderPipeline.asset.msaaSampleCount)
                : Mathf.Max(1, QualitySettings.antiAliasing);
            RenderTexture big = RenderTexture.GetTemporary(Width, Height, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, msaa);
            RenderTexture small = RenderTexture.GetTemporary(Width, Height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = cam.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                cam.aspect = (float)Width / Height;
                var request = new RenderPipeline.StandardRequest { destination = big };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = big;
                    cam.Render();
                }

                Graphics.Blit(big, small); // MSAA resolve
                RenderTexture.active = small;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                if (dir != null) File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(big);
                RenderTexture.ReleaseTemporary(small);
                Object.DestroyImmediate(image);
            }
        }

        static SoccerField FindField()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var field = root.GetComponentInChildren<SoccerField>(true);
                if (field != null) return field;
            }
            return null;
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        readonly struct CameraState
        {
            readonly Vector3 position;
            readonly Quaternion rotation;
            readonly bool orthographic;
            readonly float orthographicSize, fieldOfView;

            public CameraState(Camera cam)
            {
                position = cam.transform.position;
                rotation = cam.transform.rotation;
                orthographic = cam.orthographic;
                orthographicSize = cam.orthographicSize;
                fieldOfView = cam.fieldOfView;
            }

            public void Restore(Camera cam)
            {
                cam.transform.SetPositionAndRotation(position, rotation);
                cam.orthographic = orthographic;
                cam.orthographicSize = orthographicSize;
                cam.fieldOfView = fieldOfView;
                cam.ResetAspect();
            }
        }
    }
}
