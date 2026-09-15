using UnityEngine;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>Replaces the held weapon's finger curl while the hand touches the bag.</summary>
internal sealed class BackpackFingerPose
{
    private readonly Transform _wrist;
    private readonly Transform?[,] _joints = new Transform?[5, 3];
    private readonly Vector3 _forward;
    private readonly Vector3 _palmNormal;
    private readonly Vector3 _indexSide;

    internal BackpackFingerPose(Transform wrist, Vector3 forward, Vector3 palmNormal, Vector3 indexSide)
    {
        _wrist = wrist;
        _forward = forward;
        _palmNormal = palmNormal;
        _indexSide = indexSide;
        foreach (var bone in wrist.GetComponentsInChildren<Transform>(true))
        {
            if (BackpackHandBoneNames.TryGetDigit(bone.name, out var finger, out var joint))
            {
                _joints[finger - 1, joint - 1] = bone;
            }
        }
    }

    internal void Apply(float weight, float curl)
    {
        if (weight <= 0f || !_wrist || _forward.sqrMagnitude < 0.001f)
        {
            return;
        }

        for (var finger = 0; finger < 5; finger++)
        {
            ApplyFinger(finger, weight, curl, 0f);
        }
    }

    internal void Apply(float weight, BackpackAccessMotion motion)
    {
        if (weight <= 0f || !_wrist || _forward.sqrMagnitude < 0.001f)
        {
            return;
        }

        for (var finger = 0; finger < 5; finger++)
        {
            ApplyFinger(finger, weight, motion.RightFingerCurl(finger), motion.Grasp);
        }
    }

    private void ApplyFinger(int finger, float weight, float curl, float grasp)
    {
        var first = _joints[finger, 0];
        var middle = _joints[finger, 1];
        var last = _joints[finger, 2];
        if (!first || !middle || !last)
        {
            return;
        }

        // Oppose the thumb across the palm as the grip closes. Curling an
        // outward-pointing thumb alone left it spread away from the item.
        var direction = finger == 0
            ? Vector3.Lerp(_forward * 0.5f + _indexSide * 0.8f,
                _forward * 0.85f - _indexSide * 0.55f, grasp).normalized
            : (_forward + _indexSide * ((2.5f - finger) * Mathf.Lerp(0.06f, 0.015f, grasp))).normalized;
        var curlAxis = Vector3.Cross(direction, _palmNormal).normalized;
        var knuckleAngle = Mathf.Lerp(Mathf.Lerp(5f, 35f, curl), finger == 0 ? 25f : 65f, grasp);
        var middleAngle = Mathf.Lerp(Mathf.Lerp(15f, 75f, curl), finger == 0 ? 55f : 130f, grasp);
        AimJoint(first!, middle!, direction, curlAxis, knuckleAngle, weight);
        AimJoint(middle!, last!, direction, curlAxis, middleAngle, weight);
        // Retain native fingertip roll; the two larger joints establish the
        // relaxed shape without hard-coded local rotations for each glove.
    }

    private void AimJoint(Transform joint, Transform child, Vector3 direction, Vector3 curlAxis,
        float angle, float weight)
    {
        var desired = _wrist.TransformDirection(Quaternion.AngleAxis(angle, curlAxis) * direction);
        var current = child.position - joint.position;
        if (current.sqrMagnitude < 0.000001f)
        {
            return;
        }

        var turn = Quaternion.FromToRotation(current, desired);
        joint.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * joint.rotation;
    }
}
