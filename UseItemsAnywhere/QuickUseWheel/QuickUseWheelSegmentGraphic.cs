using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UseItemsAnywhere.QuickUseWheel;

internal sealed class QuickUseWheelSegmentGraphic : MaskableGraphic
{
    private const float ArcStepDegrees = 6f;
    private const float FullCircleThreshold = 359.9f;

    private float _centerDegrees;
    private float _sweepDegrees = 360f;
    private float _innerRadius = 108f;
    private float _edgeFeather = 1.35f;

    internal void Configure(float centerDegrees, float sweepDegrees)
    {
        if (Mathf.Approximately(_centerDegrees, centerDegrees)
            && Mathf.Approximately(_sweepDegrees, sweepDegrees))
        {
            return;
        }

        _centerDegrees = centerDegrees;
        _sweepDegrees = Mathf.Clamp(sweepDegrees, 0f, 360f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (_sweepDegrees <= 0f)
        {
            return;
        }

        var rect = GetPixelAdjustedRect();
        var outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
        var innerRadius = Mathf.Clamp(_innerRadius, _edgeFeather, outerRadius - _edgeFeather * 2f);
        var closedCircle = _sweepDegrees >= FullCircleThreshold;
        var averageRadius = (innerRadius + outerRadius) * 0.5f;
        var angularFeather = closedCircle
            ? 0f
            : _edgeFeather / Mathf.Max(averageRadius, 1f) * Mathf.Rad2Deg;
        var angles = BuildAngles(closedCircle, angularFeather);
        var radii = new[]
        {
            Mathf.Max(0f, innerRadius - _edgeFeather),
            innerRadius,
            Mathf.Max(innerRadius, outerRadius - _edgeFeather),
            outerRadius,
        };
        var radialAlpha = new[] { 0f, 1f, 1f, 0f };
        var baseColor = color;
        var center = rect.center;

        for (var radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
        {
            for (var angleIndex = 0; angleIndex < angles.Count; angleIndex++)
            {
                var angle = angles[angleIndex];
                var radians = angle.Degrees * Mathf.Deg2Rad;
                var position = center + new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * radii[radiusIndex];
                var vertexColor = baseColor;
                vertexColor.a *= radialAlpha[radiusIndex] * angle.Alpha;
                vertexHelper.AddVert(position, vertexColor, Vector2.zero);
            }
        }

        var columns = angles.Count;
        for (var radiusIndex = 0; radiusIndex < radii.Length - 1; radiusIndex++)
        {
            for (var angleIndex = 0; angleIndex < columns - 1; angleIndex++)
            {
                var innerStart = radiusIndex * columns + angleIndex;
                var innerEnd = innerStart + 1;
                var outerStart = innerStart + columns;
                var outerEnd = outerStart + 1;
                vertexHelper.AddTriangle(innerStart, innerEnd, outerStart);
                vertexHelper.AddTriangle(innerEnd, outerEnd, outerStart);
            }
        }
    }

    private List<AngleSample> BuildAngles(bool closedCircle, float angularFeather)
    {
        var samples = new List<AngleSample>();
        var start = _centerDegrees - _sweepDegrees * 0.5f;
        var divisions = Mathf.Max(1, Mathf.CeilToInt(_sweepDegrees / ArcStepDegrees));
        if (!closedCircle)
        {
            samples.Add(new AngleSample(start - angularFeather, 0f));
        }

        for (var index = 0; index <= divisions; index++)
        {
            samples.Add(new AngleSample(start + _sweepDegrees * index / divisions, 1f));
        }

        if (!closedCircle)
        {
            samples.Add(new AngleSample(start + _sweepDegrees + angularFeather, 0f));
        }

        return samples;
    }

    private readonly struct AngleSample(float degrees, float alpha)
    {
        internal float Degrees { get; } = degrees;
        internal float Alpha { get; } = alpha;
    }
}
