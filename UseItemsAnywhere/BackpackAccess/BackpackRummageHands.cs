using System.Reflection;
using EFT;
using HarmonyLib;
using RootMotion.FinalIK;
using UnityEngine;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>
///     Braces the bag, follows its top seam, searches just below the mouth,
///     then withdraws the right hand before releasing the supporting hand.
/// </summary>
internal sealed class BackpackRummageHands
{
    private static readonly FieldInfo? LimbIkField = AccessTools.Field(typeof(Player), "_limbs");
    private static readonly FieldInfo? TwistBonesField = AccessTools.Field(typeof(Player), "_twistBones");
    private static bool _loggedHandRig;
    private static BackpackRummageHands? _active;

    private readonly Player _player;
    private readonly Transform _pivot;
    private readonly LimbIK _leftArm;
    private readonly LimbIK _rightArm;
    private readonly TwistRelax[]? _twistBones;
    private readonly Vector3 _leftSupport;
    private readonly Vector3 _rightZipStart;
    private readonly Vector3 _rightZipEnd;
    private readonly Vector3 _rightInside;
    private readonly Vector3 _rightClear;
    private readonly Vector3 _rightRest;
    private readonly Vector3 _searchTravel;
    private readonly HandContact _leftContact;
    private readonly HandContact _rightContact;
    private readonly BackpackFingerPose _leftFingers;
    private readonly BackpackFingerPose _rightFingers;
    private Vector3 _rightPosition;
    private Vector3 _withdrawCameraPosition;
    private Vector3 _clearCameraPosition;
    private Vector3 _restCameraPosition;
    private Quaternion _withdrawCameraRotation;
    private Quaternion _lastRightCameraRotation;
    private Vector3 _lastRightElbowOffset;
    private Vector3 _withdrawElbowOffset;
    private bool _hasRightCameraRotation;
    private float _leftWeight;
    private float _rightWeight;
    private float _insertBlend;
    private BackpackAccessMotion _motion;
    private bool _withdrawing;
    private bool _stopped;

    private BackpackRummageHands(Player player, Transform pivot, LimbIK[] limbs, Bounds bounds)
    {
        _player = player;
        _pivot = pivot;
        _leftArm = limbs[0];
        _rightArm = limbs[1];
        _twistBones = TwistBonesField?.GetValue(player) as TwistRelax[];
        _leftContact = HandContact.FromWrist(_leftArm.solver.bone3.transform, -1f);
        _rightContact = HandContact.FromWrist(_rightArm.solver.bone3.transform, 1f);
        _leftFingers = new BackpackFingerPose(_leftArm.solver.bone3.transform,
            _leftContact.Forward, _leftContact.PalmNormal, _leftContact.IndexSide);
        _rightFingers = new BackpackFingerPose(_rightArm.solver.bone3.transform,
            _rightContact.Forward, _rightContact.PalmNormal, _rightContact.IndexSide);
        if (!_loggedHandRig)
        {
            _loggedHandRig = true;
            Plugin.LogSource?.LogInfo($"Backpack palm frames: left={_leftContact.IsValid}, right={_rightContact.IsValid}; "
                + $"forearm twist bones={_twistBones?.Length ?? 0}.");
        }

        // These are coordinates in the bag's frame. Unlike projected screen
        // rectangles, they stay attached when the bag tilts or the camera turns.
        _leftSupport = PointInBounds(bounds, 0.02f, 0.42f, 0.40f);
        _rightZipStart = PointInBounds(bounds, 0.88f, 0.86f, 0.40f);
        _rightZipEnd = PointInBounds(bounds, 0.66f, 0.86f, 0.40f);
        _rightInside = PointInBounds(bounds, 0.70f, 0.77f, 0.68f);
        // Clear close to the entry side. Reaching farther right and above
        // the bag created a display pose before the cross-body withdrawal.
        _rightClear = PointInBounds(bounds, 0.72f, 0.94f, 0.32f);
        _rightRest = PointInBounds(bounds, 1.12f, 0.18f, 0.12f);
        _searchTravel = new Vector3(
            Mathf.Min(bounds.size.x * 0.09f, 0.03f),
            Mathf.Min(bounds.size.y * 0.07f, 0.028f),
            Mathf.Min(bounds.size.z * 0.10f, 0.03f));
        _rightPosition = _rightZipStart;
    }

    internal static BackpackRummageHands? Begin(
        Player player, Transform pivot, Renderer[]? renderers)
    {
        if (!player || !pivot || renderers == null
            || LimbIkField?.GetValue(player) is not LimbIK[] { Length: > 1 } limbs
            || !HasArmBones(limbs[0]) || !HasArmBones(limbs[1])
            || !BackpackAccessAnimation.TryGetLocalRendererBounds(renderers, pivot, out var bounds))
        {
            return null;
        }

        _active?.Stop();
        var animation = new BackpackRummageHands(player, pivot, limbs, bounds);
        _active = animation;
        return animation;
    }

    internal void Update(BackpackAccessMotion motion)
    {
        if (_stopped || !_pivot)
        {
            return;
        }

        _leftWeight = motion.LeftWeight;
        _rightWeight = motion.RightWeight;
        _motion = motion;

        if (motion.Exit > 0f)
        {
            if (!_withdrawing)
            {
                _withdrawing = true;
                var cameraFrame = _pivot.parent;
                if (cameraFrame)
                {
                    _withdrawCameraPosition = cameraFrame.InverseTransformPoint(_pivot.TransformPoint(_rightPosition));
                    _clearCameraPosition = cameraFrame.InverseTransformPoint(_pivot.TransformPoint(_rightClear));
                    _restCameraPosition = cameraFrame.InverseTransformPoint(_pivot.TransformPoint(_rightRest));
                    _restCameraPosition.x = -0.24f;
                    _restCameraPosition.y = Mathf.Min(_restCameraPosition.y, -0.44f);
                    // Bag-local down can point away from the player on a
                    // tilted bag. Cross toward the left thigh in camera space,
                    // keeping the held model clear of the near camera plane.
                    _restCameraPosition.z = Mathf.Min(_clearCameraPosition.z,
                        Mathf.Max(0.18f, _clearCameraPosition.z - 0.20f));
                    _withdrawCameraRotation = _hasRightCameraRotation ? _lastRightCameraRotation
                        : Quaternion.Inverse(cameraFrame.rotation) * _rightArm.solver.bone3.transform.rotation;
                    _withdrawElbowOffset = _hasRightCameraRotation ? _lastRightElbowOffset
                        : cameraFrame.InverseTransformDirection(_rightArm.solver.bone2.transform.position
                            - _rightArm.solver.bone1.transform.position);
                }
            }

            // Keep the entire extraction in the camera frame. Both segments
            // share velocity at clearance: lift flows into lowering instead
            // of stopping for a display pose and dropping in a second beat.
            if (_pivot.parent)
            {
                var path = new BackpackWithdrawalPath(motion.Exit);
                var handPosition = _withdrawCameraPosition * path.StartWeight
                    + _clearCameraPosition * path.ClearWeight
                    + _restCameraPosition * path.RestWeight
                    + new Vector3(-0.35f, -0.045f, -0.18f) * path.TangentWeight;
                _rightPosition = _pivot.InverseTransformPoint(_pivot.parent.TransformPoint(handPosition));
            }
            return;
        }

        if (motion.Insert <= 0f)
        {
            var progress = motion.Seam;
            _rightPosition = Vector3.Lerp(_rightZipStart, _rightZipEnd, progress)
                + Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 0.012f);
            return;
        }

        _insertBlend = motion.Insert - motion.SearchLift * 0.15f;
        // Pass above the seam before dipping behind the front panel. Straight
        // interpolation made the glove drag down the outside of the closed bag.
        var entry = Vector3.Lerp(_rightZipEnd, _rightInside, motion.Insert);
        entry.y += Mathf.Sin(motion.Insert * Mathf.PI) * _searchTravel.y;
        _rightPosition = entry + Vector3.Scale(_searchTravel, new Vector3(
            motion.SearchSide,
            -motion.SearchPress + motion.SearchLift * 0.45f,
            motion.SearchPress));
    }

    internal void Stop()
    {
        _stopped = true;
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    internal static void ApplyActive(Player player)
    {
        var active = _active;
        if (active == null || active._stopped || !ReferenceEquals(active._player, player)
            || !active._pivot || !player.FirstPersonPointOfView
            || player.CustomAnimationsAreProcessing)
        {
            return;
        }

        var frame = active._pivot;
        ApplyArm(active._leftArm, frame, active._leftSupport, active._leftWeight,
            -1f, active._leftContact, new Vector3(0.45f, 0.45f, 0.8f), new Vector3(1f, 0.3f, 0f));
        // Roll the palm toward the chest as the grip clears the mouth, then
        // carry that orientation across the body toward the left thigh.
        // Start from the rendered grip so the turn has no entry snap.
        Quaternion? extractionRotation = null;
        Vector3? extractionElbowGoal = null;
        if (active._withdrawing && frame.parent)
        {
            var carryRotation = Quaternion.LookRotation(new Vector3(-0.80f, -0.35f, 0.30f),
                new Vector3(-0.15f, 0.10f, -1f)) * active._rightContact.FrameToWrist;
            extractionRotation = frame.parent.rotation * Quaternion.Slerp(
                active._withdrawCameraRotation, carryRotation, active._motion.PresentItem);
            // Start from the last solved elbow and tuck it toward the ribs
            // and behind the shoulder. This is a bend goal, not a direct
            // translation of the elbow bone; the solver preserves arm lengths.
            var upperArm = active._rightArm.solver;
            var upperLength = Vector3.Distance(upperArm.bone1.transform.position, upperArm.bone2.transform.position);
            var tuckedOffset = new Vector3(0.10f, -1f, -0.35f).normalized * upperLength;
            extractionElbowGoal = upperArm.bone1.transform.position + frame.parent.TransformDirection(
                Vector3.Lerp(active._withdrawElbowOffset, tuckedOffset, active._motion.ElbowTuck));
        }
        var searchDirection = Vector3.Lerp(new Vector3(-0.45f, 0f, 0.9f),
            new Vector3(-0.25f, -0.25f, 1f), active._insertBlend);
        ApplyArm(active._rightArm, frame, active._rightPosition, active._rightWeight,
            1f, active._rightContact,
            searchDirection, new Vector3(0f, -1f, 0.25f), extractionRotation, active._motion.PresentItem,
            extractionElbowGoal, active._motion.LowerRightHand);

        // The native twist pass ran before our new wrist orientation. Run it
        // again to distribute roll through the forearm instead of pinching the
        // sleeve at the wrist. Relax preserves the child hand's rotation.
        if (active._twistBones != null && (active._leftWeight > 0f || active._rightWeight > 0f))
        {
            foreach (var twist in active._twistBones)
            {
                if (twist && twist.parent && twist.child)
                {
                    twist.Relax();
                }
            }
        }

        active._leftFingers.Apply(active._leftWeight, 0.45f);
        active._rightFingers.Apply(active._rightWeight, active._motion);
        if (frame.parent)
        {
            active._lastRightCameraRotation = Quaternion.Inverse(frame.parent.rotation)
                * active._rightArm.solver.bone3.transform.rotation;
            active._lastRightElbowOffset = frame.parent.InverseTransformDirection(
                active._rightArm.solver.bone2.transform.position - active._rightArm.solver.bone1.transform.position);
            active._hasRightCameraRotation = true;
        }
    }

    internal Transform? CreateRetrievedItemAnchor()
    {
        if (!_rightContact.IsValid || !HasArmBones(_rightArm))
        {
            return null;
        }

        var anchor = new GameObject("UseItemsAnywhere_RetrievedItemGrip").transform;
        anchor.SetParent(_rightArm.solver.bone3.transform, false);
        anchor.localPosition = _rightContact.PalmOffset
            + _rightContact.Forward * 0.038f + _rightContact.PalmNormal * 0.019f;
        // The item's long axis runs across the curled fingers, held between
        // the thumb and palm. Parenting to the real wrist prevents visual lag.
        anchor.localRotation = Quaternion.LookRotation(
            _rightContact.IndexSide - _rightContact.Forward * 0.25f, -_rightContact.PalmNormal);
        return anchor;
    }

    private static void ApplyArm(LimbIK limb, Transform frame, Vector3 localPosition,
        float weight, float side, HandContact contact, Vector3 fingerDirection, Vector3 palmDirection,
        Quaternion? wristRotation = null, float forearmAlignment = 0f,
        Vector3? elbowGoal = null, float elbowRetraction = 0f)
    {
        if (weight <= 0f || !HasArmBones(limb))
        {
            return;
        }

        var solver = limb.solver;
        var shoulder = solver.bone1.transform;
        var elbow = solver.bone2.transform;
        var wrist = solver.bone3.transform;
        var nativeWristRotation = wrist.rotation;
        var contactRotation = wristRotation ?? (contact.IsValid
            ? Quaternion.LookRotation(frame.TransformDirection(fingerDirection), frame.TransformDirection(palmDirection))
                * contact.FrameToWrist
            : nativeWristRotation);
        var target = frame.TransformPoint(localPosition);
        // The visible contact belongs to the palm, not the wrist joint. With
        // the wrist at the seam the whole glove used to project above the bag.
        target -= contactRotation * Vector3.Scale(contact.PalmOffset, wrist.lossyScale);
        var upperLength = Vector3.Distance(shoulder.position, elbow.position);
        var lowerLength = Vector3.Distance(elbow.position, wrist.position);
        var reach = upperLength + lowerLength;
        if (reach < 0.01f)
        {
            return;
        }

        // Increase flexion after clearance, preventing an extended arm from
        // sweeping down as a rigid lever around the shoulder.
        target = shoulder.position + Vector3.ClampMagnitude(target - shoulder.position,
            BackpackArmGeometry.MaximumReach(upperLength, lowerLength, elbowRetraction));
        var blendedTarget = Vector3.Lerp(wrist.position, target, weight);
        var cameraFrame = frame.parent ? frame.parent : frame;
        // Keep sleeves outside the opening and avoid pulling elbows toward
        // the lens. The pole stays camera-relative as the bag tilts away.
        var elbowDirection = elbowGoal.HasValue ? elbowGoal.Value - shoulder.position
            : cameraFrame.TransformDirection(new Vector3(side * 0.85f, -1f, 0.35f));
        var bendNormal = Vector3.Cross(elbowDirection, blendedTarget - shoulder.position);
        if (bendNormal.sqrMagnitude < 0.000001f)
        {
            return;
        }

        var nativeBend = Vector3.Cross(elbow.position - shoulder.position, wrist.position - shoulder.position);
        if (nativeBend.sqrMagnitude > 0.000001f)
        {
            bendNormal = Vector3.Slerp(nativeBend.normalized, bendNormal.normalized, weight);
        }

        // Solve just these bones. Do not overwrite Tarkov's persistent solver
        // targets, weights, or bend settings, which the next native pass needs.
        IKSolverTrigonometric.Solve(shoulder, elbow, wrist, target, bendNormal.normalized, weight);
        if (contact.IsValid)
        {
            // Limit the correction relative to the wrist carried by the solved
            // forearm. An unrestricted target bent the hand back almost 90
            // degrees when the right arm reached over the bag.
            var carriedRotation = wrist.rotation;
            var limitedRotation = Quaternion.RotateTowards(carriedRotation, contactRotation, 55f);
            if (forearmAlignment > 0f)
            {
                // Bound actual wrist bend against the solved forearm, not
                // against the old weapon pose carried by the native rig.
                var forearmDirection = (wrist.position - elbow.position).normalized;
                var handDirection = Vector3.RotateTowards(forearmDirection,
                    contactRotation * contact.Forward, 28f * Mathf.Deg2Rad, 0f);
                var alignedRotation = Quaternion.LookRotation(handDirection,
                    contactRotation * contact.PalmNormal) * contact.FrameToWrist;
                limitedRotation = Quaternion.Slerp(limitedRotation, alignedRotation, forearmAlignment);
            }
            wrist.rotation = Quaternion.Slerp(carriedRotation, limitedRotation, weight);
        }
    }

    private static bool HasArmBones(LimbIK limb) => limb && limb.solver != null
        && limb.solver.bone1.transform && limb.solver.bone2.transform && limb.solver.bone3.transform;

    private readonly struct HandContact
    {
        internal readonly bool IsValid;
        internal readonly Quaternion FrameToWrist;
        internal readonly Vector3 PalmOffset;
        internal readonly Vector3 Forward;
        internal readonly Vector3 PalmNormal;
        internal readonly Vector3 IndexSide;

        private HandContact(Vector3 index, Vector3 little, float side)
        {
            var forward = ((index + little) * 0.5f).normalized;
            // Index-to-little runs in opposite directions on the two hands.
            var palmNormal = Vector3.Cross(forward, little - index) * -side;
            IsValid = forward.sqrMagnitude > 0.001f && palmNormal.sqrMagnitude > 0.000001f;
            FrameToWrist = IsValid ? Quaternion.Inverse(Quaternion.LookRotation(forward, palmNormal)) : Quaternion.identity;
            PalmOffset = IsValid ? (index + little) * 0.35f : Vector3.zero;
            Forward = IsValid ? forward : Vector3.zero;
            PalmNormal = IsValid ? palmNormal.normalized : Vector3.zero;
            IndexSide = IsValid ? (index - little).normalized : Vector3.zero;
        }

        internal static HandContact FromWrist(Transform wrist, float side)
        {
            var index = Vector3.zero;
            var little = Vector3.zero;
            var indexDepth = int.MaxValue;
            var littleDepth = int.MaxValue;
            foreach (var child in wrist.GetComponentsInChildren<Transform>(true))
            {
                // Pick the first finger joint in the hierarchy; a curled tip
                // can be physically closer to the wrist than its knuckle.
                var position = wrist.InverseTransformPoint(child.position);
                var distance = position.sqrMagnitude;
                if (distance < 0.000001f)
                {
                    continue;
                }

                var depth = 0;
                for (var parent = child.parent; parent && parent != wrist; parent = parent.parent)
                {
                    depth++;
                }

                if (BackpackHandBoneNames.IsIndex(child.name) && depth < indexDepth)
                {
                    index = position;
                    indexDepth = depth;
                }

                if (BackpackHandBoneNames.IsLittle(child.name) && depth < littleDepth)
                {
                    little = position;
                    littleDepth = depth;
                }
            }

            return indexDepth < int.MaxValue && littleDepth < int.MaxValue
                ? new HandContact(index, little, side)
                : default;
        }
    }

    private static Vector3 PointInBounds(Bounds bounds, float x, float y, float z) => new(
        Mathf.LerpUnclamped(bounds.min.x, bounds.max.x, x),
        Mathf.LerpUnclamped(bounds.min.y, bounds.max.y, y),
        Mathf.LerpUnclamped(bounds.min.z, bounds.max.z, z));

}
