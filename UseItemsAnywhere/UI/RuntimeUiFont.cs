using System;
using BepInEx.Logging;
using TMPro;
using UnityEngine;

namespace UseItemsAnywhere.UI;

internal sealed class RuntimeUiFont
{
#if DEBUG
    private readonly ManualLogSource _logger;
#endif
    private TMP_FontAsset? _runtimeFont;
    private bool _ownsRuntimeFont;
    private bool _warningLogged;

    internal RuntimeUiFont(ManualLogSource logger)
    {
#if DEBUG
        _logger = logger;
#endif
    }

    internal bool TryAssign(GameObject root)
    {
        if (!_runtimeFont)
        {
            TryCreateRuntimeFont();
            FindFallbackFont();
        }

        if (!_runtimeFont)
        {
#if DEBUG
            if (!_warningLogged)
            {
                _logger.LogWarning("A runtime font is not available yet; mod UI text will retry when shown.");
                _warningLogged = true;
            }
#endif
            return false;
        }

        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = _runtimeFont;
        }
        return true;
    }

    internal void Destroy()
    {
        var font = _runtimeFont;
        if (!font || !_ownsRuntimeFont)
        {
            _runtimeFont = null;
            return;
        }
        if (font!.material)
        {
            UnityEngine.Object.Destroy(font.material);
        }
        foreach (var atlas in font.atlasTextures ?? [])
        {
            if (atlas)
            {
                UnityEngine.Object.Destroy(atlas);
            }
        }
        UnityEngine.Object.Destroy(font);
        _runtimeFont = null;
        _ownsRuntimeFont = false;
    }

    private void TryCreateRuntimeFont()
    {
        Font? legacyFont = null;
        try
        {
            legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (!legacyFont)
            {
                legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }
        catch (Exception exception)
        {
            LogWarning(exception);
        }

        if (!legacyFont)
        {
            return;
        }

        try
        {
            var createdFont = TMP_FontAsset.CreateFontAsset(legacyFont);
            if (createdFont)
            {
                _runtimeFont = createdFont;
                _runtimeFont.name = "UseItemsAnywhere_RuntimeFont";
                UnityEngine.Object.DontDestroyOnLoad(_runtimeFont);
                _ownsRuntimeFont = true;
            }
        }
        catch (Exception exception)
        {
            LogWarning(exception);
        }
    }

    private void FindFallbackFont()
    {
        if (_runtimeFont)
        {
            return;
        }

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

    private void LogWarning(Exception exception)
    {
        if (_warningLogged)
        {
            return;
        }
#if DEBUG
        _logger.LogWarning($"Mod UI could not create its preferred font and will use an EFT font when available: {exception.Message}");
#endif
        _warningLogged = true;
    }
}
