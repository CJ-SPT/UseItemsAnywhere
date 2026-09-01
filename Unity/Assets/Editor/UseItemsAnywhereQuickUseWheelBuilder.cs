using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class UseItemsAnywhereQuickUseWheelBuilder
{
    private const string RootFolder = "Assets/Mods/UseItemsAnywhere.Assets";
    private const string UiFolder = RootFolder + "/UI";
    private const string CirclePath = UiFolder + "/WheelCircle.png";
    private const string StripePath = UiFolder + "/EftDiagonalStripe.png";
    private const string PrefabPath = UiFolder + "/QuickUseWheel.prefab";
    private const string TimerPrefabPath = UiFolder + "/ItemUseDelayTimer.prefab";
    private const string BundleName = "quickusewheel";
    private const string TimerBundleName = "itemusedelaytimer";

    [MenuItem("SDK/R.A.T./Build UI Bundles")]
    internal static void BuildFromMenu() => Build();

    public static void Build()
    {
        Directory.CreateDirectory(UiFolder);
        CreateCircleSprite(CirclePath);
        CreateStripeSprite(StripePath);
        var circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
        var stripe = AssetDatabase.LoadAssetAtPath<Sprite>(StripePath);
        if (circle == null || stripe == null)
        {
            throw new InvalidOperationException("Failed to create the UI sprites.");
        }

        CreatePrefab(circle);
        CreateItemUseDelayTimerPrefab(stripe);
        var importer = AssetImporter.GetAtPath(PrefabPath);
        importer.assetBundleName = BundleName;
        importer.SaveAndReimport();
        var timerImporter = AssetImporter.GetAtPath(TimerPrefabPath);
        timerImporter.assetBundleName = TimerBundleName;
        timerImporter.SaveAndReimport();

        var output = GetArgument("-quickUseWheelOutput");
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/RAT"));
        }

        Directory.CreateDirectory(output);
        var builds = new[]
        {
            new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[] { PrefabPath, CirclePath },
            },
            new AssetBundleBuild
            {
                assetBundleName = TimerBundleName,
                assetNames = new[] { TimerPrefabPath },
            },
        };
        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            builds,
            BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            throw new InvalidOperationException("Unity failed to build the quick-use wheel bundle.");
        }

        Debug.Log($"R.A.T. UI bundles built at {output}");
    }

    private static void CreateCircleSprite(string path)
    {
        const int size = 512;
        const int samplesPerAxis = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        var center = size * 0.5f;
        var radiusSquared = center * center;
        var sampleCount = samplesPerAxis * samplesPerAxis;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var coveredSamples = 0;
                for (var sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                {
                    for (var sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                    {
                        var offsetX = x + (sampleX + 0.5f) / samplesPerAxis - center;
                        var offsetY = y + (sampleY + 0.5f) / samplesPerAxis - center;
                        if (offsetX * offsetX + offsetY * offsetY <= radiusSquared)
                        {
                            coveredSamples++;
                        }
                    }
                }

                var alpha = (byte)Mathf.RoundToInt(coveredSamples / (float)sampleCount * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = size;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void CreateStripeSprite(string path)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var stripe = (x + y) % 24 < 7;
                pixels[y * size + x] = stripe
                    ? new Color32(255, 255, 255, 26)
                    : new Color32(255, 255, 255, 0);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = size;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
    }

    private static void CreatePrefab(Sprite circle)
    {
        var root = new GameObject("QuickUseWheel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        var rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        canvas.pixelPerfect = true;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var backdrop = CreateImage("Backdrop", root.transform, null, new Color(0f, 0f, 0f, 0.32f));
        Stretch(backdrop.rectTransform);

        var wheelRoot = CreateRect("WheelRoot", root.transform, new Vector2(600f, 600f));
        wheelRoot.anchorMin = wheelRoot.anchorMax = wheelRoot.pivot = new Vector2(0.5f, 0.5f);

        var shadow = CreateImage("Shadow", wheelRoot, circle, new Color(0f, 0f, 0f, 0.58f));
        shadow.rectTransform.sizeDelta = new Vector2(568f, 568f);

        var outerFrame = CreateImage("OuterFrame", wheelRoot, circle, new Color(0.3f, 0.32f, 0.32f, 0.9f));
        outerFrame.rectTransform.sizeDelta = new Vector2(558f, 558f);
        var ringBack = CreateImage("RingBack", wheelRoot, circle, new Color(0.025f, 0.027f, 0.027f, 0.97f));
        ringBack.rectTransform.sizeDelta = new Vector2(552f, 552f);

        var segmentLayer = CreateRect("SegmentLayer", wheelRoot, new Vector2(548f, 548f));
        var segment = CreateRect("SegmentTemplate", segmentLayer, new Vector2(548f, 548f));
        Stretch(segment);
        segment.gameObject.SetActive(false);

        var itemLayer = CreateRect("ItemLayer", wheelRoot, new Vector2(548f, 548f));
        var itemTemplate = CreateRect("ItemTemplate", itemLayer, new Vector2(132f, 140f));
        var iconFrame = CreateImage("IconFrame", itemTemplate, null, new Color(0.32f, 0.34f, 0.34f, 0.96f));
        iconFrame.rectTransform.sizeDelta = new Vector2(64f, 64f);
        iconFrame.rectTransform.anchoredPosition = new Vector2(0f, 31f);
        var iconBack = CreateImage("IconBack", iconFrame.transform, null, new Color(0.055f, 0.058f, 0.058f, 0.98f));
        iconBack.rectTransform.sizeDelta = new Vector2(62f, 62f);
        var icon = CreateImage("Icon", iconFrame.transform, null, Color.white);
        icon.rectTransform.sizeDelta = new Vector2(56f, 56f);
        icon.preserveAspect = true;
        var favoriteBadge = CreateImage("FavoriteBadge", iconFrame.transform, null, new Color(0.72f, 0.74f, 0.74f, 0.96f));
        favoriteBadge.rectTransform.anchorMin = favoriteBadge.rectTransform.anchorMax = new Vector2(1f, 1f);
        favoriteBadge.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        favoriteBadge.rectTransform.sizeDelta = new Vector2(29f, 14f);
        favoriteBadge.rectTransform.anchoredPosition = new Vector2(-3f, -3f);
        var favoriteLabel = CreateText("Label", favoriteBadge.transform, 8f, FontStyles.Bold, new Color(0.06f, 0.07f, 0.07f, 1f));
        favoriteLabel.text = "FAV";
        Stretch(favoriteLabel.rectTransform);
        favoriteBadge.gameObject.SetActive(false);
        var itemName = CreateText("Name", itemTemplate, 15f, FontStyles.Bold, new Color(0.82f, 0.83f, 0.8f, 1f));
        itemName.rectTransform.sizeDelta = new Vector2(132f, 24f);
        itemName.rectTransform.anchoredPosition = new Vector2(0f, -14f);
        itemName.enableAutoSizing = true;
        itemName.fontSizeMin = 9f;
        itemName.fontSizeMax = 15f;
        itemName.enableWordWrapping = false;
        itemName.overflowMode = TextOverflowModes.Ellipsis;
        var state = CreateText("State", itemTemplate, 11f, FontStyles.Bold, new Color(0.72f, 0.74f, 0.72f, 1f));
        state.rectTransform.sizeDelta = new Vector2(132f, 18f);
        state.rectTransform.anchoredPosition = new Vector2(0f, -35f);
        state.enableAutoSizing = true;
        state.fontSizeMin = 9f;
        state.fontSizeMax = 11f;
        var source = CreateText("Source", itemTemplate, 10f, FontStyles.Normal, new Color(0.45f, 0.47f, 0.46f, 1f));
        source.rectTransform.sizeDelta = new Vector2(132f, 16f);
        source.rectTransform.anchoredPosition = new Vector2(0f, -52f);
        source.enableAutoSizing = true;
        source.fontSizeMin = 7f;
        source.fontSizeMax = 10f;
        source.enableWordWrapping = false;
        source.overflowMode = TextOverflowModes.Ellipsis;
        itemTemplate.gameObject.SetActive(false);

        var centerShadow = CreateImage("CenterShadow", wheelRoot, circle, new Color(0f, 0f, 0f, 0.78f));
        centerShadow.rectTransform.sizeDelta = new Vector2(214f, 214f);
        var centerBorder = CreateImage("CenterBorder", wheelRoot, circle, new Color(0.34f, 0.36f, 0.36f, 0.96f));
        centerBorder.rectTransform.sizeDelta = new Vector2(206f, 206f);
        var center = CreateImage("Center", wheelRoot, circle, new Color(0.018f, 0.02f, 0.02f, 0.99f));
        center.rectTransform.sizeDelta = new Vector2(202f, 202f);
        var centerHeader = CreateImage("CenterHeader", center.transform, null, new Color(0.13f, 0.15f, 0.16f, 1f));
        centerHeader.rectTransform.sizeDelta = new Vector2(140f, 20f);
        centerHeader.rectTransform.anchoredPosition = new Vector2(0f, 69f);
        var centerHeaderText = CreateText("CenterHeaderText", centerHeader.transform, 9f, FontStyles.Bold, new Color(0.72f, 0.74f, 0.74f, 1f));
        centerHeaderText.text = "QUICK USE";
        Stretch(centerHeaderText.rectTransform);
        var centerRule = CreateImage("CenterRule", center.transform, null, new Color(0.32f, 0.34f, 0.34f, 1f));
        centerRule.rectTransform.sizeDelta = new Vector2(122f, 1f);
        centerRule.rectTransform.anchoredPosition = new Vector2(0f, -18f);
        var selectedName = CreateText("SelectedName", center.transform, 17f, FontStyles.Normal, new Color(0.86f, 0.87f, 0.83f, 1f));
        selectedName.rectTransform.sizeDelta = new Vector2(160f, 50f);
        selectedName.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        selectedName.enableWordWrapping = true;
        selectedName.enableAutoSizing = true;
        selectedName.fontSizeMin = 12f;
        selectedName.fontSizeMax = 17f;
        var cancelHint = CreateText("CancelHint", center.transform, 10f, FontStyles.Normal, new Color(0.48f, 0.5f, 0.49f, 1f));
        cancelHint.text = "CENTER TO CANCEL";
        cancelHint.rectTransform.sizeDelta = new Vector2(176f, 45f);
        cancelHint.rectTransform.anchoredPosition = new Vector2(0f, -47f);
        cancelHint.enableWordWrapping = true;
        cancelHint.enableAutoSizing = true;
        cancelHint.fontSizeMin = 7f;
        cancelHint.fontSizeMax = 10f;

        var detailsShadow = CreateImage("DetailsShadow", root.transform, null, new Color(0f, 0f, 0f, 0.62f));
        detailsShadow.rectTransform.sizeDelta = new Vector2(346f, 216f);
        detailsShadow.rectTransform.anchoredPosition = new Vector2(463f, 17f);
        detailsShadow.gameObject.SetActive(false);
        var detailsPanel = CreateImage("DetailsPanel", root.transform, null, new Color(0.32f, 0.34f, 0.34f, 0.98f));
        detailsPanel.rectTransform.sizeDelta = new Vector2(340f, 210f);
        detailsPanel.rectTransform.anchoredPosition = new Vector2(460f, 20f);
        var detailsBackground = CreateImage("Background", detailsPanel.transform, null, new Color(0.025f, 0.027f, 0.027f, 0.99f));
        detailsBackground.rectTransform.sizeDelta = new Vector2(336f, 206f);
        var detailsHeader = CreateImage("Header", detailsPanel.transform, null, new Color(0.13f, 0.15f, 0.16f, 1f));
        detailsHeader.rectTransform.sizeDelta = new Vector2(336f, 24f);
        detailsHeader.rectTransform.anchoredPosition = new Vector2(0f, 91f);
        var detailsHeaderText = CreateText("Label", detailsHeader.transform, 10f, FontStyles.Bold, new Color(0.72f, 0.74f, 0.74f, 1f));
        detailsHeaderText.text = "SELECTED ITEM";
        detailsHeaderText.alignment = TextAlignmentOptions.Left;
        detailsHeaderText.rectTransform.sizeDelta = new Vector2(304f, 24f);
        var detailsName = CreateText("Name", detailsPanel.transform, 18f, FontStyles.Normal, new Color(0.86f, 0.87f, 0.83f, 1f));
        detailsName.text = "ITEM NAME";
        detailsName.alignment = TextAlignmentOptions.Left;
        detailsName.rectTransform.sizeDelta = new Vector2(304f, 40f);
        detailsName.rectTransform.anchoredPosition = new Vector2(0f, 56f);
        detailsName.enableWordWrapping = true;
        detailsName.enableAutoSizing = true;
        detailsName.fontSizeMin = 13f;
        detailsName.fontSizeMax = 18f;
        var detailsBody = CreateText("Body", detailsPanel.transform, 11f, FontStyles.Normal, new Color(0.58f, 0.6f, 0.59f, 1f));
        detailsBody.text = "STATUS\nSOURCE\nACCESS DETAILS";
        detailsBody.alignment = TextAlignmentOptions.TopLeft;
        detailsBody.rectTransform.sizeDelta = new Vector2(304f, 70f);
        detailsBody.rectTransform.anchoredPosition = new Vector2(0f, -9f);
        detailsBody.enableWordWrapping = true;
        detailsBody.enableAutoSizing = true;
        detailsBody.fontSizeMin = 8f;
        detailsBody.fontSizeMax = 11f;
        var detailsRule = CreateImage("Rule", detailsPanel.transform, null, new Color(0.28f, 0.3f, 0.3f, 1f));
        detailsRule.rectTransform.sizeDelta = new Vector2(304f, 1f);
        detailsRule.rectTransform.anchoredPosition = new Vector2(0f, -58f);
        var detailsAction = CreateText("Action", detailsPanel.transform, 10f, FontStyles.Bold, new Color(0.72f, 0.74f, 0.72f, 1f));
        detailsAction.text = "RELEASE TO USE";
        detailsAction.alignment = TextAlignmentOptions.Left;
        detailsAction.rectTransform.sizeDelta = new Vector2(304f, 30f);
        detailsAction.rectTransform.anchoredPosition = new Vector2(0f, -78f);
        detailsAction.enableWordWrapping = true;
        detailsPanel.gameObject.SetActive(false);

        var pageHint = CreateText("PageHint", root.transform, 12f, FontStyles.Normal, new Color(0.64f, 0.66f, 0.65f, 1f));
        pageHint.rectTransform.anchorMin = pageHint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        pageHint.rectTransform.sizeDelta = new Vector2(760f, 30f);
        pageHint.rectTransform.anchoredPosition = new Vector2(0f, -325f);
        pageHint.gameObject.SetActive(false);

        var controls = CreateText("Controls", root.transform, 11f, FontStyles.Normal, new Color(0.52f, 0.54f, 0.53f, 1f));
        controls.text = "RELEASE TO USE   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CANCEL";
        controls.rectTransform.anchorMin = controls.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        controls.rectTransform.sizeDelta = new Vector2(660f, 28f);
        controls.rectTransform.anchoredPosition = new Vector2(0f, -358f);

        root.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
    }

    private static void CreateItemUseDelayTimerPrefab(Sprite stripe)
    {
        var root = new GameObject("ItemUseDelayTimer", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        Stretch(root.GetComponent<RectTransform>());
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var timerRoot = CreateRect("TimerRoot", root.transform, new Vector2(500f, 128f));
        timerRoot.anchorMin = timerRoot.anchorMax = new Vector2(0.5f, 0f);
        timerRoot.pivot = new Vector2(0.5f, 0f);
        timerRoot.anchoredPosition = new Vector2(0f, 105f);

        var shadow = CreateImage("Shadow", timerRoot, null, new Color(0f, 0f, 0f, 0.62f));
        shadow.rectTransform.sizeDelta = new Vector2(500f, 128f);
        shadow.rectTransform.anchoredPosition = new Vector2(3f, -3f);

        var border = CreateImage("Border", timerRoot, null, new Color(0.32f, 0.34f, 0.34f, 0.98f));
        border.rectTransform.sizeDelta = new Vector2(500f, 128f);
        var panel = CreateImage("Panel", timerRoot, null, new Color(0.025f, 0.027f, 0.027f, 0.99f));
        panel.rectTransform.sizeDelta = new Vector2(496f, 124f);
        var panelStripe = CreateImage("DiagonalHatch", timerRoot, stripe, new Color(0.42f, 0.44f, 0.44f, 0.32f));
        panelStripe.rectTransform.sizeDelta = new Vector2(496f, 124f);
        panelStripe.type = Image.Type.Tiled;

        var contentPanel = CreateImage("ContentPanel", timerRoot, null, new Color(0.015f, 0.016f, 0.016f, 0.88f));
        contentPanel.rectTransform.sizeDelta = new Vector2(392f, 90f);
        contentPanel.rectTransform.anchoredPosition = new Vector2(50f, -11f);

        var headerBand = CreateImage("HeaderBand", timerRoot, null, new Color(0.13f, 0.15f, 0.16f, 1f));
        headerBand.rectTransform.sizeDelta = new Vector2(392f, 22f);
        headerBand.rectTransform.anchoredPosition = new Vector2(50f, 47f);

        var iconBorder = CreateImage("IconFrame", timerRoot, null, new Color(0.35f, 0.37f, 0.37f, 1f));
        iconBorder.rectTransform.sizeDelta = new Vector2(92f, 92f);
        iconBorder.rectTransform.anchoredPosition = new Vector2(-198f, -5f);
        var iconBack = CreateImage("Background", iconBorder.transform, null, new Color(0.055f, 0.058f, 0.058f, 1f));
        iconBack.rectTransform.sizeDelta = new Vector2(88f, 88f);
        var icon = CreateImage("Icon", iconBorder.transform, null, Color.white);
        icon.rectTransform.sizeDelta = new Vector2(76f, 76f);
        icon.preserveAspect = true;

        var eyebrow = CreateText("Eyebrow", timerRoot, 10f, FontStyles.Bold, new Color(0.73f, 0.75f, 0.75f, 1f));
        eyebrow.text = "ACCESSING ITEM";
        eyebrow.alignment = TextAlignmentOptions.Left;
        eyebrow.rectTransform.sizeDelta = new Vector2(350f, 20f);
        eyebrow.rectTransform.anchoredPosition = new Vector2(48f, 47f);

        var itemName = CreateText("ItemName", timerRoot, 18f, FontStyles.Normal, new Color(0.83f, 0.84f, 0.81f, 1f));
        itemName.text = "ITEM NAME";
        itemName.alignment = TextAlignmentOptions.Left;
        itemName.rectTransform.sizeDelta = new Vector2(282f, 28f);
        itemName.rectTransform.anchoredPosition = new Vector2(18f, 22f);
        itemName.enableAutoSizing = true;
        itemName.fontSizeMin = 14f;
        itemName.fontSizeMax = 18f;

        var remaining = CreateText("RemainingTime", timerRoot, 17f, FontStyles.Normal, new Color(0.82f, 0.84f, 0.83f, 1f));
        remaining.text = "0.0s";
        remaining.alignment = TextAlignmentOptions.Right;
        remaining.rectTransform.sizeDelta = new Vector2(88f, 30f);
        remaining.rectTransform.anchoredPosition = new Vector2(198f, 22f);

        var detail = CreateText("Detail", timerRoot, 10f, FontStyles.Normal, new Color(0.49f, 0.51f, 0.5f, 1f));
        detail.text = "BASE ACCESS DELAY  1.5s";
        detail.alignment = TextAlignmentOptions.Left;
        detail.rectTransform.sizeDelta = new Vector2(350f, 20f);
        detail.rectTransform.anchoredPosition = new Vector2(48f, -2f);

        var progressTrackBorder = CreateImage("ProgressTrackBorder", timerRoot, null, new Color(0.35f, 0.37f, 0.37f, 1f));
        progressTrackBorder.rectTransform.sizeDelta = new Vector2(354f, 9f);
        progressTrackBorder.rectTransform.anchoredPosition = new Vector2(48f, -24f);
        var progressTrack = CreateImage("ProgressTrack", timerRoot, null, new Color(0.02f, 0.022f, 0.022f, 1f));
        progressTrack.rectTransform.sizeDelta = new Vector2(350f, 5f);
        progressTrack.rectTransform.anchoredPosition = new Vector2(48f, -24f);
        var progressFill = CreateImage("ProgressFill", progressTrack.transform, null, new Color(0.72f, 0.74f, 0.72f, 1f));
        Stretch(progressFill.rectTransform);
        progressFill.type = Image.Type.Simple;

        var cancelHint = CreateText("CancelHint", timerRoot, 9f, FontStyles.Normal, new Color(0.42f, 0.44f, 0.43f, 1f));
        cancelHint.text = "PRESS KEY TO CANCEL";
        cancelHint.rectTransform.sizeDelta = new Vector2(350f, 34f);
        cancelHint.rectTransform.anchoredPosition = new Vector2(48f, -44f);
        cancelHint.enableWordWrapping = true;
        cancelHint.enableAutoSizing = true;
        cancelHint.fontSizeMin = 7f;
        cancelHint.fontSizeMax = 9f;

        root.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(root, TimerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 size)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rect = CreateRect(name, parent, Vector2.zero);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, float size, FontStyles style, Color color)
    {
        var rect = CreateRect(name, parent, Vector2.zero);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
