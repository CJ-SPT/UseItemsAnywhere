using System;
using System.IO;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UseItemsAnywhere;

internal sealed class ItemUseDelayTimer
{
    private const string PrefabPath = "assets/mods/useitemsanywhere.assets/ui/itemusedelaytimer.prefab";

    private AssetBundle? _bundle;
    private GameObject? _uiRoot;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _timerRoot;
    private Image? _icon;
    private Image? _progressFill;
    private TMP_Text? _itemName;
    private TMP_Text? _remainingTime;
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

    internal Presentation? Begin(Player player, Item item, float duration)
    {
        if (!_uiRoot || !_canvasGroup || !_timerRoot || !IsCurrentLocalPlayer(player))
        {
            return null;
        }

        TryAssignRuntimeFont();
        var presentationId = ++_nextPresentationId;
        _activePresentationId = presentationId;
        _player = player;
        _duration = Mathf.Max(duration, 0.01f);
        _itemName!.text = GetItemName(item);
        _remainingTime!.text = $"{duration:0.0}s";
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

        if (!Configuration.ShowTimerPanel.Value || !IsCurrentLocalPlayer(_player))
        {
            HideActive();
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

    private void End(int presentationId)
    {
        if (presentationId == _activePresentationId)
        {
            HideActive();
        }
    }

    private void HideActive()
    {
        _visible = false;
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
        HideActive();
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

        public void Dispose()
        {
            _owner?.End(_presentationId);
            _owner = null;
        }
    }
}
