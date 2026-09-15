using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.InventoryLogic;
using UnityEngine;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>
///     A visual-only copy of the requested item. Never creates a LootItem or
///     changes inventory ownership; the normal TryProceed call uses the real
///     item after this presentation has been returned to the pool.
/// </summary>
internal sealed class BackpackRetrievedItem : IDisposable
{
    private readonly List<Action> _restore = new();
    private GameObject? _model;
    private Transform? _anchor;
    private Renderer[] _renderers = Array.Empty<Renderer>();
    private bool _disposed;

    internal static BackpackRetrievedItem? TryCreate(Player player, Item item, BackpackRummageHands hands, int layer)
    {
        var presentation = new BackpackRetrievedItem();
        try
        {
            presentation._anchor = hands.CreateRetrievedItemAnchor();
            if (!presentation._anchor)
            {
                return null;
            }

            presentation._model = Singleton<ObjectsFactory>.Instance.CreateCleanLootPrefab(item, ECameraType.Default, player);
            if (!presentation._model)
            {
                presentation.Dispose();
                return null;
            }

            presentation.Prepare(layer);
            return presentation;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UseItemsAnywhere] Could not prepare retrieved item presentation: {exception.Message}");
            presentation.Dispose();
            return null;
        }
    }

    private void Prepare(int layer)
    {
        var model = _model!;
        var originalName = model.name;
        var originalPosition = model.transform.localPosition;
        var originalRotation = model.transform.localRotation;
        var originalScale = model.transform.localScale;
        var originalActive = model.activeSelf;
        _restore.Add(() =>
        {
            if (!model) return;
            model.name = originalName;
            model.transform.localPosition = originalPosition;
            model.transform.localRotation = originalRotation;
            model.transform.localScale = originalScale;
            model.SetActive(originalActive);
        });
        model.name = "UseItemsAnywhere_RetrievedItemModel";

        foreach (var child in model.GetComponentsInChildren<Transform>(true))
        {
            var originalLayer = child.gameObject.layer;
            _restore.Add(() => { if (child) child.gameObject.layer = originalLayer; });
            child.gameObject.layer = layer;
        }
        foreach (var collider in model.GetComponentsInChildren<Collider>(true))
        {
            var wasEnabled = collider.enabled;
            _restore.Add(() => { if (collider) collider.enabled = wasEnabled; });
            collider.enabled = false;
        }
        foreach (var body in model.GetComponentsInChildren<Rigidbody>(true))
        {
            var wasKinematic = body.isKinematic;
            var detectedCollisions = body.detectCollisions;
            _restore.Add(() =>
            {
                if (!body) return;
                body.isKinematic = wasKinematic;
                body.detectCollisions = detectedCollisions;
            });
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        model.transform.SetParent(_anchor, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.SetActive(true);
        _renderers = model.GetComponentsInChildren<Renderer>(true);
        var hasEnabledRenderer = false;
        foreach (var renderer in _renderers)
        {
            var wasEnabled = renderer.enabled;
            var wasSuppressed = renderer.forceRenderingOff;
            _restore.Add(() =>
            {
                if (!renderer) return;
                renderer.enabled = wasEnabled;
                renderer.forceRenderingOff = wasSuppressed;
            });
            hasEnabledRenderer |= wasEnabled && renderer.gameObject.activeInHierarchy;
            renderer.forceRenderingOff = false;
        }
        if (!hasEnabledRenderer)
        {
            foreach (var renderer in _renderers)
            {
                if (renderer.gameObject.activeInHierarchy) renderer.enabled = true;
            }
        }

        if (!BackpackAccessAnimation.TryGetLocalRendererBounds(_renderers, model.transform, out var bounds))
        {
            throw new InvalidOperationException("The item prefab has no visible mesh.");
        }

        var size = Vector3.Scale(bounds.size, new Vector3(
            Mathf.Abs(originalScale.x), Mathf.Abs(originalScale.y), Mathf.Abs(originalScale.z)));
        var longest = Mathf.Max(size.x, size.y, size.z);
        var longAxis = size.x >= size.y && size.x >= size.z ? Vector3.right
            : size.y >= size.z ? Vector3.up : Vector3.forward;
        // Keep small items at their authored size. Only oversized props are
        // reduced enough to fit the temporary one-handed presentation.
        var scale = longest > 0.25f ? 0.25f / longest : 1f;
        model.transform.localScale = originalScale * scale;
        model.transform.localRotation = Quaternion.FromToRotation(longAxis, Vector3.forward);
        model.transform.localPosition = -(model.transform.localRotation
            * Vector3.Scale(bounds.center, model.transform.localScale))
            + Vector3.forward * Mathf.Min(longest * scale * 0.18f, 0.025f);
        SetVisible(false);
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed) return;
        foreach (var renderer in _renderers)
        {
            if (renderer) renderer.forceRenderingOff = !visible;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var model = _model;
        var anchor = _anchor;
        _model = null;
        _anchor = null;
        try
        {
            // Detach before destroying the hand anchor; never destroy a pooled
            // item indirectly by leaving it parented to a presentation object.
            if (model) model!.transform.SetParent(null, false);
            for (var index = _restore.Count - 1; index >= 0; index--)
            {
                try { _restore[index](); }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[UseItemsAnywhere] Could not restore retrieved item state: {exception.Message}");
                }
            }
            if (model) AssetPoolObject.ReturnToPool(model, true);
        }
        finally
        {
            _restore.Clear();
            _renderers = Array.Empty<Renderer>();
            if (anchor) UnityEngine.Object.Destroy(anchor!.gameObject);
        }
    }
}
