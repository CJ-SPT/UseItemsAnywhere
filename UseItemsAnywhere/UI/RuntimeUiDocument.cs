using System;
using UnityEngine;

namespace UseItemsAnywhere.UI;

internal sealed class RuntimeUiDocument
{
    private AssetBundle? _bundle;
    private readonly RuntimeUiFont _font;

    internal RuntimeUiDocument(AssetBundle bundle, GameObject root, RuntimeUiFont font)
    {
        _bundle = bundle;
        Root = root;
        _font = font;
    }

    internal GameObject Root { get; }

    internal bool IsAvailable => Root;

    internal void Prepare()
    {
        Root.SetActive(false);
        _font.TryAssign(Root);
    }

    internal void Show()
    {
        if (!Root)
        {
            return;
        }

        _font.TryAssign(Root);
        Root.SetActive(true);
    }

    internal void Hide()
    {
        if (Root)
        {
            Root.SetActive(false);
        }
    }

    internal T Require<T>(string path) where T : Component
    {
        var target = string.IsNullOrEmpty(path) ? Root.transform : Root.transform.Find(path);
        if (!target || !target.TryGetComponent<T>(out var component))
        {
            throw new InvalidOperationException($"Missing {typeof(T).Name} at '{path}'.");
        }
        return component;
    }

    internal static T Require<T>(Transform root, string path) where T : Component
    {
        var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
        if (!target || !target.TryGetComponent<T>(out var component))
        {
            throw new InvalidOperationException($"Missing {typeof(T).Name} at '{path}'.");
        }
        return component;
    }

    internal void Destroy()
    {
        if (Root)
        {
            UnityEngine.Object.Destroy(Root);
        }

        _bundle?.Unload(false);
        _bundle = null;
    }
}
