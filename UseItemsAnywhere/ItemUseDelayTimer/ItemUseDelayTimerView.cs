using System;
using System.IO;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.ItemUseDelayTimer;

internal sealed class ItemUseDelayTimerView
{
    private const string PrefabPath = "assets/mods/useitemsanywhere.assets/ui/itemusedelaytimer.prefab";
    private const float ExitHoldDuration = 0.08f;
    private const float ExitFadeDuration = 0.18f;

    private AssetBundle? _bundle;
    private GameObject? _uiRoot;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _timerRoot;
    private Image? _icon;
    private Image? _progressFill;
    private TMP_Text? _itemName;
    private TMP_Text? _remainingTime;
    private TMP_Text? _statusText;
    private TMP_Text? _detailText;
    private TMP_Text? _cancelHint;
    private ItemIcon? _itemIcon;
    private RuntimeUiFont? _font;
    private ManualLogSource? _logger;
    private float _duration;
    private bool _visible;
    private bool _exiting;
    private float _exitStartTime;
    private float _exitStartAlpha;
    private Vector3 _exitStartScale;

    internal bool IsAvailable => _uiRoot;

    internal bool IsVisible => _visible;

    internal void Initialize(
        string pluginDirectory,
        ManualLogSource logger,
        Transform persistentParent,
        RuntimeUiFont font)
    {
        _logger = logger;
        var bundlePath = Path.Combine(pluginDirectory, "itemusedelaytimer");
        if (!File.Exists(bundlePath))
        {
            logger.LogError($"Item-use delay timer bundle was not found: {bundlePath}");
            return;
        }

        _bundle = AssetBundle.LoadFromFile(bundlePath);
        if (!_bundle)
        {
            logger.LogError($"Item-use delay timer bundle could not be loaded: {bundlePath}");
            return;
        }

        var prefab = _bundle.LoadAsset<GameObject>(PrefabPath);
        if (!prefab)
        {
            logger.LogError($"Item-use delay timer prefab was not found in {bundlePath}");
            _bundle.Unload(false);
            _bundle = null;
            return;
        }

        _uiRoot = UnityEngine.Object.Instantiate(prefab, persistentParent, false);
        _uiRoot.name = "UseItemsAnywhere_ItemUseDelayTimer";
        _uiRoot.SetActive(false);

        try
        {
            BindPrefab();
        }
        catch (Exception exception)
        {
            logger.LogError($"Item-use delay timer prefab binding failed:\n{exception}");
            UnityEngine.Object.Destroy(_uiRoot);
            _uiRoot = null;
            _bundle.Unload(false);
            _bundle = null;
            return;
        }

        _font = font;
        _font.TryAssign(_uiRoot);
    }

    internal void Show(Item item, Configuration.ItemAccessDelayInfo delayInfo)
    {
        var root = _uiRoot;
        if (!root)
        {
            return;
        }

        _font?.TryAssign(root!);
        _duration = Mathf.Max(delayInfo.TotalDelay, 0.01f);
        _itemName!.text = GetItemName(item);
        _remainingTime!.text = $"{delayInfo.TotalDelay:0.0}s";
        _statusText!.text = $"ACCESSING ITEM  •  {GetSlotName(delayInfo.SourceSlot)}";
        _detailText!.text = GetDelayDetail(delayInfo);
        SetCancelHint();
        _progressFill!.rectTransform.anchorMax = Vector2.one;
        _itemIcon = LoadItemIcon(item);
        _icon!.sprite = _itemIcon?.Sprite;
        _icon.enabled = _icon.sprite;
        _canvasGroup!.alpha = 0f;
        _timerRoot!.localScale = Vector3.one * 0.94f;
        root!.SetActive(true);
        _visible = true;
    }

    internal void Update()
    {
        if (!_visible || !_uiRoot)
        {
            return;
        }

        if (_exiting)
        {
            UpdateExitAnimation();
            return;
        }

        if (!Configuration.ShowTimerPanel.Value)
        {
            HideImmediately();
            return;
        }

        _canvasGroup!.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 1f, Time.unscaledDeltaTime * 12f);
        _timerRoot!.localScale = Vector3.Lerp(_timerRoot.localScale, Vector3.one, Time.unscaledDeltaTime * 18f);

        var loadedSprite = _itemIcon?.Sprite;
        if (!_icon!.sprite && loadedSprite)
        {
            _icon.sprite = loadedSprite;
            _icon.enabled = true;
        }
    }

    internal void SetRemaining(float remaining)
    {
        remaining = Mathf.Max(0f, remaining);
        _remainingTime!.text = $"{remaining:0.0}s";
        _progressFill!.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(remaining / _duration), 1f);
    }

    internal void ShowResult(bool completed)
    {
        _statusText!.text = completed ? "ITEM READY" : "ACCESS CANCELLED";
        _remainingTime!.text = completed ? "READY" : string.Empty;
        _cancelHint!.gameObject.SetActive(false);
        if (completed)
        {
            _progressFill!.rectTransform.anchorMax = new Vector2(0f, 1f);
        }
        PlayResultSound(completed);
        _exiting = true;
        _exitStartTime = Time.unscaledTime;
        _exitStartAlpha = _canvasGroup!.alpha;
        _exitStartScale = _timerRoot!.localScale;
    }

    internal void HideImmediately()
    {
        _visible = false;
        _exiting = false;
        _itemIcon = null;
        if (_icon)
        {
            _icon!.sprite = null;
            _icon.enabled = false;
        }
        if (_uiRoot)
        {
            _uiRoot!.SetActive(false);
        }
    }

    internal void Destroy()
    {
        HideImmediately();
        if (_uiRoot)
        {
            UnityEngine.Object.Destroy(_uiRoot);
            _uiRoot = null;
        }
        _bundle?.Unload(false);
        _bundle = null;
        _font = null;
    }

    private void UpdateExitAnimation()
    {
        var elapsed = Time.unscaledTime - _exitStartTime;
        if (elapsed <= ExitHoldDuration)
        {
            return;
        }

        var progress = Mathf.Clamp01((elapsed - ExitHoldDuration) / ExitFadeDuration);
        _canvasGroup!.alpha = Mathf.Lerp(_exitStartAlpha, 0f, progress);
        _timerRoot!.localScale = Vector3.Lerp(_exitStartScale, Vector3.one * 0.96f, progress);
        if (progress >= 1f)
        {
            HideImmediately();
        }
    }

    private void BindPrefab()
    {
        _canvasGroup = RequireComponent<CanvasGroup>(_uiRoot!.transform, string.Empty);
        _timerRoot = RequireComponent<RectTransform>(_uiRoot.transform, "TimerRoot");
        _icon = RequireComponent<Image>(_uiRoot.transform, "TimerRoot/IconFrame/Icon");
        _progressFill = RequireComponent<Image>(_uiRoot.transform, "TimerRoot/ProgressTrack/ProgressFill");
        _itemName = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/ItemName");
        _remainingTime = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/RemainingTime");
        _statusText = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/Eyebrow");
        _detailText = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/Detail");
        _cancelHint = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/CancelHint");
    }

    private void SetCancelHint()
    {
        var shortcut = Configuration.ClearItemAccessDelay.Value;
        var hasShortcut = shortcut.MainKey != KeyCode.None;
        _cancelHint!.text = hasShortcut
            ? $"PRESS {shortcut.ToString().ToUpperInvariant()} TO CANCEL"
            : string.Empty;
        _cancelHint.gameObject.SetActive(hasShortcut);
    }

    private void PlayResultSound(bool completed)
    {
        if (!Configuration.TimerSounds.Value || !Singleton<GUISounds>.Instantiated)
        {
            return;
        }

        try
        {
            Singleton<GUISounds>.Instance.PlayUISound(
                completed ? EUISoundType.ButtonBottomBarClick : EUISoundType.MenuEscape);
        }
        catch (Exception exception)
        {
            _ = exception;
#if DEBUG
            _logger?.LogWarning($"Item-use delay timer sound could not be played: {exception.Message}");
#endif
        }
    }

    private static string GetItemName(Item item)
    {
        var name = item.LocalizedName();
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, item.Template.NameLocalizationKey, StringComparison.Ordinal))
        {
            return name;
        }

        name = item.LocalizedShortName();
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, item.ShortName, StringComparison.Ordinal))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(item.ShortName) ? item.TemplateId.ToString() : item.ShortName;
    }

    private static ItemIcon? LoadItemIcon(Item item)
    {
        try
        {
            return ItemViewFactory.LoadItemIcon(item, 1, false);
        }
        catch
        {
            return null;
        }
    }

    private static string GetDelayDetail(Configuration.ItemAccessDelayInfo delayInfo)
    {
        if (delayInfo.NestingDelay <= 0f)
        {
            return $"BASE ACCESS DELAY  {delayInfo.BaseDelay:0.0}s";
        }

        var layerLabel = delayInfo.NestingDepth == 1 ? "LAYER" : "LAYERS";
        return $"BASE {delayInfo.BaseDelay:0.0}s  +  {delayInfo.NestingDelay:0.0}s NESTED  •  {delayInfo.NestingDepth} {layerLabel}";
    }

    private static string GetSlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Pockets => "POCKETS",
        EquipmentSlot.TacticalVest => "TACTICAL VEST",
        EquipmentSlot.ArmBand => "ARM BAND",
        EquipmentSlot.Backpack => "BACKPACK",
        EquipmentSlot.SecuredContainer => "SECURE CONTAINER",
        _ => slot.ToString().ToUpperInvariant(),
    };

    private static T RequireComponent<T>(Transform root, string path) where T : Component
    {
        var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
        if (!target || !target.TryGetComponent<T>(out var component))
        {
            throw new InvalidOperationException($"Missing {typeof(T).Name} at '{path}'.");
        }
        return component;
    }
}
