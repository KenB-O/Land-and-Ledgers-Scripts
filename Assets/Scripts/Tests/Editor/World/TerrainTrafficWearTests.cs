using System.Collections.Generic;
using System.Reflection;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class TerrainTrafficWearTests
    {
        private static readonly float[] RepresentativeCoordinates = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        [Test]
        public void RepeatedActualMovementAccumulationBlendsSmoothlyIntoSoil()
        {
            List<Object> cleanup = new();
            try
            {
                TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    "Assets/EnvironmentDevelopment/TerrainLayers/Meadow Grass.terrainlayer");
                TerrainLayer soil = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    "Assets/EnvironmentDevelopment/TerrainLayers/Disturbed Soil.terrainlayer");
                Assert.NotNull(grass);
                Assert.NotNull(soil);

                TerrainData data = new()
                {
                    heightmapResolution = 33,
                    alphamapResolution = 128,
                    size = new Vector3(64f, 12f, 64f),
                    terrainLayers = new[] { grass, soil }
                };
                cleanup.Add(data);
                float[,,] initial = new float[128, 128, 2];
                for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++) initial[z, x, 0] = 1f;
                data.SetAlphamaps(0, 0, initial);

                GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
                cleanup.Add(terrainObject);
                Terrain terrain = terrainObject.GetComponent<Terrain>();
                TerrainTrafficWear wear = terrainObject.AddComponent<TerrainTrafficWear>();
                SetField(wear, "terrain", terrain);
                SetField(wear, "runtimeData", data);
                SetField(wear, "soilLayerIndex", 1);
                SetField(wear, "accumulatedWear", new float[128, 128]);

                Vector3 center = terrain.transform.position + new Vector3(32f, 0f, 32f);
                MethodInfo kernel = typeof(TerrainTrafficWear).GetMethod("AccumulateKernel", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo flush = typeof(TerrainTrafficWear).GetMethod("ApplyDirtyTile", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(kernel);
                Assert.NotNull(flush);

                kernel.Invoke(wear, new object[] { center, 1.35f, 0.006f });
                flush.Invoke(wear, new object[] { 36 });
                float onePassSoil = ReadSoilAlpha(data, 64, 64);
                float softEdgeSoil = ReadSoilAlpha(data, 64, 65);
                Assert.That(onePassSoil, Is.GreaterThan(0f).And.LessThan(0.04f),
                    "One crossing should leave only a light blended trace.");
                Assert.That(softEdgeSoil, Is.GreaterThan(0f).And.LessThan(onePassSoil),
                    "The radial brush should soften the edge instead of painting a square patch.");

                for (int pass = 0; pass < 19; pass++)
                    kernel.Invoke(wear, new object[] { center, 1.35f, 0.006f });
                flush.Invoke(wear, new object[] { 36 });
                float repeatedPassSoil = ReadSoilAlpha(data, 64, 64);
                Assert.That(repeatedPassSoil, Is.GreaterThan(onePassSoil + 0.3f),
                    "Repeated traffic should accumulate visibly beyond an isolated crossing.");

                float[,] accumulated = (float[,])GetField(wear, "accumulatedWear");
                Assert.That(accumulated[64, 64], Is.EqualTo(0.12f).Within(0.002f));

            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        [Test]
        public void RuntimeTerrainInitializationClonesAndRestoresAuthoredTerrainData()
        {
            TerrainData authored = AssetDatabase.LoadAssetAtPath<TerrainData>(
                "Assets/Core/World/PreAuthoredTerrain/MainAuthoredTerrainData.asset");
            Assert.NotNull(authored);
            int authoredResolution = authored.alphamapResolution;
            float[,,] authoredSamples = CaptureRepresentativeAlphamaps(authored);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(authored);
            try
            {
                Terrain terrain = terrainObject.GetComponent<Terrain>();
                TerrainTrafficWear wear = terrainObject.AddComponent<TerrainTrafficWear>();
                InvokePrivate(wear, "TryCreateRuntimeTerrain");

                Assert.IsTrue(wear.RuntimeTerrainCloneActive);
                Assert.AreEqual(1024, terrain.terrainData.alphamapResolution);
                Assert.AreEqual(authoredResolution, authored.alphamapResolution,
                    "Runtime wear must not resize the authored TerrainData asset.");
                AssertRepresentativeAlphamapsEqual(authoredSamples, authored, "The authored terrain must remain unchanged.");
                AssertResampledAlphamapsMatchSource(authored, terrain.terrainData,
                    "Resizing the runtime clone must preserve the authored layer distribution instead of resetting it to layer zero.");

                InvokePrivate(wear, "RestoreAuthoredTerrainReference");
                Assert.AreSame(authored, terrain.terrainData);
                Assert.AreEqual(authoredResolution, authored.alphamapResolution);
                AssertRepresentativeAlphamapsEqual(authoredSamples, authored, "Restoring the authored terrain must keep its original layer distribution.");
            }
            finally
            {
                if (terrainObject != null) Object.DestroyImmediate(terrainObject);
            }
        }

        private static float ReadSoilAlpha(TerrainData data, int x, int z)
            => data.GetAlphamaps(x, z, 1, 1)[0, 0, 1];

        private static float[,,] CaptureRepresentativeAlphamaps(TerrainData data)
        {
            float[,,] samples = new float[RepresentativeCoordinates.Length, RepresentativeCoordinates.Length, data.terrainLayers.Length];
            for (int z = 0; z < RepresentativeCoordinates.Length; z++)
            {
                for (int x = 0; x < RepresentativeCoordinates.Length; x++)
                {
                    int sampleX = Mathf.RoundToInt(RepresentativeCoordinates[x] * (data.alphamapWidth - 1));
                    int sampleZ = Mathf.RoundToInt(RepresentativeCoordinates[z] * (data.alphamapHeight - 1));
                    float[,,] pixel = data.GetAlphamaps(sampleX, sampleZ, 1, 1);
                    for (int layer = 0; layer < data.terrainLayers.Length; layer++)
                    {
                        samples[z, x, layer] = pixel[0, 0, layer];
                    }
                }
            }

            return samples;
        }

        private static void AssertRepresentativeAlphamapsEqual(float[,,] expected, TerrainData actual, string message)
        {
            Assert.AreEqual(expected.GetLength(2), actual.terrainLayers.Length, message);
            for (int z = 0; z < RepresentativeCoordinates.Length; z++)
            {
                for (int x = 0; x < RepresentativeCoordinates.Length; x++)
                {
                    int actualX = Mathf.RoundToInt(RepresentativeCoordinates[x] * (actual.alphamapWidth - 1));
                    int actualZ = Mathf.RoundToInt(RepresentativeCoordinates[z] * (actual.alphamapHeight - 1));
                    float[,,] pixel = actual.GetAlphamaps(actualX, actualZ, 1, 1);
                    for (int layer = 0; layer < actual.terrainLayers.Length; layer++)
                    {
                        Assert.That(pixel[0, 0, layer], Is.EqualTo(expected[z, x, layer]).Within(0.01f),
                            $"Layer {layer} at representative alphamap sample ({z},{x}) differs. {message}");
                    }
                }
            }
        }

        private static void AssertResampledAlphamapsMatchSource(TerrainData source, TerrainData destination, string message)
        {
            float[,,] sourceMaps = source.GetAlphamaps(0, 0, source.alphamapWidth, source.alphamapHeight);
            int layerCount = sourceMaps.GetLength(2);
            Assert.AreEqual(layerCount, destination.terrainLayers.Length, message);
            foreach (float zFraction in RepresentativeCoordinates)
            {
                foreach (float xFraction in RepresentativeCoordinates)
                {
                    int x = Mathf.RoundToInt(xFraction * (destination.alphamapWidth - 1));
                    int z = Mathf.RoundToInt(zFraction * (destination.alphamapHeight - 1));
                    float sourceX = x / (float)(destination.alphamapWidth - 1) * (source.alphamapWidth - 1);
                    float sourceZ = z / (float)(destination.alphamapHeight - 1) * (source.alphamapHeight - 1);
                    int x0 = Mathf.FloorToInt(sourceX);
                    int x1 = Mathf.Min(source.alphamapWidth - 1, x0 + 1);
                    int z0 = Mathf.FloorToInt(sourceZ);
                    int z1 = Mathf.Min(source.alphamapHeight - 1, z0 + 1);
                    float tx = sourceX - x0;
                    float tz = sourceZ - z0;
                    float[,,] actualPixel = destination.GetAlphamaps(x, z, 1, 1);
                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        float lower = Mathf.Lerp(sourceMaps[z0, x0, layer], sourceMaps[z0, x1, layer], tx);
                        float upper = Mathf.Lerp(sourceMaps[z1, x0, layer], sourceMaps[z1, x1, layer], tx);
                        float expected = Mathf.Lerp(lower, upper, tz);
                        Assert.That(actualPixel[0, 0, layer], Is.EqualTo(expected).Within(0.01f),
                            $"Layer {layer} at destination sample ({z},{x}) differs. {message}");
                    }
                }
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = typeof(TerrainTrafficWear).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Expected TerrainTrafficWear.{name}.");
            field.SetValue(target, value);
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = typeof(TerrainTrafficWear).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Expected TerrainTrafficWear.{name}.");
            return field.GetValue(target);
        }

        private static void InvokePrivate(object target, string name)
        {
            MethodInfo method = typeof(TerrainTrafficWear).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"Expected TerrainTrafficWear.{name}.");
            method.Invoke(target, null);
        }
    }
}
