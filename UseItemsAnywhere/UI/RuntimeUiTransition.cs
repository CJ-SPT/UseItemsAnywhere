using UnityEngine;

namespace UseItemsAnywhere.UI;

internal sealed class RuntimeUiTransition(CanvasGroup canvasGroup, RectTransform contentRoot)
{
    private float _exitStartTime;
    private float _exitStartAlpha;
    private float _exitHoldDuration;
    private float _exitDuration;
    private Vector3 _exitStartScale;
    private Vector3 _exitTargetScale;

    internal bool IsExiting { get; private set; }

    internal void BeginEntrance(float initialScale)
    {
        IsExiting = false;
        canvasGroup.alpha = 0f;
        contentRoot.localScale = Vector3.one * initialScale;
    }

    internal void UpdateEntrance(float alphaSpeed = 12f, float scaleSpeed = 18f)
    {
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1f, Time.unscaledDeltaTime * alphaSpeed);
        contentRoot.localScale = Vector3.Lerp(
            contentRoot.localScale,
            Vector3.one,
            Time.unscaledDeltaTime * scaleSpeed);
    }

    internal void BeginExit(float holdDuration, float duration, float targetScale)
    {
        IsExiting = true;
        _exitStartTime = Time.unscaledTime;
        _exitStartAlpha = canvasGroup.alpha;
        _exitStartScale = contentRoot.localScale;
        _exitHoldDuration = Mathf.Max(0f, holdDuration);
        _exitDuration = Mathf.Max(0.01f, duration);
        _exitTargetScale = Vector3.one * targetScale;
    }

    internal bool UpdateExit()
    {
        if (!IsExiting)
        {
            return false;
        }

        var elapsed = Time.unscaledTime - _exitStartTime;
        if (elapsed <= _exitHoldDuration)
        {
            return false;
        }

        var progress = Mathf.Clamp01((elapsed - _exitHoldDuration) / _exitDuration);
        canvasGroup.alpha = Mathf.Lerp(_exitStartAlpha, 0f, progress);
        contentRoot.localScale = Vector3.Lerp(_exitStartScale, _exitTargetScale, progress);
        if (progress < 1f)
        {
            return false;
        }

        IsExiting = false;
        return true;
    }

    internal void Reset()
    {
        IsExiting = false;
    }
}
