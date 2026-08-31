using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using UnityEngine;

namespace UseItemsAnywhere.UI;

internal sealed class RuntimeUiService
{
    private readonly string _pluginDirectory;
    private readonly ManualLogSource _logger;
    private readonly Transform _persistentParent;
    private readonly RuntimeUiFont _font;
    private readonly Dictionary<Item, ItemIcon?> _itemIcons = [];
    private Player? _itemCachePlayer;

    internal ManualLogSource Logger => _logger;

    internal RuntimeUiService(
        string pluginDirectory,
        ManualLogSource logger,
        Transform persistentParent)
    {
        _pluginDirectory = pluginDirectory;
        _logger = logger;
        _persistentParent = persistentParent;
        _font = new RuntimeUiFont(logger);
    }

    internal RuntimeUiDocument? CreateDocument(
        string displayName,
        string bundleFileName,
        string prefabPath,
        string instanceName,
        Action<RuntimeUiDocument> bindPrefab)
    {
        var bundlePath = Path.Combine(_pluginDirectory, bundleFileName);
        if (!File.Exists(bundlePath))
        {
            _logger.LogError($"{displayName} bundle was not found: {bundlePath}");
            return null;
        }

        AssetBundle? bundle = null;
        GameObject? root = null;
        try
        {
            bundle = AssetBundle.LoadFromFile(bundlePath);
            if (!bundle)
            {
                _logger.LogError($"{displayName} bundle could not be loaded: {bundlePath}");
                return null;
            }

            var prefab = bundle.LoadAsset<GameObject>(prefabPath);
            if (!prefab)
            {
                _logger.LogError($"{displayName} prefab was not found in {bundlePath}");
                bundle.Unload(false);
                return null;
            }

            root = UnityEngine.Object.Instantiate(prefab, _persistentParent, false);
            root.name = instanceName;
            var document = new RuntimeUiDocument(bundle, root, _font);
            bindPrefab(document);
            document.Prepare();
            return document;
        }
        catch (Exception exception)
        {
            _logger.LogError($"{displayName} initialization failed:\n{exception}");
            if (root)
            {
                UnityEngine.Object.Destroy(root);
            }
            bundle?.Unload(false);
            return null;
        }
    }

    internal void SetItemCachePlayer(Player player)
    {
        if (ReferenceEquals(_itemCachePlayer, player))
        {
            return;
        }

        _itemIcons.Clear();
        _itemCachePlayer = player;
    }

    internal ItemIcon? GetItemIcon(Item item)
    {
        if (_itemIcons.TryGetValue(item, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var icon = ItemViewFactory.LoadItemIcon(item, 1, false);
            if (icon != null)
            {
                _itemIcons[item] = icon;
            }
            return icon;
        }
        catch
        {
            return null;
        }
    }

    internal string GetItemName(Item item)
    {
        var name = item.LocalizedName();
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, item.Template.NameLocalizationKey, StringComparison.Ordinal))
        {
            return name;
        }

        name = item.LocalizedShortName();
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, item.ShortName, StringComparison.Ordinal)
            ? item.TemplateId.ToString()
            : name;
    }

    internal string GetItemDisplayName(Item item, int maximumLength)
    {
        maximumLength = Mathf.Max(2, maximumLength);
        var name = item.LocalizedShortName();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, item.ShortName, StringComparison.Ordinal))
        {
            name = GetItemName(item);
        }

        return name.Length > maximumLength
            ? $"{name[..(maximumLength - 2)]}…"
            : name;
    }

    internal static string GetSlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Pockets => "POCKETS",
        EquipmentSlot.TacticalVest => "TACTICAL VEST",
        EquipmentSlot.ArmBand => "ARM BAND",
        EquipmentSlot.Backpack => "BACKPACK",
        EquipmentSlot.SecuredContainer => "SECURE CONTAINER",
        _ => slot.ToString(),
    };

    internal void PlaySound(bool enabled, EUISoundType soundType, string source)
    {
        if (!enabled || !Singleton<GUISounds>.Instantiated)
        {
            return;
        }

        try
        {
            Singleton<GUISounds>.Instance.PlayUISound(soundType);
        }
        catch (Exception exception)
        {
            _ = exception;
#if DEBUG
            _logger.LogWarning($"{source} sound could not be played: {exception.Message}");
#endif
        }
    }

    internal void Destroy()
    {
        _itemIcons.Clear();
        _itemCachePlayer = null;
        _font.Destroy();
    }
}
