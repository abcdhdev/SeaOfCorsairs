using System.IO;
using Gaia.Internal;
using PWCommon5;
using Gaia;
using UnityEditor;
using UnityEngine;

namespace Gaia
{
    public class HeightmapTerraceRemover : EditorWindow, IPWEditor
    {
        private Texture2D inputTexture;
        private Texture2D outputTexture;
        private Texture2D tempOutputTexture;
        private Texture2D analyzedTexture; // Texture to store the analyzed image
        private Texture2D importantDetailsMask; // Mask to store important details

        private Terrain inputTerrain; // Terrain object to get the input texture from

        private string outputPath = "Assets/Gaia User Data/Fixed Terrain Heightmaps/";

        private int bilateralKernelSize = 10;

        private float perlinScale = 0.12f;
        private float perlinStrength = 0.0035f;
        private float bilateralSigmaSpatial = 8.0f;
        private float bilateralSigmaRange = 5f;
        private float slopeTerraceThreshold = 0.00119f;
        private float flatThreshold = 0.00091f;
        private float verticalGradientThreshold = 0.00056f;
        private float minTerraceThreshold = 0.000275f;
        private float maxTerraceThreshold = 0.00053f;
        private float previewScale = 256f;

        private Vector2 scrollPosition;

        private bool isAnalyzed = false;
        private bool excludeRed = true;
        private bool excludeBlack = false;
        private bool automaticWorkflow = true; // Flag to indicate automatic workflow of the tool
        private bool terrainWorkflow = false;
        private bool previousTerrainWorkflow = false;
        private bool exportExrTerrainWorkflow = false; // Flag to indicate exporting to EXR for terrain workflow

        private string processButtonText = "Process Heightmap";

        private EditorUtils m_editorUtils;
        private GaiaSettings m_settings;

        private bool m_advancedSettingsFolded = false;

        // Visual variables for sliders
        private float perlinStrength_v = 3.5f;
        private float slopeTerraceThreshold_v = 11.9f;
        private float flatThreshold_v = 9.1f;
        private float verticalGradientThreshold_v = 5.6f;
        private float minTerraceThreshold_v = 2.75f;
        private float maxTerraceThreshold_v = 5.3f;

        [HideInInspector]
        public bool
            autoManagerProcessing = false; // Flag to indicate automatic workflow of the tool from the Manager Window

        [HideInInspector]
        public bool enabledInManager = false;

        enum EWorkflow
        {
            Terrain,
            Texture,
            TerrainWithTexture
        }

        public bool PositionChecked
        {
            get => true;
            set => PositionChecked = value;
        }

        void OnEnable()
        {
            if (m_editorUtils == null)
            {
                // Get editor utils for this
                m_editorUtils = PWApp.GetEditorUtils(this);
            }

            titleContent = m_editorUtils.GetContent("WindowTitle");

            // Initialize real values from visual slider values
            perlinStrength = perlinStrength_v / 1000f;
            slopeTerraceThreshold = slopeTerraceThreshold_v / 10000f;
            flatThreshold = flatThreshold_v / 10000f;
            verticalGradientThreshold = verticalGradientThreshold_v / 10000f;
            minTerraceThreshold = minTerraceThreshold_v / 10000f;
            maxTerraceThreshold = maxTerraceThreshold_v / 10000f;
        }

        public void OnGUI()
        {
            m_editorUtils.Initialize();

            m_editorUtils.Panel("HeightmapTerraceRemover", DrawHeightmapTerraceRemover, true);
        }

        private void DrawHeightmapTerraceRemover(bool helpEnabled)
        {
            if (m_settings == null)
            {
                m_settings = GaiaUtils.GetGaiaSettings();
            }

            if (autoManagerProcessing)
            {
                // In manager context, always operate on terrain and hide confusing toggles
                terrainWorkflow = true;
                automaticWorkflow = true;
                exportExrTerrainWorkflow = false;

                // Enable toggle for manager selection
                enabledInManager = EditorGUILayout.ToggleLeft("Enable Heightmap Terrace Remover", enabledInManager);
                if (!enabledInManager)
                {
                    return;
                }
            }
            else
            {
                terrainWorkflow = m_editorUtils.Toggle("HeightmapFromTerrain", terrainWorkflow, helpEnabled);
            }

            // Detect change in terrainWorkflow
            if (terrainWorkflow != previousTerrainWorkflow)
            {
                previousTerrainWorkflow = terrainWorkflow;
                // Reset all variables when switching to terrain workflow
                if (terrainWorkflow)
                {
                    inputTexture = null;
                    outputPath = "Assets/Gaia User Data/Fixed Terrain Heightmaps/";
                    analyzedTexture = null;
                    importantDetailsMask = null;
                    outputTexture = null;
                    isAnalyzed = false;
                }
                else
                {
                    inputTerrain = null;
                    inputTexture = null;
                    analyzedTexture = null;
                    importantDetailsMask = null;
                    outputTexture = null;
                    isAnalyzed = false;
                    exportExrTerrainWorkflow = false;
                }
            }

            GUILayout.Space(6f);

            if (terrainWorkflow)
            {
                if (!autoManagerProcessing)
                {
                    inputTerrain = (Terrain)m_editorUtils.ObjectField("InputTerrain", inputTerrain, typeof(Terrain), true, helpEnabled);

                    GUILayout.Space(5f);
                }

                if (!autoManagerProcessing)
                {
                    exportExrTerrainWorkflow = m_editorUtils.Toggle("ExportHeightmapForTerrain", exportExrTerrainWorkflow, helpEnabled);
                    if (exportExrTerrainWorkflow)
                    {
                        GUILayout.Space(7);
                        outputPath = m_editorUtils.TextField("OutputPath", outputPath, helpEnabled);
                    }
                }

                processButtonText = "Process Terrain";
            }
            else
            {
                inputTexture = (Texture2D)m_editorUtils.ObjectField("InputTexture", inputTexture, typeof(Texture2D), false, helpEnabled);

                processButtonText = "Process Heightmap";
            }

            perlinScale = m_editorUtils.FloatField("PerlinNoiseScale", perlinScale, helpEnabled);

            perlinStrength_v = m_editorUtils.Slider("PerlinNoiseStrength", perlinStrength_v, 0.1f, 10f, helpEnabled);
            perlinStrength = perlinStrength_v / 1000f;

            m_advancedSettingsFolded = EditorGUILayout.Foldout(m_advancedSettingsFolded, "Advanced Settings", true);
            if (m_advancedSettingsFolded)
            {
                EditorGUI.indentLevel++;

                bilateralKernelSize = m_editorUtils.IntField("BilateralKernelSize", bilateralKernelSize, helpEnabled);
                bilateralSigmaSpatial = m_editorUtils.FloatField("BilateralSigmaSpatial", bilateralSigmaSpatial, helpEnabled);
                bilateralSigmaRange = m_editorUtils.FloatField("BilateralSigmaRange", bilateralSigmaRange, helpEnabled);

                slopeTerraceThreshold_v = m_editorUtils.Slider("SlopeTerraceThreshold", slopeTerraceThreshold_v, 0.1f, 20f, helpEnabled);
                slopeTerraceThreshold = slopeTerraceThreshold_v / 10000f;

                flatThreshold_v = m_editorUtils.Slider("FlatThreshold", flatThreshold_v, 0.1f, 20f, helpEnabled);
                flatThreshold = flatThreshold_v / 10000f;

                verticalGradientThreshold_v = m_editorUtils.Slider("VerticalGradientThreshold", verticalGradientThreshold_v, 0.1f, 10f, helpEnabled);
                verticalGradientThreshold = verticalGradientThreshold_v / 10000f;

                minTerraceThreshold_v = m_editorUtils.Slider("MinTerraceThreshold", minTerraceThreshold_v, 0.1f, 10f, helpEnabled);
                minTerraceThreshold = minTerraceThreshold_v / 10000f;

                maxTerraceThreshold_v = m_editorUtils.Slider("MaxTerraceThreshold", maxTerraceThreshold_v, 0.1f, 10f, helpEnabled);
                maxTerraceThreshold = maxTerraceThreshold_v / 10000f;

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(6f);

            if (!autoManagerProcessing)
            {
                automaticWorkflow = m_editorUtils.Toggle("AutomaticWorkflow", automaticWorkflow, helpEnabled);
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUI.backgroundColor = m_settings.GetActionButtonColor();

            if (!autoManagerProcessing)
            {
                string analyzeKey = terrainWorkflow ? "AnalyzeTerrain" : "AnalyzeTexture";
                if (m_editorUtils.Button(analyzeKey))
                {
                    StartAnalyzing();
                }
            }

            GUI.backgroundColor = Color.white;

            if (analyzedTexture && !automaticWorkflow)
            {
                GUILayout.Label(m_editorUtils.GetTextValue("AnalyzedTextureLabel"));
                previewScale = m_editorUtils.Slider("PreviewScale", previewScale, 128f, 512f, helpEnabled);
                GUILayout.Label(new GUIContent(analyzedTexture), GUILayout.Width(previewScale),
                    GUILayout.Height(previewScale));

                excludeRed = m_editorUtils.Toggle("ExcludeRed", excludeRed, helpEnabled);
                excludeBlack = m_editorUtils.Toggle("ExcludeBlack", excludeBlack, helpEnabled);
            }

            GUI.backgroundColor = m_settings.GetActionButtonColor();
            if (isAnalyzed && !automaticWorkflow && GUILayout.Button(processButtonText))
            {
                if (inputTexture)
                {
                    if (terrainWorkflow && exportExrTerrainWorkflow)
                    {
                        outputTexture = ProcessHeightmap(inputTexture, EWorkflow.TerrainWithTexture);
                        ApplyHeightmapToTerrain(outputTexture);
                    }
                    else if (terrainWorkflow)
                    {
                        outputTexture = ProcessHeightmap(inputTexture, EWorkflow.Terrain);
                        ApplyHeightmapToTerrain(outputTexture);
                    }
                    else
                    {
                        outputTexture = ProcessHeightmap(inputTexture, EWorkflow.Texture);
                    }
                }
            }

            GUI.backgroundColor = Color.white;

            if (outputTexture || tempOutputTexture && !automaticWorkflow)
            {
                if (terrainWorkflow && exportExrTerrainWorkflow || !terrainWorkflow)
                {
                    if (terrainWorkflow && exportExrTerrainWorkflow && tempOutputTexture)
                    {
                        GUILayout.Label("Processed Texture:");
                        GUILayout.Label(new GUIContent(tempOutputTexture), GUILayout.Width(previewScale),
                            GUILayout.Height(previewScale));

                        if (GUILayout.Button("Save Processed Texture"))
                        {
                            SaveTexture(tempOutputTexture);
                        }
                    }
                    else if (outputTexture)
                    {
                        GUILayout.Label("Processed Texture:");
                        GUILayout.Label(new GUIContent(outputTexture), GUILayout.Width(previewScale),
                            GUILayout.Height(previewScale));

                        GUI.backgroundColor = m_settings.GetActionButtonColor();
                        if (GUILayout.Button("Save Processed Texture"))
                        {
                            SaveTexture(outputTexture);
                        }

                        GUI.backgroundColor = Color.white;
                    }
                }
            }

            GUILayout.EndScrollView();
        }

        public void StartProcessingTerrain(Terrain terrain)
        {
            inputTerrain = terrain;

            if (inputTerrain)
            {
                StartAnalyzing();
            }
        }

        private void StartAnalyzing()
        {
            if (automaticWorkflow)
            {
                AnalyzeTexture(automaticWorkflow);
            }
            else
            {
                if (terrainWorkflow)
                {
                    AnalyzeTexture(automaticWorkflow);
                }
                else if (!terrainWorkflow && inputTexture)
                {
                    AnalyzeTexture(automaticWorkflow);
                }
                else
                {
                    Debug.LogError("Please assign an input texture.");
                }
            }
        }

        private void AnalyzeTexture(bool auto)
        {
            if (inputTerrain)
            {
                inputTexture = ExtractHeightmap();
            }

            importantDetailsMask =
                new Texture2D(inputTexture.width, inputTexture.height, TextureFormat.RGBAFloat, false);
            analyzedTexture = new Texture2D(inputTexture.width, inputTexture.height, TextureFormat.RGBAFloat, false);

            if (inputTerrain && exportExrTerrainWorkflow)
            {
                float[,] heightData =
                    ExtractHeightData(inputTexture.GetPixels(), inputTexture.width, inputTexture.height);
                // Normalize the height values and populate the export texture with the heightmap data in the red channel
                NormalizeHeightmap(heightData, inputTexture);

                // First pass: classify pixels
                for (int y = 1; y < inputTexture.height - 1; y++)
                {
                    for (int x = 1; x < inputTexture.width - 1; x++)
                    {
                        float currentHeight = inputTexture.GetPixel(x, y).r;

                        float gradientX = Mathf.Abs(currentHeight - inputTexture.GetPixel(x - 1, y).r) +
                                          Mathf.Abs(currentHeight - inputTexture.GetPixel(x + 1, y).r);
                        float gradientY = Mathf.Abs(currentHeight - inputTexture.GetPixel(x, y - 1).r) +
                                          Mathf.Abs(currentHeight - inputTexture.GetPixel(x, y + 1).r);

                        float gradientMagnitude = gradientX + gradientY;
                        float verticalGradient =
                            Mathf.Abs(inputTexture.GetPixel(x, y - 1).r - inputTexture.GetPixel(x, y + 1).r);

                        bool isSlopeTerrace = false;
                        bool isFlatArea = false;
                        bool isMountainOrHeight = false;
                        bool isTerrace = false;

                        if (gradientMagnitude < slopeTerraceThreshold && verticalGradient < verticalGradientThreshold)
                        {
                            float leftHeight = inputTexture.GetPixel(x - 1, y).r;
                            float rightHeight = inputTexture.GetPixel(x + 1, y).r;
                            float topHeight = inputTexture.GetPixel(x, y - 1).r;
                            float bottomHeight = inputTexture.GetPixel(x, y + 1).r;

                            if (Mathf.Abs(leftHeight - currentHeight) < slopeTerraceThreshold &&
                                Mathf.Abs(rightHeight - currentHeight) < slopeTerraceThreshold &&
                                Mathf.Abs(topHeight - currentHeight) < slopeTerraceThreshold &&
                                Mathf.Abs(bottomHeight - currentHeight) < slopeTerraceThreshold)
                            {
                                isSlopeTerrace = true;
                            }
                        }

                        if (gradientMagnitude < flatThreshold)
                        {
                            isFlatArea = true;
                        }

                        if (verticalGradient > verticalGradientThreshold)
                        {
                            isMountainOrHeight = true;
                        }

                        if (verticalGradient >= minTerraceThreshold && verticalGradient <= maxTerraceThreshold)
                        {
                            isTerrace = true;
                        }

                        if (isTerrace)
                        {
                            importantDetailsMask.SetPixel(x, y, Color.green);
                            analyzedTexture.SetPixel(x, y, Color.green);
                        }
                        else if (isFlatArea)
                        {
                            importantDetailsMask.SetPixel(x, y, Color.black);
                            analyzedTexture.SetPixel(x, y, Color.black);
                        }
                        else if (isMountainOrHeight)
                        {
                            importantDetailsMask.SetPixel(x, y, Color.red);
                            analyzedTexture.SetPixel(x, y, Color.red);
                        }
                        else if (isSlopeTerrace)
                        {
                            importantDetailsMask.SetPixel(x, y, Color.blue);
                            analyzedTexture.SetPixel(x, y, Color.blue);
                        }
                        else
                        {
                            importantDetailsMask.SetPixel(x, y, Color.black);
                            analyzedTexture.SetPixel(x, y, Color.black);
                        }
                    }
                }

                importantDetailsMask.Apply();
                analyzedTexture.Apply();
                isAnalyzed = true;

                if (auto)
                {
                    if (inputTexture)
                    {
                        outputTexture = ProcessHeightmap(inputTexture, EWorkflow.TerrainWithTexture);

                        if (exportExrTerrainWorkflow)
                        {
                            // Save the export texture
                            SaveTexture(outputTexture);
                        }
                    }
                }
                else
                {
                    outputTexture = ProcessHeightmap(inputTexture, EWorkflow.TerrainWithTexture);
                    tempOutputTexture = outputTexture;
                    outputTexture = null;
                }
            }

            if (inputTerrain)
            {
                inputTexture = ExtractHeightmap();
            }

            importantDetailsMask =
                new Texture2D(inputTexture.width, inputTexture.height, TextureFormat.RGBAFloat, false);
            analyzedTexture = new Texture2D(inputTexture.width, inputTexture.height, TextureFormat.RGBAFloat, false);

            // First pass: classify pixels
            for (int y = 1; y < inputTexture.height - 1; y++)
            {
                for (int x = 1; x < inputTexture.width - 1; x++)
                {
                    float currentHeight = inputTexture.GetPixel(x, y).r;

                    float gradientX = Mathf.Abs(currentHeight - inputTexture.GetPixel(x - 1, y).r) +
                                      Mathf.Abs(currentHeight - inputTexture.GetPixel(x + 1, y).r);
                    float gradientY = Mathf.Abs(currentHeight - inputTexture.GetPixel(x, y - 1).r) +
                                      Mathf.Abs(currentHeight - inputTexture.GetPixel(x, y + 1).r);

                    float gradientMagnitude = gradientX + gradientY;
                    float verticalGradient =
                        Mathf.Abs(inputTexture.GetPixel(x, y - 1).r - inputTexture.GetPixel(x, y + 1).r);

                    bool isSlopeTerrace = false;
                    bool isFlatArea = false;
                    bool isMountainOrHeight = false;
                    bool isTerrace = false;

                    if (gradientMagnitude < slopeTerraceThreshold && verticalGradient < verticalGradientThreshold)
                    {
                        float leftHeight = inputTexture.GetPixel(x - 1, y).r;
                        float rightHeight = inputTexture.GetPixel(x + 1, y).r;
                        float topHeight = inputTexture.GetPixel(x, y - 1).r;
                        float bottomHeight = inputTexture.GetPixel(x, y + 1).r;

                        if (Mathf.Abs(leftHeight - currentHeight) < slopeTerraceThreshold &&
                            Mathf.Abs(rightHeight - currentHeight) < slopeTerraceThreshold &&
                            Mathf.Abs(topHeight - currentHeight) < slopeTerraceThreshold &&
                            Mathf.Abs(bottomHeight - currentHeight) < slopeTerraceThreshold)
                        {
                            isSlopeTerrace = true;
                        }
                    }

                    if (gradientMagnitude < flatThreshold)
                    {
                        isFlatArea = true;
                    }

                    if (verticalGradient > verticalGradientThreshold)
                    {
                        isMountainOrHeight = true;
                    }

                    if (verticalGradient >= minTerraceThreshold && verticalGradient <= maxTerraceThreshold)
                    {
                        isTerrace = true;
                    }

                    if (isTerrace)
                    {
                        importantDetailsMask.SetPixel(x, y, Color.green);
                        analyzedTexture.SetPixel(x, y, Color.green);
                    }
                    else if (isFlatArea)
                    {
                        importantDetailsMask.SetPixel(x, y, Color.black);
                        analyzedTexture.SetPixel(x, y, Color.black);
                    }
                    else if (isMountainOrHeight)
                    {
                        importantDetailsMask.SetPixel(x, y, Color.red);
                        analyzedTexture.SetPixel(x, y, Color.red);
                    }
                    else if (isSlopeTerrace)
                    {
                        importantDetailsMask.SetPixel(x, y, Color.blue);
                        analyzedTexture.SetPixel(x, y, Color.blue);
                    }
                    else
                    {
                        importantDetailsMask.SetPixel(x, y, Color.black);
                        analyzedTexture.SetPixel(x, y, Color.black);
                    }
                }
            }

            importantDetailsMask.Apply();
            analyzedTexture.Apply();
            isAnalyzed = true;

            if (auto)
            {
                if (inputTexture)
                {
                    if (terrainWorkflow)
                    {
                        outputTexture = ProcessHeightmap(inputTexture, EWorkflow.Terrain);
                    }
                    else
                    {
                        outputTexture = ProcessHeightmap(inputTexture, EWorkflow.Texture);
                    }

                    if (outputTexture && !terrainWorkflow && !exportExrTerrainWorkflow)
                    {
                        SaveTexture(outputTexture);
                    }
                    else if (outputTexture && terrainWorkflow)
                    {
                        ApplyHeightmapToTerrain(outputTexture);
                    }
                }
            }
        }

        private Texture2D FlipHeightmap(Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;
            Texture2D flippedTexture = new Texture2D(width, height, texture.format, false);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Flip both vertically and horizontally
                    flippedTexture.SetPixel(width - 1 - x, height - 1 - y, texture.GetPixel(x, y));
                }
            }

            flippedTexture.Apply();
            return flippedTexture;
        }


        private Texture2D ProcessHeightmap(Texture2D texture, EWorkflow workflow)
        {
            int width = texture.width;
            int height = texture.height;
            Texture2D newTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
            Color[] pixels = texture.GetPixels();
            Color[] importantDetailsPixels = importantDetailsMask.GetPixels();

            float[,] heightData = ExtractHeightData(pixels, width, height);
            bool[,] terraceMask = DetectTerraces(heightData, width, height, importantDetailsPixels);
            heightData = AdaptiveSmoothing(heightData, terraceMask, width, height);
            heightData = GradientAwareNoise(heightData, terraceMask, width, height, workflow);
            heightData = MultiDirectionalFiltering(heightData, width, height);

            // Additional smoothing stage
            heightData = GaussianBlur(heightData, width, height);

            pixels = ApplyHeightDataToPixels(heightData, width, height);
            newTexture.SetPixels(pixels);
            newTexture.Apply();
            return newTexture;
        }

        private float[,] ExtractHeightData(Color[] pixels, int width, int height)
        {
            float[,] heightData = new float[width, height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                heightData[x, y] = pixels[y * width + x].r;
            return heightData;
        }

        private bool[,] DetectTerraces(float[,] heightData, int width, int height, Color[] importantDetailsPixels)
        {
            bool[,] mask = new bool[width, height];
            for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                // Check if the pixel is marked as important detail (red in the mask) or flat area (black in the mask)
                if ((excludeRed && importantDetailsPixels[y * width + x] == Color.red) ||
                    (excludeBlack && importantDetailsPixels[y * width + x] == Color.black))
                {
                    mask[x, y] = false;
                    continue;
                }

                mask[x, y] = Mathf.Abs(heightData[x, y] - heightData[x + 1, y]) < 0.01f &&
                             Mathf.Abs(heightData[x, y] - heightData[x, y + 1]) < 0.01f;
            }

            return mask;
        }

        private float[,] AdaptiveSmoothing(float[,] heightData, bool[,] mask, int width, int height)
        {
            float[,] smoothed = (float[,])heightData.Clone();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    if (mask[x, y])
                    {
                        float sum = 0f;
                        int count = 0;

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                sum += heightData[nx, ny];
                                count++;
                            }
                        }

                        smoothed[x, y] = sum / count; // Average 3x3 smoothing
                    }
                }
            }

            return smoothed;
        }

        private float[,] GradientAwareNoise(float[,] heightData, bool[,] mask, int width, int height,
            EWorkflow workflow)
        {
            float[,] modified = (float[,])heightData.Clone();

            // Calculate the ratio between the hardcoded value and the default perlinStrength
            float defaultPerlinStrength = 0.0035f;
            float terrainPerlinStrength = 0.00042f;
            float ratio = terrainPerlinStrength / defaultPerlinStrength;

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    if (mask[x, y])
                    {
                        // Compute gradient (difference between current pixel and its neighbors)
                        float dx = Mathf.Abs(heightData[x + 1, y] - heightData[x - 1, y]);
                        float dy = Mathf.Abs(heightData[x, y + 1] - heightData[x, y - 1]);
                        float gradient = Mathf.Sqrt(dx * dx + dy * dy);

                        float adaptiveStrength = 0f;

                        switch (workflow)
                        {
                            case EWorkflow.Terrain:
                                // Adaptive noise strength based on gradient for Terrain workflow
                                adaptiveStrength = perlinStrength * ratio * (1f - Mathf.Clamp01(gradient * 10f));
                                break;
                            case EWorkflow.TerrainWithTexture:
                                // Adaptive noise strength based on gradient for TerrainWithTexture workflow
                                adaptiveStrength = perlinStrength * ratio * (1f - Mathf.Clamp01(gradient * 10f));
                                break;
                            case EWorkflow.Texture:
                                // Adaptive noise strength based on gradient for Texture workflow
                                adaptiveStrength = perlinStrength * (1f - Mathf.Clamp01(gradient * 10f));
                                break;
                        }

                        // Apply Perlin noise
                        float noise = Mathf.PerlinNoise(x * perlinScale, y * perlinScale) * adaptiveStrength;
                        modified[x, y] += noise;
                    }
                }
            }

            return modified;
        }


        private float[,] MultiDirectionalFiltering(float[,] heightData, int width, int height)
        {
            float[,] filtered = (float[,])heightData.Clone();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float sum = 0f;
                    float weightSum = 0f;

                    // Gaussian-style weighted average for smoother blending
                    float[,] weights =
                    {
                        { 1, 2, 1 },
                        { 2, 4, 2 },
                        { 1, 2, 1 }
                    };

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            float weight = weights[dy + 1, dx + 1];

                            sum += heightData[nx, ny] * weight;
                            weightSum += weight;
                        }
                    }

                    filtered[x, y] = sum / weightSum;
                }
            }

            return filtered;
        }

        private float[,] GaussianBlur(float[,] heightData, int width, int height)
        {
            float[,] blurred = (float[,])heightData.Clone();
            int kernelSize = 5; // Size of the Gaussian kernel
            float sigma = 1.0f; // Standard deviation for the Gaussian kernel
            float[,] kernel = GenerateGaussianKernel(kernelSize, sigma);

            int halfKernel = kernelSize / 2;

            for (int y = halfKernel; y < height - halfKernel; y++)
            {
                for (int x = halfKernel; x < width - halfKernel; x++)
                {
                    float sum = 0f;
                    float weightSum = 0f;

                    for (int ky = -halfKernel; ky <= halfKernel; ky++)
                    {
                        for (int kx = -halfKernel; kx <= halfKernel; kx++)
                        {
                            int nx = x + kx;
                            int ny = y + ky;
                            float weight = kernel[ky + halfKernel, kx + halfKernel];

                            sum += heightData[nx, ny] * weight;
                            weightSum += weight;
                        }
                    }

                    blurred[x, y] = sum / weightSum;
                }
            }

            return blurred;
        }

        private float[,] GenerateGaussianKernel(int kernelSize, float sigma)
        {
            float[,] kernel = new float[kernelSize, kernelSize];
            float sum = 0f;

            int halfKernel = kernelSize / 2;

            for (int y = -halfKernel; y <= halfKernel; y++)
            {
                for (int x = -halfKernel; x <= halfKernel; x++)
                {
                    kernel[y + halfKernel, x + halfKernel] = Mathf.Exp(-(x * x + y * y) / (2 * sigma * sigma));
                    sum += kernel[y + halfKernel, x + halfKernel];
                }
            }

            // Normalize the kernel
            for (int y = 0; y < kernelSize; y++)
            {
                for (int x = 0; x < kernelSize; x++)
                {
                    kernel[y, x] /= sum;
                }
            }

            return kernel;
        }

        private Color[] ApplyHeightDataToPixels(float[,] heightData, int width, int height)
        {
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color(heightData[x, y], 0, 0);
            //pixels[y * width + x] = new Color(heightData[x, y], heightData[x, y], heightData[x, y]);
            return pixels;
        }

        private Texture2D ExtractHeightmap()
        {
            // Get the TerrainData from the selected terrain
            TerrainData terrainData = inputTerrain.terrainData;

            // Get the resolution of the heightmap
            int heightmapWidth = terrainData.heightmapResolution;
            int heightmapHeight = terrainData.heightmapResolution;

            // Create a new Texture2D to store the heightmap as an image
            Texture2D heightmapTexture = new Texture2D(heightmapWidth, heightmapHeight, TextureFormat.RGBAFloat, false);

            // Retrieve the heightmap data as a 2D array of floats
            float[,] heightmapData = terrainData.GetHeights(0, 0, heightmapWidth, heightmapHeight);

            // Flip the heightmap data to account for Unity's terrain coordinate system
            float[,] flippedHeightmapData = new float[heightmapWidth, heightmapHeight];
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    flippedHeightmapData[x, heightmapHeight - 1 - y] = heightmapData[x, y];
                }
            }

            float[,] rotatedHeightmapData = new float[heightmapWidth, heightmapHeight];
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    rotatedHeightmapData[y, heightmapHeight - 1 - x] = flippedHeightmapData[x, y];
                }
            }

            Color[] pixels = new Color[heightmapWidth * heightmapHeight];
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    float heightValue = rotatedHeightmapData[x, y];
                    pixels[y * heightmapWidth + x] = new Color(heightValue, 0, 0, 1);
                }
            }

            heightmapTexture.SetPixels(pixels);

            // Apply the changes to the texture
            heightmapTexture.Apply();

            return heightmapTexture;
        }

// New method for normalization
        private void NormalizeHeightmap(float[,] heightData, Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float heightValue = heightData[x, y];
                    if (heightValue < minHeight) minHeight = heightValue;
                    if (heightValue > maxHeight) maxHeight = heightValue;
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float heightValue = heightData[x, y];
                    float normalizedHeight = (heightValue - minHeight) / (maxHeight - minHeight);
                    Color pixelColor = new Color(normalizedHeight, 0, 0, 1); // Red channel only
                    texture.SetPixel(x, y, pixelColor);
                }
            }
        }

        private void SaveTexture(Texture2D texture)
        {
            if (inputTexture == null)
            {
                Debug.LogError("No input texture assigned.");
                return;
            }

            string inputPath;
            string directory;
            string fileNameWithoutExtension;
            string newFileName;
            string path;

            if (terrainWorkflow)
            {
                inputPath = AssetDatabase.GetAssetPath(inputTerrain.terrainData);
                directory = outputPath;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
                newFileName = $"{fileNameWithoutExtension}_Processed.exr";
                path = Path.Combine(directory, newFileName);
            }
            else
            {
                inputPath = AssetDatabase.GetAssetPath(inputTexture);
                directory = Path.GetDirectoryName(inputPath);
                fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
                newFileName = $"{fileNameWithoutExtension}_Processed.exr";
                path = Path.Combine(directory, newFileName);
            }

            // Check for existing files and append a number if necessary
            int fileCounter = 0;
            while (File.Exists(path))
            {
                fileCounter++;
                newFileName = $"{fileNameWithoutExtension}_Processed_{fileCounter}.exr";
                path = Path.Combine(directory, newFileName);
            }

            // Flip the texture if using terrain workflow
            if (terrainWorkflow)
            {
                texture = FlipHeightmap(texture);
            }

            if (terrainWorkflow)
            {
                if (exportExrTerrainWorkflow)
                {
                    File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                    AssetDatabase.Refresh();

                    // Change import settings to RFloat
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.sRGBTexture = false;
                        importer.alphaSource = TextureImporterAlphaSource.None;
                        importer.wrapMode = TextureWrapMode.Clamp;

                        // Get input texture import settings
                        TextureImporter inputImporter = AssetImporter.GetAtPath(inputPath) as TextureImporter;
                        if (inputImporter != null)
                        {
                            importer.maxTextureSize = inputImporter.maxTextureSize;
                        }

                        // Set platform settings for RFloat
                        TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings
                        {
                            maxTextureSize = importer.maxTextureSize,
                            format = TextureImporterFormat.RFloat,
                            overridden = true
                        };

                        // Override the format to RFloat
                        importer.SetPlatformTextureSettings(platformSettings);
                        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                        {
                            format = TextureImporterFormat.RFloat,
                            name = "Standalone"
                        });

                        importer.SaveAndReimport();
                    }
                }
            }

            if (!terrainWorkflow)
            {
                File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                AssetDatabase.Refresh();

                // Change import settings to RFloat
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.sRGBTexture = false;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.wrapMode = TextureWrapMode.Clamp;

                    // Get input texture import settings
                    TextureImporter inputImporter = AssetImporter.GetAtPath(inputPath) as TextureImporter;
                    if (inputImporter != null)
                    {
                        importer.maxTextureSize = inputImporter.maxTextureSize;
                    }

                    // Set platform settings for RFloat
                    TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings
                    {
                        maxTextureSize = importer.maxTextureSize,
                        format = TextureImporterFormat.RFloat,
                        overridden = true
                    };

                    // Override the format to RFloat
                    importer.SetPlatformTextureSettings(platformSettings);
                    importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    {
                        format = TextureImporterFormat.RFloat,
                        name = "Standalone"
                    });

                    importer.SaveAndReimport();
                }
            }
        }

        private void ApplyHeightmapToTerrain(Texture2D texture)
        {
            if (inputTerrain == null)
            {
                Debug.LogError("No terrain assigned.");
                return;
            }

            TerrainData terrainData = inputTerrain.terrainData;

            int terrainWidth = terrainData.heightmapResolution;
            int terrainHeight = terrainData.heightmapResolution;

            int heightmapWidth = texture.width;
            int heightmapHeight = texture.height;

            if (heightmapWidth != terrainWidth || heightmapHeight != terrainHeight)
            {
                Debug.LogError(
                    $"Heightmap resolution mismatch! Expected {terrainWidth}x{terrainHeight}, but got {heightmapWidth}x{heightmapHeight}.");
                return;
            }

            if (heightmapWidth <= 0 || heightmapHeight <= 0)
            {
                Debug.LogError("Invalid heightmap dimensions.");
                return;
            }

            float[,] temporaryHeightData =
                ExtractHeightData(outputTexture.GetPixels(), heightmapWidth, heightmapHeight);

            float[,] flippedHeightmapData = new float[heightmapWidth, heightmapHeight];
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    flippedHeightmapData[x, heightmapHeight - 1 - y] = temporaryHeightData[x, y];
                }
            }

            float[,] rotatedHeightmapData = new float[heightmapWidth, heightmapHeight];
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    rotatedHeightmapData[y, heightmapHeight - 1 - x] = flippedHeightmapData[x, y];
                }
            }

            terrainData.SetHeights(0, 0, rotatedHeightmapData);
        }


    }
}