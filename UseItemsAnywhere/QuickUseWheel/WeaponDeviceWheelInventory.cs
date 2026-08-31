using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.QuickUseWheel;

internal sealed class WeaponDeviceWheelInventory
{
    private readonly List<WeaponDeviceWheelItem> _items = [];
    private readonly List<LightComponent> _lights = [];
    private RuntimeUiService _ui = null!;
    private Player.FirearmController? _controller;

    internal IReadOnlyList<WeaponDeviceWheelItem> Items => _items;

    internal bool HasAvailableItems => _items.Exists(static item => item.IsAvailable);

    internal void Initialize(RuntimeUiService ui)
    {
        _ui = ui;
    }

    internal void Populate(Player player)
    {
        _items.Clear();
        _lights.Clear();
        _controller = player.HandsController as Player.FirearmController;
        if (_controller is null)
        {
            return;
        }

        _ui.SetItemCachePlayer(player);
        foreach (var light in _controller.GetAllLightMods())
        {
            if (light?.Item != null)
            {
                _lights.Add(light);
            }
        }

        if (_lights.Count > 1)
        {
            AddAggregateItem();
        }

        for (var index = 0; index < _lights.Count; index++)
        {
            AddDeviceItem(_lights[index], index + 1);
        }
    }

    internal bool IsCurrentFirearm(Player player)
    {
        return _controller is not null
            && ReferenceEquals(player.HandsController, _controller);
    }

    internal bool Toggle(Player player, WeaponDeviceWheelItem item)
    {
        if (!TryResolveCurrentLights(player, item, out var lights) || lights.Count == 0)
        {
            return false;
        }

        var states = new LightsState[lights.Count];
        var activate = item.IsAggregate && lights.Exists(static light => !light.IsActive);
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            var state = light.GetLightState(!item.IsAggregate, false);
            if (item.IsAggregate)
            {
                state.IsActive = activate;
            }
            states[index] = state;
        }

        return _controller!.SetLightsState(states, false, false);
    }

    internal bool CycleMode(Player player, WeaponDeviceWheelItem item)
    {
        if (!item.CanCycleMode
            || !TryResolveCurrentLights(player, item, out var lights)
            || lights.Count == 0)
        {
            return false;
        }

        var states = new LightsState[lights.Count];
        for (var index = 0; index < lights.Count; index++)
        {
            states[index] = lights[index].GetLightState(false, true);
        }

        return _controller!.SetLightsState(states, false, true);
    }

    internal void Clear()
    {
        _items.Clear();
        _lights.Clear();
        _controller = null;
    }

    private void AddAggregateItem()
    {
        var activeCount = 0;
        foreach (var light in _lights)
        {
            if (light.IsActive)
            {
                activeCount++;
            }
        }

        var firearm = _controller!.Item;
        _items.Add(new WeaponDeviceWheelItem(
            null,
            "ALL DEVICES",
            "All Weapon Devices",
            $"{activeCount}/{_lights.Count} ON",
            _ui.GetItemDisplayName(firearm, 22).ToUpperInvariant(),
            CanChangeState(_lights),
            true,
            _lights.Exists(static light => light._template.ModesCount > 1),
            _ui.GetItemIcon(firearm)));
    }

    private void AddDeviceItem(LightComponent light, int ordinal)
    {
        var modeCount = light._template.ModesCount;
        var mode = modeCount > 1
            ? $" • MODE {light.SelectedMode + 1}/{modeCount}"
            : string.Empty;
        var item = light.Item;
        _items.Add(new WeaponDeviceWheelItem(
            light,
            _ui.GetItemDisplayName(item, 18),
            _ui.GetItemName(item),
            $"{(light.IsActive ? "ON" : "OFF")}{mode}",
            $"DEVICE {ordinal}",
            CanChangeState([light]),
            false,
            modeCount > 1,
            _ui.GetItemIcon(item)));
    }

    private bool CanChangeState(IReadOnlyList<LightComponent> lights)
    {
        if (_controller is null || lights.Count == 0)
        {
            return false;
        }

        var states = new LightsState[lights.Count];
        for (var index = 0; index < lights.Count; index++)
        {
            states[index] = lights[index].GetLightState(false, false);
        }
        return _controller.CurrentOperation is { } operation
            && operation.CanChangeLightState(states);
    }

    private bool TryResolveCurrentLights(
        Player player,
        WeaponDeviceWheelItem item,
        out List<LightComponent> lights)
    {
        lights = [];
        if (!IsCurrentFirearm(player) || _controller is null)
        {
            return false;
        }

        if (item.IsAggregate)
        {
            lights.AddRange(_controller.GetAllLightMods());
            return true;
        }

        var targetId = item.Light?.Item?.Id;
        if (string.IsNullOrEmpty(targetId))
        {
            return false;
        }

        foreach (var light in _controller.GetAllLightMods())
        {
            if (light?.Item?.Id == targetId)
            {
                lights.Add(light);
                return true;
            }
        }
        return false;
    }
}
