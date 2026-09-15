using System;
using EFT;
using UnityEngine;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>
///     Hides the original held item without disabling its controller, animator,
///     or the player's separate hand meshes. Lives on the pooled item so that
///     disabling/returning that item always restores its renderer state.
/// </summary>
internal sealed class BackpackHeldItemVisibility : MonoBehaviour
{
    private Player? _player;
    private object? _controller;
    private Renderer[] _renderers = Array.Empty<Renderer>();
    private bool[] _originalForceRenderingOff = Array.Empty<bool>();
    private float _handoffDeadline = float.PositiveInfinity;
    private bool _restored;

    internal static BackpackHeldItemVisibility? Hide(Player player)
    {
        var controller = player.HandsController;
        var root = controller?.ControllerGameObject;
        if (!root || !root!.activeInHierarchy)
        {
            return null;
        }

        // A second request can start before a previous component's deferred
        // destruction. Restore its snapshot before capturing a fresh one.
        foreach (var previous in root.GetComponents<BackpackHeldItemVisibility>())
        {
            previous.Restore();
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return null;
        }

        var states = new bool[renderers.Length];
        for (var index = 0; index < renderers.Length; index++)
        {
            states[index] = renderers[index].forceRenderingOff;
        }

        var visibility = root.AddComponent<BackpackHeldItemVisibility>();
        visibility._player = player;
        visibility._controller = controller;
        visibility._renderers = renderers;
        visibility._originalForceRenderingOff = states;
        try
        {
            visibility.HideRenderers();
            return visibility;
        }
        catch
        {
            visibility.Restore();
            throw;
        }
    }

    internal void BeginHandoff()
    {
        // The old weapon must stay hidden while TryProceed lowers/replaces its
        // controller. Bound the wait in case the native callback never arrives.
        _handoffDeadline = Time.unscaledTime + 2f;
    }

    internal void Restore()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        for (var index = 0; index < _renderers.Length; index++)
        {
            var renderer = _renderers[index];
            if (renderer)
            {
                renderer.forceRenderingOff = _originalForceRenderingOff[index];
            }
        }

        _renderers = Array.Empty<Renderer>();
        _originalForceRenderingOff = Array.Empty<bool>();
        _player = null;
        _controller = null;
        Destroy(this);
    }

    private void LateUpdate()
    {
        if (_restored)
        {
            return;
        }

        if (!_player || !ReferenceEquals(_player!.HandsController, _controller)
            || _player.HealthController?.IsAlive == false || Time.unscaledTime >= _handoffDeadline)
        {
            Restore();
            return;
        }

        HideRenderers();
    }

    private void HideRenderers()
    {
        foreach (var renderer in _renderers)
        {
            if (renderer)
            {
                // Leave enabled untouched: native LOD/attachment animation may
                // change it while hidden. Preserve any pre-existing suppression.
                renderer.forceRenderingOff = true;
            }
        }
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();
}
