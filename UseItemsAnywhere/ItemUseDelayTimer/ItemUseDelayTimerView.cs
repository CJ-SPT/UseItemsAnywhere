using EFT;
using EFT.InventoryLogic;
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

    private RuntimeUiDocument? _document;
    private RuntimeUiTransition? _transition;
    private RuntimeUiService? _ui;
    private Image? _icon;
    private Image? _progressFill;
    private TMP_Text? _itemName;
    private TMP_Text? _remainingTime;
    private TMP_Text? _statusText;
    private TMP_Text? _detailText;
    private TMP_Text? _cancelHint;
    private ItemIcon? _itemIcon;
    private float _duration;
    private bool _visible;

    internal bool IsAvailable => _document?.IsAvailable == true;

    internal bool IsVisible => _visible;

    internal void Initialize(RuntimeUiService ui)
    {
        _ui = ui;
        _document = ui.CreateDocument(
            "Item-use delay timer",
            "itemusedelaytimer",
            PrefabPath,
            "UseItemsAnywhere_ItemUseDelayTimer",
            BindPrefab);
    }

    internal void Show(Item item, Configuration.ItemAccessDelayInfo delayInfo)
    {
        if (_document?.IsAvailable != true || _ui is null)
        {
            return;
        }

        _duration = Mathf.Max(delayInfo.TotalDelay, 0.01f);
        _itemName!.text = _ui.GetItemName(item);
        _remainingTime!.text = $"{delayInfo.TotalDelay:0.0}s";
        _statusText!.text = $"ACCESSING ITEM  •  {RuntimeUiService.GetSlotName(delayInfo.SourceSlot)}";
        _detailText!.text = GetDelayDetail(delayInfo);
        SetCancelHint();
        _progressFill!.rectTransform.anchorMax = Vector2.one;
        _itemIcon = _ui.GetItemIcon(item);
        _icon!.sprite = _itemIcon?.Sprite;
        _icon.enabled = _icon.sprite;
        _document.Show();
        _transition!.BeginEntrance(0.94f);
        _visible = true;
    }

    internal void Update()
    {
        if (!_visible || _document?.IsAvailable != true)
        {
            return;
        }

        if (_transition!.IsExiting)
        {
            if (_transition.UpdateExit())
            {
                HideImmediately();
            }
            return;
        }

        if (!Configuration.ShowTimerPanel.Value)
        {
            HideImmediately();
            return;
        }

        _transition.UpdateEntrance();

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
        _transition!.BeginExit(ExitHoldDuration, ExitFadeDuration, 0.96f);
    }

    internal void HideImmediately()
    {
        _visible = false;
        _transition?.Reset();
        _itemIcon = null;
        if (_icon)
        {
            _icon!.sprite = null;
            _icon.enabled = false;
        }
        _document?.Hide();
    }

    internal void Destroy()
    {
        HideImmediately();
        _document?.Destroy();
        _document = null;
        _transition = null;
        _ui = null;
    }

    private void BindPrefab(RuntimeUiDocument document)
    {
        var canvasGroup = document.Require<CanvasGroup>(string.Empty);
        var timerRoot = document.Require<RectTransform>("TimerRoot");
        _transition = new RuntimeUiTransition(canvasGroup, timerRoot);
        _icon = document.Require<Image>("TimerRoot/IconFrame/Icon");
        _progressFill = document.Require<Image>("TimerRoot/ProgressTrack/ProgressFill");
        _itemName = document.Require<TMP_Text>("TimerRoot/ItemName");
        _remainingTime = document.Require<TMP_Text>("TimerRoot/RemainingTime");
        _statusText = document.Require<TMP_Text>("TimerRoot/Eyebrow");
        _detailText = document.Require<TMP_Text>("TimerRoot/Detail");
        _cancelHint = document.Require<TMP_Text>("TimerRoot/CancelHint");
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
        _ui?.PlaySound(
            Configuration.TimerSounds.Value,
            completed ? EFT.UI.EUISoundType.ButtonBottomBarClick : EFT.UI.EUISoundType.MenuEscape,
            "Item-use delay timer");
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

}
