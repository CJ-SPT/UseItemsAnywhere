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

namespace UseItemsAnywhere;

internal sealed class ItemUseDelayTimer
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
    private TMP_FontAsset? _runtimeFont;
    private bool _ownsRuntimeFont;
    private bool _fontAssigned;
    private bool _fontWarningLogged;
    private ManualLogSource? _logger;
    private float _duration;
    private Player? _player;
    private int _nextPresentationId;
    private int _activePresentationId;
    private bool _visible;
    private bool _exiting;
    private float _exitStartTime;
    private float _exitStartAlpha;
    private Vector3 _exitStartScale;

    internal void Initialize(string pluginDirectory, ManualLogSource logger, Transform persistentParent)
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
            _canvasGroup = RequireComponent<CanvasGroup>(_uiRoot.transform, string.Empty);
            _timerRoot = RequireComponent<RectTransform>(_uiRoot.transform, "TimerRoot");
            _icon = RequireComponent<Image>(_uiRoot.transform, "TimerRoot/IconFrame/Icon");
            _progressFill = RequireComponent<Image>(_uiRoot.transform, "TimerRoot/ProgressTrack/ProgressFill");
            _itemName = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/ItemName");
            _remainingTime = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/RemainingTime");
            _statusText = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/Eyebrow");
            _detailText = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/Detail");
            _cancelHint = RequireComponent<TMP_Text>(_uiRoot.transform, "TimerRoot/CancelHint");
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

        TryAssignRuntimeFont();
    }

    internal Presentation? Begin(
        Player player,
        Item item,
        Configuration.ItemAccessDelayInfo delayInfo)
    {
        if (!_uiRoot || !_canvasGroup || !_timerRoot || !IsCurrentLocalPlayer(player))
        {
            return null;
        }

        TryAssignRuntimeFont();
        HideImmediately();
        var presentationId = ++_nextPresentationId;
        _activePresentationId = presentationId;
        _player = player;
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
        _uiRoot!.SetActive(true);
        _visible = true;
        return new Presentation(this, presentationId);
    }

    internal void Update()
    {
        if (!_visible || !_uiRoot)
        {
            return;
        }

        if (!IsCurrentLocalPlayer(_player))
        {
            HideImmediately();
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

    private void SetRemaining(int presentationId, float remaining)
    {
        if (!_visible || presentationId != _activePresentationId)
        {
            return;
        }

        remaining = Mathf.Max(0f, remaining);
        _remainingTime!.text = $"{remaining:0.0}s";
        _progressFill!.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(remaining / _duration), 1f);
    }

    private void End(int presentationId, bool completed)
    {
        if (presentationId != _activePresentationId)
        {
            return;
        }

        _activePresentationId = 0;
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

    private void HideImmediately()
    {
        _visible = false;
        _exiting = false;
        _activePresentationId = 0;
        _player = null;
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

    internal void OnDestroy()
    {
        HideImmediately();
        if (_uiRoot)
        {
            UnityEngine.Object.Destroy(_uiRoot);
            _uiRoot = null;
        }
        _bundle?.Unload(false);
        _bundle = null;
        DestroyRuntimeFont();
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

    private void SetCancelHint()
    {
        var shortcut = Configuration.ClearItemAccessDelay.Value;
        var hasShortcut = shortcut.MainKey != KeyCode.None;
        _cancelHint!.text = hasShortcut
            ? $"PRESS {shortcut.ToString().ToUpperInvariant()} TO CANCEL"
            : string.Empty;
        _cancelHint.gameObject.SetActive(hasShortcut);
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
#if DEBUG
            _logger?.LogWarning($"Item-use delay timer sound could not be played: {exception.Message}");
#endif
        }
    }

    private static bool IsCurrentLocalPlayer(Player? player)
    {
        return player is not null
            && player
            && Singleton<IBotGame>.Instance is LocalGame localGame
            && localGame.PlayerOwner
            && ReferenceEquals(localGame.PlayerOwner.Player, player);
    }

    private bool TryAssignRuntimeFont()
    {
        if (!_uiRoot || (_fontAssigned && _runtimeFont))
        {
            return _fontAssigned;
        }

        if (!_runtimeFont)
        {
            try
            {
                var legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (!legacyFont)
                {
                    legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                if (legacyFont)
                {
                    _runtimeFont = TMP_FontAsset.CreateFontAsset(legacyFont);
                    if (_runtimeFont)
                    {
                        _runtimeFont.name = "UseItemsAnywhere_TimerRuntimeFont";
                        UnityEngine.Object.DontDestroyOnLoad(_runtimeFont);
                        _ownsRuntimeFont = true;
                    }
                }
            }
            catch (Exception exception)
            {
                LogFontWarning(exception);
            }

            if (!_runtimeFont)
            {
                TMP_FontAsset? fallbackFont = null;
                foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (!candidate || !candidate.material)
                    {
                        continue;
                    }
                    fallbackFont ??= candidate;
                    if (candidate.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _runtimeFont = candidate;
                        break;
                    }
                }
                _runtimeFont ??= fallbackFont;
            }
        }

        if (!_runtimeFont)
        {
            return false;
        }

        foreach (var text in _uiRoot!.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = _runtimeFont;
        }
        _fontAssigned = true;
        return true;
    }

    private void LogFontWarning(Exception exception)
    {
        if (_fontWarningLogged)
        {
            return;
        }
#if DEBUG
        _logger?.LogWarning($"Item-use delay timer could not create its preferred font and will use an EFT font when available: {exception.Message}");
#endif
        _fontWarningLogged = true;
    }

    private void DestroyRuntimeFont()
    {
        if (!_runtimeFont || !_ownsRuntimeFont)
        {
            _runtimeFont = null;
            _fontAssigned = false;
            return;
        }
        if (_runtimeFont!.material)
        {
            UnityEngine.Object.Destroy(_runtimeFont.material);
        }
        foreach (var atlas in _runtimeFont.atlasTextures ?? [])
        {
            if (atlas)
            {
                UnityEngine.Object.Destroy(atlas);
            }
        }
        UnityEngine.Object.Destroy(_runtimeFont);
        _runtimeFont = null;
        _ownsRuntimeFont = false;
        _fontAssigned = false;
    }

    private static T RequireComponent<T>(Transform root, string path) where T : Component
    {
        var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
        if (!target || !target.TryGetComponent<T>(out var component))
        {
            throw new InvalidOperationException($"Missing {typeof(T).Name} at '{path}'.");
        }
        return component;
    }

    internal sealed class Presentation : IDisposable
    {
        private ItemUseDelayTimer? _owner;
        private readonly int _presentationId;

        internal Presentation(ItemUseDelayTimer owner, int presentationId)
        {
            _owner = owner;
            _presentationId = presentationId;
        }

        internal void SetRemaining(float remaining) => _owner?.SetRemaining(_presentationId, remaining);

        internal void Finish(bool completed)
        {
            var owner = _owner;
            _owner = null;
            owner?.End(_presentationId, completed);
        }

        public void Dispose() => Finish(false);
    }
}
