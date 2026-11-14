using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RagdollFootball.Field.EditorTools
{
    /// <summary>Creates or reuses the assets the field generator needs.</summary>
    internal static class FieldAssets
    {
        public const string RootFolder = "Assets/SoccerField";
        public const string MaterialsFolder = RootFolder + "/Materials";
        public const string TexturesFolder = RootFolder + "/Textures";
        public const string PhysicsFolder = RootFolder + "/Physics";
        public const string UndoName = "Rebuild Soccer Field";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Shader LitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            return shader;
        }

        /// <summary>Lit material asset. Colour, smoothness and texture are reapplied on every build.</summary>
        public static Material GetLitMaterial(string name, Color color, float smoothness, Texture texture = null)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(LitShader()) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                Undo.RecordObject(mat, UndoName);
            }

            mat.color = color; // _BaseColor in URP, _Color in the built-in pipeline
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (texture != null) mat.mainTexture = texture; // _BaseMap in URP, _MainTex in built-in
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Switches a URP Lit material to straight alpha blending (Surface Transparent, Blend
        /// Alpha, Preserve Specular off), setting exactly the state URP's material validation
        /// derives from those options so URP never rewrites it. Used for the nets: cords thinner
        /// than a pixel fade to a faint haze instead of breaking up, as they do with alpha
        /// clipping. A uniformly white net looks the same in any draw order.
        /// </summary>
        public static void MakeTransparent(Material mat)
        {
            Undo.RecordObject(mat, UndoName);
            if (!mat.HasProperty("_Surface")) return; // not URP Lit

            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha
            mat.SetFloat("_BlendModePreserveSpecular", 0f);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_AlphaToMask", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("MotionVectors", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
        }

        public static PhysicsMaterial GetPhysicsMaterial(string name, float dynamicFriction, float staticFriction,
            float bounciness, PhysicsMaterialCombine frictionCombine, PhysicsMaterialCombine bounceCombine)
        {
            EnsureFolder(PhysicsFolder);
            string path = $"{PhysicsFolder}/{name}.asset";
            PhysicsMaterial pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm == null)
            {
                pm = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(pm, path);
            }
            else
            {
                Undo.RecordObject(pm, UndoName);
            }

            pm.dynamicFriction = dynamicFriction;
            pm.staticFriction = staticFriction;
            pm.bounciness = bounciness;
            pm.frictionCombine = frictionCombine;
            pm.bounceCombine = bounceCombine;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        /// <summary>
        /// Procedural sky whose colour below the horizon matches the distance fog, so the far
        /// edge of the ground blends into the sky instead of ending in a hard line.
        /// </summary>
        public static Material GetSkyboxMaterial(Color groundColor)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/Sky.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Skybox/Procedural");
                if (shader == null) return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
                mat = new Material(shader) { name = "Sky" };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                Undo.RecordObject(mat, UndoName);
            }

            if (mat.HasProperty("_GroundColor")) mat.SetColor("_GroundColor", groundColor);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tileable grass detail, multiplied with the stripe colours.</summary>
        public static Texture2D GetGrassTexture() =>
            GetGeneratedTexture("GrassDetail.png", () => GenerateGrassDetail(512), importer =>
            {
                importer.anisoLevel = 8;
                importer.alphaSource = TextureImporterAlphaSource.None;
            });

        /// <summary>One square of goal net with cords on two edges; tiles into a grid.</summary>
        public static Texture2D GetNetTexture() =>
            GetGeneratedTexture("NetTile.png", () => GenerateNetTile(64), importer =>
            {
                importer.anisoLevel = 4;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = false; // blended net: distant cords average into a light haze
            });

        static Texture2D GetGeneratedTexture(string fileName, System.Func<Texture2D> generate,
            System.Action<TextureImporter> configure)
        {
            EnsureFolder(TexturesFolder);
            string path = $"{TexturesFolder}/{fileName}";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            Texture2D tex = generate();
            File.WriteAllBytes(ToFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            configure(importer);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string ToFullPath(string assetPath) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);

        static Texture2D GenerateGrassDetail(int size)
        {
            var rng = new System.Random(20261113);
            int[] periods = { 4, 8, 16, 32, 64 };              // noise cells across the tile
            float[] weights = { 0.35f, 0.25f, 0.18f, 0.12f, 0.10f }; // sum to 1
            var lattices = new float[periods.Length][];
            for (int o = 0; o < periods.Length; o++)
            {
                int p = periods[o];
                lattices[o] = new float[p * p];
                for (int i = 0; i < p * p; i++) lattices[o][i] = (float)rng.NextDouble();
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = 0f;
                for (int o = 0; o < periods.Length; o++)
                    n += PeriodicValueNoise(lattices[o], periods[o], (float)x / size, (float)y / size) * weights[o];

                float grain = (float)rng.NextDouble();
                float v = Mathf.Lerp(0.84f, 1f, n * 0.7f + grain * 0.3f);
                pixels[y * size + x] = new Color(v * (0.97f + 0.05f * n), v, v * 0.94f);
            }

            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // Smoothly interpolated value noise that wraps at the tile edges.
        static float PeriodicValueNoise(float[] lattice, int period, float u, float v)
        {
            float fx = u * period, fy = v * period;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            x0 %= period;
            y0 %= period;
            float a = Mathf.Lerp(lattice[y0 * period + x0], lattice[y0 * period + x1], tx);
            float b = Mathf.Lerp(lattice[y1 * period + x0], lattice[y1 * period + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static Texture2D GenerateNetTile(int size)
        {
            int cord = Mathf.Max(2, size / 12); // about 1 cm cords on a 12 cm mesh
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool isCord = x < cord || y < cord;
                pixels[y * size + x] = new Color32(255, 255, 255, isCord ? (byte)255 : (byte)0);
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
