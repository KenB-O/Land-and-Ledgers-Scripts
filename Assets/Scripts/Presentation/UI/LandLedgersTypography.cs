using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine.TextCore.LowLevel;
#endif

namespace LandLedgers.UI
{
    public static class LandLedgersTypography
    {
#if UNITY_EDITOR
        static LandLedgersTypography()
        {
            EditorApplication.delayCall += EnsureAllFontAssets;
        }
#endif
        public enum FontRole
        {
            BitterRegular,
            BitterMedium,
            BitterSemiBold,
            BitterBold,
            BitterBlack,
            SourceSansRegular,
            SourceSansSemiBold,
            SourceSansBold
        }

        public enum TextRole
        {
            ScreenTitle,
            ModalTitle,
            PanelHeader,
            SectionHeader,
            TabLabel,
            ButtonLabel,
            ImportantLabel,
            FeaturedValue,
            NoticeTitle,
            Body,
            DenseBody,
            DenseValue,
            Badge,
            TooltipTitle,
            TooltipBody,
            LogBody,
            HelperText,
            Metadata,
            HudLabel,
            HudUtility
        }

        public static readonly FontRole[] RequiredRoles =
        {
            FontRole.BitterRegular,
            FontRole.BitterMedium,
            FontRole.BitterSemiBold,
            FontRole.BitterBold,
            FontRole.BitterBlack,
            FontRole.SourceSansRegular,
            FontRole.SourceSansSemiBold,
            FontRole.SourceSansBold
        };

        private const string BitterRegularName = "Bitter Regular SDF";
        private const string BitterMediumName = "Bitter Medium SDF";
        private const string BitterSemiBoldName = "Bitter SemiBold SDF";
        private const string BitterBoldName = "Bitter Bold SDF";
        private const string BitterBlackName = "Bitter Black SDF";
        private const string SourceRegularName = "Source Sans 3 Regular SDF";
        private const string SourceSemiBoldName = "Source Sans 3 SemiBold SDF";
        private const string SourceBoldName = "Source Sans 3 Bold SDF";

        private static readonly Dictionary<FontRole, TMP_FontAsset> FontCache = new();

        public static TMP_FontAsset GetFont(FontRole role)
        {
            if (FontCache.TryGetValue(role, out TMP_FontAsset cached) && cached != null)
            {
                return cached;
            }

            TMP_FontAsset resolved = null;
#if !UNITY_EDITOR
            resolved = Resources.Load<TMP_FontAsset>($"Core/UI/Fonts/{FontName(role)}");
#endif
#if UNITY_EDITOR
            // Editor smoke tests and prefab repair run before these generated assets
            // are resident in Resources. Resolve the authored asset directly first.
            resolved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(role));
            if (!IsUsableFont(resolved))
            {
                // The first editor invocation can arrive before the delayed
                // bootstrap callback. Generate/load the deterministic project
                // asset synchronously instead of falling back to Liberation Sans.
                resolved = EnsureFontAsset(role);
            }
#endif
            resolved = IsUsableFont(resolved) ? resolved : FindLoadedFont(FontName(role));
            if (!IsUsableFont(resolved))
            {
                resolved = GetSafeDefaultFont();
            }

            if (resolved != null)
            {
                FontCache[role] = resolved;
            }

            return resolved;
        }

        public static void ApplyRole(TMP_Text text, TextRole role)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset font = GetFont(FontForRole(role));
            if (font != null)
            {
                text.font = font;
            }

            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
        }

        public static void ApplyRole(TMP_Text text, TextRole role, float fontSize)
        {
            ApplyRole(text, role);
            if (text != null)
            {
                text.fontSize = fontSize;
            }
        }

        public static void ApplyButtonLabel(Button button)
        {
            if (button == null)
            {
                return;
            }

            ApplyRole(button.GetComponentInChildren<TMP_Text>(true), TextRole.ButtonLabel);
        }

        public static FontRole FontForRole(TextRole role)
        {
            return role switch
            {
                TextRole.ScreenTitle => FontRole.BitterBold,
                TextRole.ModalTitle => FontRole.BitterBold,
                TextRole.PanelHeader => FontRole.BitterSemiBold,
                TextRole.SectionHeader => FontRole.BitterSemiBold,
                TextRole.TabLabel => FontRole.BitterSemiBold,
                TextRole.ButtonLabel => FontRole.BitterSemiBold,
                TextRole.ImportantLabel => FontRole.BitterSemiBold,
                TextRole.FeaturedValue => FontRole.BitterBold,
                TextRole.NoticeTitle => FontRole.BitterSemiBold,
                TextRole.TooltipTitle => FontRole.BitterSemiBold,
                TextRole.HudLabel => FontRole.BitterSemiBold,
                TextRole.Badge => FontRole.SourceSansBold,
                TextRole.DenseValue => FontRole.SourceSansSemiBold,
                TextRole.HudUtility => FontRole.SourceSansSemiBold,
                TextRole.Body => FontRole.SourceSansRegular,
                TextRole.DenseBody => FontRole.SourceSansRegular,
                TextRole.TooltipBody => FontRole.SourceSansRegular,
                TextRole.LogBody => FontRole.SourceSansRegular,
                TextRole.HelperText => FontRole.SourceSansRegular,
                TextRole.Metadata => FontRole.SourceSansRegular,
                _ => FontRole.SourceSansRegular
            };
        }

        private static TMP_FontAsset FindLoadedFont(string fontName)
        {
            TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
            if (IsUsableFont(defaultFont) && string.Equals(defaultFont.name, fontName, StringComparison.Ordinal))
            {
                return defaultFont;
            }

            IReadOnlyList<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Count; i++)
                {
                    TMP_FontAsset fallback = fallbacks[i];
                    if (IsUsableFont(fallback) && string.Equals(fallback.name, fontName, StringComparison.Ordinal))
                    {
                        return fallback;
                    }
                }
            }

            TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < loadedFonts.Length; i++)
            {
                TMP_FontAsset font = loadedFonts[i];
                if (IsUsableFont(font) && string.Equals(font.name, fontName, StringComparison.Ordinal))
                {
                    return font;
                }
            }

            return null;
        }

        private static TMP_FontAsset GetSafeDefaultFont()
        {
            TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
            if (IsUsableFont(defaultFont))
            {
                return defaultFont;
            }

            TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < loadedFonts.Length; i++)
            {
                if (IsUsableFont(loadedFonts[i]))
                {
                    return loadedFonts[i];
                }
            }

            return null;
        }

        private static bool IsUsableFont(TMP_FontAsset font)
        {
            if (font == null)
            {
                return false;
            }

            Texture2D[] atlasTextures = font.atlasTextures;
            if (atlasTextures == null || atlasTextures.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < atlasTextures.Length; i++)
            {
                if (atlasTextures[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static string FontName(FontRole role)
        {
            return role switch
            {
                FontRole.BitterRegular => BitterRegularName,
                FontRole.BitterMedium => BitterMediumName,
                FontRole.BitterSemiBold => BitterSemiBoldName,
                FontRole.BitterBold => BitterBoldName,
                FontRole.BitterBlack => BitterBlackName,
                FontRole.SourceSansRegular => SourceRegularName,
                FontRole.SourceSansSemiBold => SourceSemiBoldName,
                FontRole.SourceSansBold => SourceBoldName,
                _ => BitterRegularName
            };
        }

#if UNITY_EDITOR
        private const string FontAssetFolder = "Assets/Resources/Core/UI/Fonts";
        private const string TmpSettingsPath = "Assets/Asset Packs/TextMesh Pro/Resources/TMP Settings.asset";
        private const string MainUiPrefabPath = "Assets/Core/UI/UI Canvas.prefab";

        [MenuItem("Land & Ledgers/UI/Repair Typography")]
        public static void RepairTypography()
        {
            ConfigureTmpSettings();
            ApplyTypographyToMainUiPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void ApplyTypographyToMainUiPrefab()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(MainUiPrefabPath);
            try
            {
                TMP_Text[] texts = prefabRoot.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    TMP_Text text = texts[i];
                    ApplyRole(text, ResolveRoleFromObjectName(text.name));
                    EditorUtility.SetDirty(text);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, MainUiPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        public static void EnsureAllFontAssets()
        {
            EnsureFolder(FontAssetFolder);
            for (int i = 0; i < RequiredRoles.Length; i++)
            {
                EnsureFontAsset(RequiredRoles[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void ConfigureTmpSettings()
        {
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                return;
            }

            SerializedObject serialized = new(settings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = LoadLiberationSans();
            serialized.FindProperty("m_defaultFontAssetPath").stringValue = "Fonts & Materials/";

            SerializedProperty fallbacks = serialized.FindProperty("m_fallbackFontAssets");
            fallbacks.ClearArray();

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        public static TMP_FontAsset EnsureFontAsset(FontRole role)
        {
            string assetPath = AssetPath(role);
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (IsUsableFont(existing))
            {
                FontCache[role] = existing;
                return existing;
            }

            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            if (!TryGenerateFontAsset(role, assetPath))
            {
                Debug.LogWarning($"Missing TMP font source for '{FontName(role)}'. Expected the authored Land & Ledgers font pack under Assets/Asset Packs/Fonts.");
                return null;
            }

            TMP_FontAsset generated = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (IsUsableFont(generated))
            {
                FontCache[role] = generated;
                return generated;
            }

            return null;
        }

        private static bool TryGenerateFontAsset(FontRole role, string assetPath)
        {
            string sourcePath = role switch
            {
                FontRole.BitterRegular => "Assets/Asset Packs/Fonts/Bitter/static/Bitter-Regular.ttf",
                FontRole.BitterMedium => "Assets/Asset Packs/Fonts/Bitter/static/Bitter-Medium.ttf",
                FontRole.BitterSemiBold => "Assets/Asset Packs/Fonts/Bitter/static/Bitter-SemiBold.ttf",
                FontRole.BitterBold => "Assets/Asset Packs/Fonts/Bitter/static/Bitter-Bold.ttf",
                FontRole.BitterBlack => "Assets/Asset Packs/Fonts/Bitter/static/Bitter-Black.ttf",
                FontRole.SourceSansRegular => "Assets/Asset Packs/Fonts/Source_Sans_3/static/SourceSans3-Regular.ttf",
                FontRole.SourceSansSemiBold => "Assets/Asset Packs/Fonts/Source_Sans_3/static/SourceSans3-SemiBold.ttf",
                FontRole.SourceSansBold => "Assets/Asset Packs/Fonts/Source_Sans_3/static/SourceSans3-Bold.ttf",
                _ => string.Empty
            };

            Font source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null)
            {
                return false;
            }

            TMP_FontAsset generated = TMP_FontAsset.CreateFontAsset(
                source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
            if (generated == null)
            {
                return false;
            }

            generated.name = FontName(role);
            Texture2D atlas = generated.atlasTexture;
            Material material = generated.material;
            atlas.name = generated.name + " Atlas";
            material.name = generated.name + " Material";
            AssetDatabase.CreateAsset(generated, assetPath);
            AssetDatabase.AddObjectToAsset(atlas, generated);
            AssetDatabase.AddObjectToAsset(material, generated);

            uint[] characters = new uint[95];
            for (uint i = 0; i < characters.Length; i++)
            {
                characters[i] = i + 32;
            }

            if (!generated.TryAddCharacters(characters, out _))
            {
                AssetDatabase.DeleteAsset(assetPath);
                return false;
            }

            MethodInfo flush = typeof(TMP_FontAsset).GetMethod(
                "UpdateAtlasTexturesInQueue", BindingFlags.Static | BindingFlags.NonPublic);
            flush?.Invoke(null, null);
            EditorUtility.SetDirty(generated);
            EditorUtility.SetDirty(atlas);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static TMP_FontAsset LoadLiberationSans()
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/Asset Packs/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        }

        private static string AssetPath(FontRole role)
        {
            return $"{FontAssetFolder}/{FontName(role)}.asset";
        }

        private static TextRole ResolveRoleFromObjectName(string objectName)
        {
            if (objectName.Contains("Cash", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("NetWorth", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.FeaturedValue;
            }

            if (objectName.Contains("Title", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.PanelHeader;
            }

            if (objectName.Contains("Tab", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.TabLabel;
            }

            if (objectName.Contains("Button", StringComparison.OrdinalIgnoreCase)
                || objectName.Equals("Label", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.ButtonLabel;
            }

            if (objectName.Contains("Alert", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Status", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.Badge;
            }

            if (objectName.Contains("Hint", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Help", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Fallback", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.HelperText;
            }

            if (objectName.Contains("History", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Ledger", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Log", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.LogBody;
            }

            if (objectName.Contains("Finance", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Stock", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Staffing", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Acquisition", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Summary", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("Overview", StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.DenseBody;
            }

            return TextRole.Body;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
#endif
    }
}
