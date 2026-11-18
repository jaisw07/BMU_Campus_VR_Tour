using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Campus Terrain: 8 PBR Terrain Layers (ambientCG CC0) painted procedurally from the road/path layout, building footprints,
    /// tree positions and the campus boundary, plus Terrain detail grass (mown lawn tufts, tall dry tufts, weeds, flowers) with wind.
    /// Layers: 0 lawn A, 1 lawn B, 2 dry grass, 3 patchy grass, 4 farmland/sand, 5 dirt strip, 6 bare soil, 7 gravel.
    /// </summary>
    public static class RealismTerrain
    {
        public const string DataPath = "Assets/Art/Terrain/Campus_TerrainData.asset";
        public const string VegDir = "Assets/Art/Textures/Vegetation";

        // Terrain covers the campus plus >= 150 m on every side. Origin is the south-west corner (min x, min z).
        public static readonly Vector3 Origin = new Vector3(-1060f, 0f, -560f);
        public static readonly Vector3 Size = new Vector3(1260f, 20f, 920f);
        const int HeightRes = 513, AlphaRes = 2048, DetailRes = 2048;

        public struct Footprint { public float x0, x1, z0, z1; public Footprint(float a, float b, float c, float d) { x0 = a; x1 = b; z0 = c; z1 = d; } }

        // layer indices
        const int LawnA = 0, LawnB = 1, Dry = 2, Patchy = 3, Farm = 4, Dirt = 5, Bare = 6, Gravel = 7, Layers = 8;

        // built-up area of the campus (outside it the ground fades to farmland)
        static readonly Footprint Campus = new Footprint(-885f, 35f, -390f, 205f);
        // sand courts next to the Mechanical Workshop (two rectangles, see NB_Mechanical_Ground.png); position estimated from the photo
        static readonly Footprint[] Courts = { new Footprint(-455f, -415f, -282f, -258f), new Footprint(-410f, -370f, -282f, -258f) };

        // ------------------------------------------------------------------ vegetation textures (procedural, alpha-cutout)
        static System.Random rng = new System.Random(7);

        static float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        static void Stamp(float[] a, Color[] c, int n, float x, float y, float radius, Color col)
        {
            int r = Mathf.CeilToInt(radius + 1f);
            for (int yy = Mathf.Max(0, (int)y - r); yy <= Mathf.Min(n - 1, (int)y + r); yy++)
                for (int xx = Mathf.Max(0, (int)x - r); xx <= Mathf.Min(n - 1, (int)x + r); xx++)
                {
                    float d = Mathf.Sqrt((xx + 0.5f - x) * (xx + 0.5f - x) + (yy + 0.5f - y) * (yy + 0.5f - y));
                    float cov = Mathf.Clamp01(radius - d + 0.5f);
                    if (cov <= 0f) continue;
                    int i = yy * n + xx;
                    if (cov >= a[i]) { a[i] = cov; c[i] = col; }
                }
        }

        static void Blade(float[] a, Color[] c, int n, float x0, float baseY, float height, float lean, float width, Color baseCol, Color tipCol)
        {
            int steps = Mathf.CeilToInt(height * 1.5f);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float x = x0 + lean * t * t * height;
                float y = baseY + t * height - lean * lean * 0.15f * t * t * height;
                float w = width * (1f - Mathf.Pow(t, 0.9f)) * 0.5f + 0.35f;
                Stamp(a, c, n, x, y, w, Color.Lerp(baseCol, tipCol, t));
            }
        }

        static void WriteCard(string name, int n, Action<float[], Color[]> draw)
        {
            string path = VegDir + "/" + name + ".png";
            if (File.Exists(path)) return;
            var a = new float[n * n]; var c = new Color[n * n];
            draw(a, c);
            // dilate colours into transparent texels so mip-mapping does not produce dark halos
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++)
            {
                Color col = a[i] > 0f ? c[i] : new Color(0.3f, 0.45f, 0.15f);
                px[i] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(a[i] > 0.5f ? 255 : a[i] * 2f * 255f));
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
            ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static void EnsureVegetationTextures()
        {
            RealismTrees.EnsureFolder(VegDir);
            const int n = 256;
            // mown-lawn tuft: many short blades, fresh green
            WriteCard("Grass_Mown", n, (a, c) => { for (int i = 0; i < 90; i++) Blade(a, c, n, R(20, 236), 2f, R(70, 120), R(-0.35f, 0.35f), R(2.4f, 3.6f), new Color(0.10f, 0.26f, 0.06f), new Color(R(0.34f, 0.5f), R(0.62f, 0.75f), R(0.15f, 0.25f))); });
            // tall dry tuft: fewer, longer, yellow-green to straw
            WriteCard("Grass_Dry", n, (a, c) => { for (int i = 0; i < 46; i++) { float t = R(0f, 1f); Blade(a, c, n, R(30, 226), 2f, R(120, 238), R(-0.6f, 0.6f), R(2.2f, 3.2f), new Color(0.22f, 0.30f, 0.08f), Color.Lerp(new Color(0.55f, 0.58f, 0.2f), new Color(0.78f, 0.68f, 0.35f), t)); } });
            // weed: broad leaves low in a rosette
            WriteCard("Weed", n, (a, c) =>
            {
                for (int i = 0; i < 9; i++)
                {
                    float ang = R(-1.2f, 1.2f), len = R(60, 120), x0 = 128 + R(-14, 14);
                    for (int s = 0; s <= (int)len; s++)
                    {
                        float t = s / len; float x = x0 + Mathf.Sin(ang) * t * len; float y = 6 + Mathf.Cos(ang) * t * len * 0.8f;
                        float w = Mathf.Sin(t * Mathf.PI) * R(9, 14) * (0.4f + 0.6f * (1f - t));
                        Stamp(a, c, n, x, y, Mathf.Max(0.8f, w), Color.Lerp(new Color(0.1f, 0.3f, 0.07f), new Color(0.28f, 0.52f, 0.14f), t));
                    }
                }
            });
            // small flowers (daisy / clover / buttercup mix) on short stems
            WriteCard("Flowers", n, (a, c) =>
            {
                Color[] petals = { new Color(1f, 1f, 0.95f), new Color(1f, 0.85f, 0.2f), new Color(0.85f, 0.55f, 0.9f), new Color(1f, 0.6f, 0.75f) };
                for (int i = 0; i < 7; i++)
                {
                    float x = R(30, 226), h = R(55, 150);
                    Blade(a, c, n, x, 2f, h, R(-0.3f, 0.3f), 2.2f, new Color(0.12f, 0.3f, 0.08f), new Color(0.2f, 0.45f, 0.12f));
                    Color p = petals[rng.Next(petals.Length)];
                    float hx = x + 0.0f, hy = 2f + h;
                    for (int k = 0; k < 8; k++) { float ang = k * Mathf.PI * 0.25f; Stamp(a, c, n, hx + Mathf.Cos(ang) * 6f, hy + Mathf.Sin(ang) * 6f, 4.2f, p); }
                    Stamp(a, c, n, hx, hy, 3.6f, new Color(0.95f, 0.75f, 0.1f));
                }
                for (int i = 0; i < 30; i++) Blade(a, c, n, R(20, 236), 2f, R(30, 60), R(-0.3f, 0.3f), 2.4f, new Color(0.1f, 0.26f, 0.06f), new Color(0.3f, 0.55f, 0.15f));
            });
        }

        // ------------------------------------------------------------------ distance fields
        static float[] Raster(int n, Func<float, float, float> sampleFn) { return null; }

        // world position of an alpha/detail cell centre
        static float WX(int cell, int res) { return Origin.x + (cell + 0.5f) / res * Size.x; }
        static float WZ(int cell, int res) { return Origin.z + (cell + 0.5f) / res * Size.z; }

        /// <summary>Distance (m) from each cell to the nearest road/path edge; negative inside. Large where there is none.</summary>
        static float[] CorridorField(int res, IList<Vector3> corridors)
        {
            var f = new float[res * res];
            for (int i = 0; i < f.Length; i++) f[i] = 999f;
            float cx = Size.x / res, cz = Size.z / res;
            foreach (var c in corridors) // x = world x, y = world z, z = half width
            {
                float reach = c.z + 6f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - reach - Origin.x) / cx)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.x + reach - Origin.x) / cx));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((c.y - reach - Origin.z) / cz)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.y + reach - Origin.z) / cz));
                for (int zi = z0; zi <= z1; zi++)
                    for (int xi = x0; xi <= x1; xi++)
                    {
                        float dx = WX(xi, res) - c.x, dz = WZ(zi, res) - c.y;
                        float d = Mathf.Sqrt(dx * dx + dz * dz) - c.z;
                        int idx = zi * res + xi;
                        if (d < f[idx]) f[idx] = d;
                    }
            }
            return f;
        }

        static float[] BoxField(int res, IList<Footprint> boxes, float margin)
        {
            var f = new float[res * res];
            for (int i = 0; i < f.Length; i++) f[i] = 999f;
            float cx = Size.x / res, cz = Size.z / res;
            foreach (var b in boxes)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.x0 - margin - Origin.x) / cx)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((b.x1 + margin - Origin.x) / cx));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.z0 - margin - Origin.z) / cz)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((b.z1 + margin - Origin.z) / cz));
                for (int zi = z0; zi <= z1; zi++)
                    for (int xi = x0; xi <= x1; xi++)
                    {
                        float x = WX(xi, res), z = WZ(zi, res);
                        float dx = Mathf.Max(Mathf.Max(b.x0 - x, x - b.x1), 0f), dz = Mathf.Max(Mathf.Max(b.z0 - z, z - b.z1), 0f);
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        int idx = zi * res + xi;
                        if (d < f[idx]) f[idx] = d;
                    }
            }
            return f;
        }

        /// <summary>Canopy influence 0..1 around tree positions (x,z) with the given radius.</summary>
        static float[] TreeField(int res, IList<Vector2> trees, float radius)
        {
            var f = new float[res * res];
            float cx = Size.x / res, cz = Size.z / res;
            foreach (var t in trees)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((t.x - radius - Origin.x) / cx)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((t.x + radius - Origin.x) / cx));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((t.y - radius - Origin.z) / cz)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((t.y + radius - Origin.z) / cz));
                for (int zi = z0; zi <= z1; zi++)
                    for (int xi = x0; xi <= x1; xi++)
                    {
                        float dx = WX(xi, res) - t.x, dz = WZ(zi, res) - t.y;
                        float v = 1f - Mathf.Sqrt(dx * dx + dz * dz) / radius;
                        if (v > f[zi * res + xi]) f[zi * res + xi] = v;
                    }
            }
            return f;
        }

        static float Noise(float x, float z, float scale, float seed) { return Mathf.PerlinNoise(x / scale + seed, z / scale + seed * 1.7f); }
        static float Smooth(float a, float b, float v) { v = Mathf.Clamp01((v - a) / (b - a)); return v * v * (3f - 2f * v); }

        // ------------------------------------------------------------------ build
        /// <param name="corridors">(x, z, halfWidth) samples of roads and paths</param>
        /// <param name="buildings">building footprints</param>
        /// <param name="openLawn">central lawn that must stay short and mown</param>
        /// <param name="trees">tree positions (x, z)</param>
        public static Terrain Build(Transform parent, IList<Vector3> corridors, IList<Footprint> buildings, Footprint openLawn, IList<Vector2> trees)
        {
            EnsureVegetationTextures();
            foreach (string s in RealismTextures.AllSets) RealismTextures.ImportSet(s);
            RealismTrees.EnsureFolder("Assets/Art/Terrain");

            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
            if (td == null) { td = new TerrainData(); AssetDatabase.CreateAsset(td, DataPath); }
            td.heightmapResolution = HeightRes;
            td.alphamapResolution = AlphaRes;
            td.baseMapResolution = 1024;
            td.size = Size;
            td.SetHeights(0, 0, new float[HeightRes, HeightRes]); // flat at y = 0: all buildings, roads and anchors are authored for y = 0

            string[] sets = { "Grass005", "Grass006", "Grass004", "Grass007", "Ground054", "Ground103", "Ground106", "Ground108" };
            float[] tile = { 5f, 6.5f, 5f, 4.5f, 8f, 3.5f, 4f, 3f };
            float[] smooth = { 0.32f, 0.32f, 0.22f, 0.25f, 0.18f, 0.18f, 0.2f, 0.3f };
            var layers = new TerrainLayer[Layers];
            // diffuse remap (multiplier) tones the very saturated ambientCG lawns toward the duller, yellower campus grass in the aerials
            Vector4[] tint = { new Vector4(0.80f, 0.86f, 0.60f, 1f), new Vector4(0.74f, 0.82f, 0.56f, 1f), new Vector4(0.98f, 0.95f, 0.72f, 1f), new Vector4(0.86f, 0.84f, 0.62f, 1f),
                               new Vector4(0.95f, 0.88f, 0.76f, 1f), new Vector4(0.9f, 0.85f, 0.78f, 1f), new Vector4(0.92f, 0.86f, 0.78f, 1f), new Vector4(0.95f, 0.95f, 0.95f, 1f) };
            for (int i = 0; i < Layers; i++) { layers[i] = RealismTextures.Layer(sets[i], tile[i], smooth[i]); layers[i].diffuseRemapMin = Vector4.zero; layers[i].diffuseRemapMax = tint[i]; EditorUtility.SetDirty(layers[i]); }
            td.terrainLayers = layers;

            PaintAlphamaps(td, corridors, buildings, openLawn, trees);
            SetupDetails(td, corridors, buildings, openLawn, trees);

            td.wavingGrassStrength = 0.35f; td.wavingGrassSpeed = 0.45f; td.wavingGrassAmount = 0.35f;
            td.wavingGrassTint = new Color(0.8f, 0.85f, 0.7f);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Campus_Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = Origin;
            go.layer = 0;
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = TerrainMaterial();
            t.drawInstanced = true;
            t.heightmapPixelError = 4f;
            t.basemapDistance = 220f;
            t.shadowCastingMode = ShadowCastingMode.Off;
            t.detailObjectDistance = 95f;
            t.detailObjectDensity = 1f;
            t.groupingID = 0;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return t;
        }

        static Material TerrainMaterial()
        {
            string path = "Assets/Materials/Campus/M_Campus_Terrain.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            m.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            m.SetFloat("_EnableHeightBlend", 1f);
            m.SetFloat("_HeightTransition", 0.35f);
            m.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ alphamaps
        static void PaintAlphamaps(TerrainData td, IList<Vector3> corridors, IList<Footprint> buildings, Footprint openLawn, IList<Vector2> trees)
        {
            int n = AlphaRes;
            float[] road = CorridorField(n, corridors);
            float[] bld = BoxField(n, buildings, 0f);
            float[] shade = TreeField(n, trees, 9f);
            var maps = new float[n, n, Layers];
            var w = new float[Layers];
            for (int zi = 0; zi < n; zi++)
            {
                float z = WZ(zi, n);
                for (int xi = 0; xi < n; xi++)
                {
                    float x = WX(xi, n);
                    int idx = zi * n + xi;
                    Array.Clear(w, 0, Layers);

                    // distance outside the built-up campus (negative inside), roughened by noise so the boundary is not a straight line
                    float dx = Mathf.Max(Mathf.Max(Campus.x0 - x, x - Campus.x1), 0f), dz = Mathf.Max(Mathf.Max(Campus.z0 - z, z - Campus.z1), 0f);
                    float dOut = Mathf.Sqrt(dx * dx + dz * dz);
                    float inside = Mathf.Min(Mathf.Min(x - Campus.x0, Campus.x1 - x), Mathf.Min(z - Campus.z0, Campus.z1 - z)); // depth inside the boundary
                    float rough = (Noise(x, z, 38f, 3f) - 0.5f) * 45f;
                    float farm = Smooth(-5f, 55f, dOut + rough * (dOut > 0f ? 1f : 0.3f) + 4f);
                    if (dOut <= 0f) farm *= Smooth(-30f, 0f, -inside + rough * 0.4f) * 0.35f;

                    // lawn base: two mown greens in large, non-repeating patches
                    float macro = Smooth(0.35f, 0.65f, Noise(x, z, 55f, 11f)) * 0.8f + Noise(x, z, 9f, 5f) * 0.2f;
                    // dryness: near the boundary, under trees, in noise patches; never on the central lawn / garden belts
                    float edge = 1f - Smooth(0f, 45f, inside);
                    float dry = Mathf.Clamp01(edge * 0.85f + shade[idx] * 0.55f + Smooth(0.55f, 0.85f, Noise(x, z, 24f, 17f)) * 0.35f);
                    bool inOpen = x > openLawn.x0 && x < openLawn.x1 && z > openLawn.z0 && z < openLawn.z1;
                    if (inOpen) dry *= 0.12f;
                    float patchy = Mathf.Clamp01(shade[idx] * 0.9f + Smooth(0.62f, 0.9f, Noise(x, z, 14f, 29f)) * 0.5f) * dry;

                    float grassW = 1f - farm;
                    float dryW = dry * 0.7f, patchW = patchy * 0.6f;
                    float lawn = Mathf.Clamp01(1f - dryW - patchW);
                    w[LawnA] = grassW * lawn * (1f - macro);
                    w[LawnB] = grassW * lawn * macro;
                    w[Dry] = grassW * dryW;
                    w[Patchy] = grassW * patchW;
                    w[Farm] = farm * (0.75f + 0.25f * Noise(x, z, 20f, 41f));
                    w[Dirt] = farm * (0.25f - 0.25f * Noise(x, z, 20f, 41f)) + 0.0f;
                    w[Bare] = farm * Smooth(0.62f, 0.8f, Noise(x, z, 17f, 53f)) * 0.5f;

                    // leaf-litter / bare soil patches under dense trees
                    float litter = shade[idx] * Smooth(0.55f, 0.8f, Noise(x, z, 6f, 61f));
                    w[Bare] += litter * 0.9f;

                    // sand courts
                    foreach (var c in Courts)
                    {
                        float cdx = Mathf.Max(Mathf.Max(c.x0 - x, x - c.x1), 0f), cdz = Mathf.Max(Mathf.Max(c.z0 - z, z - c.z1), 0f);
                        float cd = Mathf.Sqrt(cdx * cdx + cdz * cdz);
                        float inC = 1f - Smooth(0f, 1.2f, cd);
                        if (inC > 0f) { for (int l = 0; l < Layers; l++) w[l] *= (1f - inC); w[Farm] += inC; }
                    }

                    // road/path edge: soil strip (soft), and the corridor itself is bare dirt under the paving
                    float rd = road[idx];
                    if (rd < 1.4f)
                    {
                        float strip = 1f - Smooth(0.15f, 1.4f, rd);
                        strip *= 0.55f + 0.45f * Noise(x, z, 3f, 71f);
                        for (int l = 0; l < Layers; l++) w[l] *= (1f - strip);
                        w[Dirt] += strip;
                    }
                    if (bld[idx] < 3f) { float b = 1f - Smooth(0f, 3f, bld[idx]); for (int l = 0; l < Layers; l++) w[l] *= (1f - b); w[Gravel] += b; }

                    float sum = 0f; for (int l = 0; l < Layers; l++) sum += w[l];
                    if (sum < 1e-4f) { w[LawnA] = 1f; sum = 1f; }
                    for (int l = 0; l < Layers; l++) maps[zi, xi, l] = w[l] / sum;
                }
            }
            td.SetAlphamaps(0, 0, maps);
        }

        // ------------------------------------------------------------------ detail grass
        static DetailPrototype Proto(string tex, float minW, float maxW, float minH, float maxH, Color healthy, Color dry, float noise)
        {
            return new DetailPrototype
            {
                prototypeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(VegDir + "/" + tex + ".png"),
                usePrototypeMesh = false, renderMode = DetailRenderMode.GrassBillboard,
                minWidth = minW, maxWidth = maxW, minHeight = minH, maxHeight = maxH,
                healthyColor = healthy, dryColor = dry, noiseSpread = noise, holeEdgePadding = 0.2f,
            };
        }

        static void SetupDetails(TerrainData td, IList<Vector3> corridors, IList<Footprint> buildings, Footprint openLawn, IList<Vector2> trees)
        {
            td.SetDetailResolution(DetailRes, 16);
            td.detailPrototypes = new[]
            {
                Proto("Grass_Mown", 0.35f, 0.55f, 0.13f, 0.24f, new Color(0.95f, 1f, 0.9f), new Color(0.85f, 0.85f, 0.5f), 0.4f),
                Proto("Grass_Dry", 0.5f, 0.9f, 0.35f, 0.75f, new Color(0.9f, 0.95f, 0.7f), new Color(0.95f, 0.85f, 0.5f), 0.6f),
                Proto("Weed", 0.3f, 0.5f, 0.12f, 0.25f, new Color(0.9f, 1f, 0.85f), new Color(0.85f, 0.85f, 0.55f), 0.7f),
                Proto("Flowers", 0.3f, 0.5f, 0.18f, 0.32f, Color.white, Color.white, 0.5f),
            };
            int n = DetailRes;
            float[] road = CorridorField(n, corridors);
            float[] bld = BoxField(n, buildings, 1.5f);
            float[] shade = TreeField(n, trees, 9f);
            var mown = new int[n, n]; var dryG = new int[n, n]; var weed = new int[n, n]; var flower = new int[n, n];
            // grass is placed from the same inputs as the terrain paint so it never grows on roads, paths, buildings or farmland
            for (int zi = 0; zi < n; zi++)
            {
                float z = WZ(zi, n);
                for (int xi = 0; xi < n; xi++)
                {
                    float x = WX(xi, n);
                    int idx = zi * n + xi;
                    if (road[idx] < 0.45f || bld[idx] < 0.3f) continue;
                    foreach (var c in Courts) if (x > c.x0 - 1f && x < c.x1 + 1f && z > c.z0 - 1f && z < c.z1 + 1f) goto skip;
                    float dx = Mathf.Max(Mathf.Max(Campus.x0 - x, x - Campus.x1), 0f), dz = Mathf.Max(Mathf.Max(Campus.z0 - z, z - Campus.z1), 0f);
                    float dOut = Mathf.Sqrt(dx * dx + dz * dz);
                    float rough = (Noise(x, z, 38f, 3f) - 0.5f) * 45f;
                    float farm = Smooth(-5f, 55f, dOut + rough * (dOut > 0f ? 1f : 0.3f) + 4f);
                    if (farm > 0.75f) continue;
                    float inside = Mathf.Min(Mathf.Min(x - Campus.x0, Campus.x1 - x), Mathf.Min(z - Campus.z0, Campus.z1 - z));
                    float edge = 1f - Smooth(0f, 45f, inside);
                    bool inOpen = x > openLawn.x0 && x < openLawn.x1 && z > openLawn.z0 && z < openLawn.z1;
                    float dry = Mathf.Clamp01(edge * 0.85f + shade[idx] * 0.55f + Smooth(0.55f, 0.85f, Noise(x, z, 24f, 17f)) * 0.35f);
                    if (inOpen) dry *= 0.12f;
                    float dens = 1f - farm;
                    // short, dense mown tufts everywhere on lawns (fewer when dry); taller dry tufts in dry zones
                    float edgeFade = Smooth(0.45f, 1.6f, road[idx]); // thin out right beside kerbs and paths
                    mown[zi, xi] = Mathf.RoundToInt(Mathf.Clamp01(dens * (1f - dry * 0.75f) * edgeFade) * 9f);
                    dryG[zi, xi] = Mathf.RoundToInt(Mathf.Clamp01(dens * dry * (0.4f + 0.6f * Noise(x, z, 6f, 83f)) * edgeFade) * 7f);
                    float weedN = Noise(x, z, 5f, 91f);
                    weed[zi, xi] = weedN > 0.62f ? Mathf.RoundToInt(dens * edgeFade * (weedN - 0.6f) * 12f) : 0;
                    float flN = Noise(x, z, 8f, 97f);
                    flower[zi, xi] = flN > 0.7f && !inOpen ? Mathf.RoundToInt(dens * edgeFade * (flN - 0.68f) * 14f) : (flN > 0.8f ? 1 : 0);
                    skip:;
                }
            }
            td.SetDetailLayer(0, 0, 0, mown);
            td.SetDetailLayer(0, 0, 1, dryG);
            td.SetDetailLayer(0, 0, 2, weed);
            td.SetDetailLayer(0, 0, 3, flower);
        }
    }
}
