using System;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.InventoryLogic;
using EFT.UI;
using UnityEngine;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>
///     Presents backpack access as a short physical interaction. Tarkov's native
///     body and hands animation parameters provide the movement while a pooled
///     copy of the equipped backpack is brought into the first-person view.
/// </summary>
internal sealed class BackpackAccessAnimation
{
    private const float CrouchedPoseLevel = 0f;
    private const float PoseTolerance = 0.01f;
    private const float PresentationDistance = 0.56f;
    private const float FallbackModelSize = 0.46f;

    private static readonly Vector3 FallbackHiddenPosition = new(-0.18f, -0.58f, PresentationDistance + 0.04f);
    private static readonly Vector3 FallbackAccessPosition = new(-0.04f, -0.20f, PresentationDistance);
    private static readonly Quaternion HiddenRotation = Quaternion.Euler(12f, -24f, -22f);
    private static readonly Quaternion AccessRotation = Quaternion.Euler(-24f, -16f, -12f);
    private static readonly Quaternion ModelRotation = Quaternion.Euler(-76f, 0f, 0f);
    private static bool _loggedPresentationDetails;

    private readonly Player _player;
    private readonly Item _backpack;
    private readonly Item _accessedItem;
    private readonly float _originalPoseLevel;
    private readonly float _startTime;
    private readonly float _timingScale;
    private Vector3 _hiddenPosition = FallbackHiddenPosition;
    private Vector3 _accessPosition = FallbackAccessPosition;
    private float _targetModelSize = FallbackModelSize;
    private PlayerAnimator? _playerAnimator;
    private FirearmsAnimator? _handsAnimator;
    private GameObject? _backpackPivot;
    private GameObject? _backpackModel;
    private Renderer[]? _equippedBackpackRenderers;
    private bool[]? _equippedBackpackRendererStates;
    private Transform[]? _modelTransforms;
    private int[]? _modelLayers;
    private Collider[]? _modelColliders;
    private bool[]? _modelColliderStates;
    private Rigidbody[]? _modelRigidbodies;
    private bool[]? _modelRigidbodyKinematicStates;
    private bool[]? _modelRigidbodyCollisionStates;
    private Renderer[]? _modelRenderers;
    private bool[]? _modelRendererStates;
    private bool[]? _modelForceRenderingOffStates;
    private string? _originalModelName;
    private Vector3 _originalModelScale;
    private bool _hasOriginalModelScale;
    private BetterSource? _searchSource;
    private bool _searchSoundAttempted;
    private bool _changedPose;
    private bool _nativeAnimationStarted;
    private BackpackRummageHands? _rummageHands;
    private BackpackHeldItemVisibility? _heldItemVisibility;
    private BackpackRetrievedItem? _retrievedItem;
    private bool _rummagingStopped;
    private bool _finished;

    private BackpackAccessAnimation(Player player, Item backpack, Item accessedItem, float totalDelay)
    {
        _player = player;
        _backpack = backpack;
        _accessedItem = accessedItem;
        _originalPoseLevel = player.PoseLevel;
        _startTime = Time.time;
        // Fit the complete gesture into short access delays without skipping
        // straight from a partly raised bag to the put-away pose.
        _timingScale = BackpackAccessMotion.TimeScale(totalDelay);
    }

    internal static BackpackAccessAnimation? Begin(
        Player player,
        Item accessedItem,
        Configuration.ItemAccessDelayInfo delayInfo)
    {
        if (!Configuration.AnimateBackpackAccess.Value
            || delayInfo.SourceSlot != EquipmentSlot.Backpack
            || delayInfo.TotalDelay <= 0f
            || !player
            || player.IsInPronePose
            || player.IsInventoryOpened)
        {
            return null;
        }

        var backpack = player.InventoryController.Inventory.Equipment
            .GetSlot(EquipmentSlot.Backpack)
            .ContainedItem;
        if (backpack == null)
        {
            return null;
        }

        var animation = new BackpackAccessAnimation(player, backpack, accessedItem, delayInfo.TotalDelay);
        try
        {
            animation.Start();
            return animation;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UseItemsAnywhere] Could not start backpack access animation: {exception.Message}");
            animation.Finish();
            return null;
        }
    }

    internal void Update(float remaining)
    {
        if (_finished || !_player)
        {
            return;
        }

        var pivot = _backpackPivot;
        if (!pivot)
        {
            return;
        }

        Vector3 position;
        Quaternion rotation;
        var elapsed = Time.time - _startTime;
        var motion = new BackpackAccessMotion(elapsed, remaining, _timingScale);
        if (motion.Exit > 0f)
        {
            if (_searchSource)
            {
                TryCleanup(StopSearchSound, "stop backpack search sound");
            }

            // Let the searching hand clear the mouth before lowering the bag.
            position = Vector3.Lerp(_accessPosition, _hiddenPosition, motion.Lower);
            // The supporting arm tucks the bag toward the left hip while
            // the retrieved item leaves on the right, rather than both props
            // descending together on parallel vertical tracks.
            position.x -= Mathf.Sin(motion.Lower * Mathf.PI) * 0.045f;
            rotation = Quaternion.Slerp(AccessRotation, HiddenRotation, motion.Lower);
        }
        else
        {
            var progress = motion.Raise;
            position = Vector3.Lerp(_hiddenPosition, _accessPosition, progress);
            position.x -= Mathf.Sin(progress * Mathf.PI) * 0.035f;
            rotation = Quaternion.Slerp(HiddenRotation, AccessRotation, progress);

            // The supporting hand and bag respond to the searching hand's
            // pressure. Independent noise made the interaction look detached.
            position += new Vector3(
                motion.SearchSide * 0.003f,
                -motion.SearchPress * 0.004f + motion.SearchLift * 0.002f,
                motion.SearchPress * 0.002f);
            rotation *= Quaternion.Euler(
                motion.SearchPress * 1.2f,
                motion.SearchSide * 0.8f,
                motion.SearchSide * 1.4f);

            if (!_searchSoundAttempted && motion.Seam > 0f)
            {
                _searchSoundAttempted = true;
                TryStartSearchSound();
            }
        }

        pivot!.transform.localPosition = position;
        pivot.transform.localRotation = rotation;
        _rummageHands?.Update(motion);
        _retrievedItem?.SetVisible(motion.ShowRetrievedItem);
    }

    internal void Finish(bool handoffHeldItem = false)
    {
        if (handoffHeldItem && _heldItemVisibility)
        {
            _heldItemVisibility!.BeginHandoff();
        }
        else
        {
            RestoreHeldItem();
        }

        if (_finished)
        {
            return;
        }

        _finished = true;
        TryCleanup(ReleaseRetrievedItem, "release retrieved item presentation");
        TryCleanup(StopRummaging, "stop backpack animation");
        TryCleanup(RestoreEquippedBackpackRenderers, "restore equipped backpack");
        TryCleanup(ReleaseBackpackModel, "release backpack model");

        if (!_player)
        {
            return;
        }

        // Respect a stance change made by the player during the delay. We only
        // restore the captured pose if the player is still at the crouch level
        // that this animation requested.
        if (_changedPose
            && !_player.IsInPronePose
            && Mathf.Abs(_player.PoseLevel - CrouchedPoseLevel) <= PoseTolerance)
        {
            _player.ChangePose(_originalPoseLevel - _player.PoseLevel);
        }
    }

    private void ReleaseRetrievedItem()
    {
        var retrievedItem = _retrievedItem;
        _retrievedItem = null;
        retrievedItem?.Dispose();
    }

    internal void RestoreHeldItem()
    {
        if (_heldItemVisibility)
        {
            _heldItemVisibility!.Restore();
        }

        _heldItemVisibility = null;
    }

    private void Start()
    {
        if (_originalPoseLevel > CrouchedPoseLevel + PoseTolerance)
        {
            _player.ChangePose(CrouchedPoseLevel - _originalPoseLevel);
            _changedPose = Mathf.Abs(_player.PoseLevel - _originalPoseLevel) > PoseTolerance;
        }

        StartNativeAnimation();
        TryCreateBackpackModel();
        if (_backpackPivot)
        {
            _heldItemVisibility = BackpackHeldItemVisibility.Hide(_player);
            if (_rummageHands != null)
            {
                _retrievedItem = BackpackRetrievedItem.TryCreate(
                    _player, _accessedItem, _rummageHands, _backpackModel!.layer);
            }
        }
    }

    private void StartNativeAnimation()
    {
        _playerAnimator = _player.MovementContext?.PlayerAnimator;
        _handsAnimator = _player.HandsController?.FirearmsAnimator;
        _nativeAnimationStarted = _playerAnimator != null || _handsAnimator != null;

        _playerAnimator?.EnableLoot(true);
        if (_handsAnimator != null)
        {
            // Lower the held item and retain Tarkov's neutral inventory hand
            // pose. The backpack-specific motion is applied after VisualPass.
            _handsAnimator.SetInventory(true);
        }
    }

    private void StopRummaging()
    {
        if (_rummagingStopped)
        {
            return;
        }

        _rummagingStopped = true;
        TryCleanup(StopSearchSound, "stop backpack search sound");
        TryCleanup(StopRummageHands, "stop staged backpack hand animation");

        if (!_nativeAnimationStarted)
        {
            return;
        }

        TryCleanup(
            () => _playerAnimator?.EnableLoot(false),
            "clear backpack body animation");
        if (_handsAnimator != null)
        {
            TryCleanup(
                () => _handsAnimator.SetInventory(false),
                "clear backpack hands animation");
        }

        _nativeAnimationStarted = false;
    }

    private void StopRummageHands()
    {
        _rummageHands?.Stop();
        _rummageHands = null;
    }

    private void TryCreateBackpackModel()
    {
        try
        {
            var cameraContainer = _player.CameraContainer;
            var presentationCamera = Camera.main;
            var cameraAnchor = presentationCamera
                ? presentationCamera!.transform
                : _player.CameraPosition;
            if (!cameraAnchor && cameraContainer)
            {
                cameraAnchor = cameraContainer.transform;
            }

            if (!cameraAnchor)
            {
                return;
            }

            var model = Singleton<ObjectsFactory>.Instance.CreateCleanLootPrefab(
                _backpack,
                ECameraType.Default,
                _player);
            if (!model)
            {
                return;
            }

            _backpackModel = model;
            _originalModelName = _backpackModel.name;
            _originalModelScale = _backpackModel.transform.localScale;
            _hasOriginalModelScale = true;
            _backpackModel.name = "UseItemsAnywhere_BackpackAccessModel";
            DisablePhysics(_backpackModel);
            var presentationLayer = GetFirstPersonLayer();
            SetLayerRecursively(_backpackModel, presentationLayer);
            ConfigurePresentationFraming(presentationCamera);

            _backpackPivot = new GameObject("UseItemsAnywhere_BackpackAccessPivot");
            _backpackPivot.transform.SetParent(cameraAnchor, false);
            _backpackPivot.transform.localPosition = _hiddenPosition;
            _backpackPivot.transform.localRotation = HiddenRotation;

            _backpackModel.transform.SetParent(_backpackPivot.transform, false);
            _backpackModel.transform.localPosition = Vector3.zero;
            _backpackModel.transform.localRotation = ModelRotation;
            _backpackModel.SetActive(true);
            PrepareModelRenderers(_backpackModel);
            // Measure in the presentation frame, not a world-aligned box whose
            // size changes when the player looks in a different direction.
            _backpackPivot.transform.localRotation = AccessRotation;
            CenterAndNormalizeModel(
                _backpackModel,
                _backpackPivot.transform,
                _targetModelSize);
            CorrectPresentationProjection(
                presentationCamera,
                _backpackPivot.transform,
                _modelRenderers);
            HideEquippedBackpackRenderers();
            _rummageHands = BackpackRummageHands.Begin(
                _player,
                _backpackPivot.transform,
                _modelRenderers);

            if (!_loggedPresentationDetails)
            {
                _loggedPresentationDetails = true;
                var layerName = LayerMask.LayerToName(presentationLayer);
                Plugin.LogSource?.LogInfo(
                    $"Backpack presentation created at '{cameraAnchor.name}' on layer "
                    + $"{presentationLayer} ('{layerName}') with {_modelRenderers?.Length ?? 0} renderers; "
                    + $"screen position {Configuration.BackpackHorizontalPosition.Value:0.00}, "
                    + $"{Configuration.BackpackVerticalPosition.Value:0.00}; "
                    + $"staged rummage animation active: {_rummageHands != null}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UseItemsAnywhere] Could not present backpack model: {exception.Message}");
            TryCleanup(StopRummageHands, "stop staged backpack hand animation");
            RestoreEquippedBackpackRenderers();
            ReleaseBackpackModel();
        }
    }

    private int GetFirstPersonLayer()
    {
        var controllerObject = _player.HandsController?.ControllerGameObject;
        if (controllerObject != null)
        {
            var renderers = controllerObject.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer
                    && renderer.enabled
                    && renderer.gameObject.activeInHierarchy)
                {
                    return renderer.gameObject.layer;
                }
            }

            foreach (var renderer in renderers)
            {
                if (renderer)
                {
                    return renderer.gameObject.layer;
                }
            }
        }

        // Default is rendered by Tarkov's main first-person camera and is a
        // safer fallback than the player root, which is normally self-culled.
        return 0;
    }

    private void ConfigurePresentationFraming(Camera? camera)
    {
        if (camera == null || camera.fieldOfView <= 1f || camera.aspect <= 0.1f)
        {
            return;
        }

        var halfViewHeight = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f)
            * PresentationDistance;
        var halfViewWidth = halfViewHeight * camera.aspect;
        var horizontalPosition = Configuration.BackpackHorizontalPosition.Value;
        var verticalPosition = Configuration.BackpackVerticalPosition.Value;
        // Hands retain their real scale. Scaling the entire bag down to fit
        // the viewport made it look miniature beside the gloves at lower FOV.
        // Keep a consistent physical proportion and let the lower bag sit
        // below frame, as it would when braced on the player's lap.
        _targetModelSize = Mathf.Clamp(
            FallbackModelSize * Configuration.BackpackViewSize.Value / 0.64f,
            0.34f,
            0.65f);

        _accessPosition = new Vector3(
            (horizontalPosition - 0.5f) * halfViewWidth * 2f,
            (verticalPosition - 0.5f) * halfViewHeight * 2f,
            PresentationDistance);
        _hiddenPosition = new Vector3(
            _accessPosition.x - halfViewWidth * 0.18f,
            -halfViewHeight - _targetModelSize * 0.65f,
            PresentationDistance + 0.04f);
    }

    private void CorrectPresentationProjection(
        Camera? camera,
        Transform pivot,
        Renderer[]? renderers)
    {
        if (camera == null || renderers == null || renderers.Length == 0)
        {
            return;
        }

        var hiddenOffset = _hiddenPosition - _accessPosition;
        pivot.localPosition = _accessPosition;
        pivot.localRotation = AccessRotation;

        if (!TryGetLocalRendererBounds(renderers, pivot, out var visualBounds))
        {
            pivot.localPosition = _hiddenPosition;
            pivot.localRotation = HiddenRotation;
            return;
        }

        var worldCenter = pivot.TransformPoint(visualBounds.center);
        var projectedCenter = camera.WorldToViewportPoint(worldCenter);
        if (projectedCenter.z <= camera.nearClipPlane)
        {
            pivot.localPosition = _hiddenPosition;
            pivot.localRotation = HiddenRotation;
            return;
        }

        var desiredWorldCenter = camera.ViewportToWorldPoint(new Vector3(
            Configuration.BackpackHorizontalPosition.Value,
            Configuration.BackpackVerticalPosition.Value,
            projectedCenter.z));
        var worldCorrection = desiredWorldCenter - worldCenter;
        var parent = pivot.parent;
        _accessPosition += parent
            ? parent!.InverseTransformVector(worldCorrection)
            : worldCorrection;
        _hiddenPosition = _accessPosition + hiddenOffset;

        pivot.localPosition = _hiddenPosition;
        pivot.localRotation = HiddenRotation;
    }

    private void PrepareModelRenderers(GameObject model)
    {
        _modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        _modelRendererStates = new bool[_modelRenderers.Length];
        _modelForceRenderingOffStates = new bool[_modelRenderers.Length];

        var hasEnabledRenderer = false;
        for (var index = 0; index < _modelRenderers.Length; index++)
        {
            var renderer = _modelRenderers[index];
            _modelRendererStates[index] = renderer.enabled;
            _modelForceRenderingOffStates[index] = renderer.forceRenderingOff;
            hasEnabledRenderer |= renderer.enabled && renderer.gameObject.activeInHierarchy;
        }

        for (var index = 0; index < _modelRenderers.Length; index++)
        {
            var renderer = _modelRenderers[index];
            if (!renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            // Pool state occasionally leaves forceRenderingOff set. If the
            // prefab has no enabled renderer at all, enable its active meshes as
            // a last-resort presentation fallback.
            renderer.forceRenderingOff = false;
            if (!hasEnabledRenderer)
            {
                renderer.enabled = true;
            }
        }

        if (_modelRenderers.Length == 0)
        {
            Plugin.LogSource?.LogWarning("The spawned backpack presentation has no renderers.");
        }
    }

    private void HideEquippedBackpackRenderers()
    {
        var slotView = _player.PlayerBody?.GetSlotViewByItem(_backpack);
        var renderers = slotView?.Renderers;
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        _equippedBackpackRenderers = renderers;
        _equippedBackpackRendererStates = new bool[renderers.Length];
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (!renderer)
            {
                continue;
            }

            _equippedBackpackRendererStates[index] = renderer.enabled;
            renderer.enabled = false;
        }
    }

    private void RestoreEquippedBackpackRenderers()
    {
        if (_equippedBackpackRenderers == null || _equippedBackpackRendererStates == null)
        {
            return;
        }

        var count = Math.Min(
            _equippedBackpackRenderers.Length,
            _equippedBackpackRendererStates.Length);
        for (var index = 0; index < count; index++)
        {
            var renderer = _equippedBackpackRenderers[index];
            if (renderer)
            {
                renderer.enabled = _equippedBackpackRendererStates[index];
            }
        }

        _equippedBackpackRenderers = null;
        _equippedBackpackRendererStates = null;
    }

    private void TryStartSearchSound()
    {
        try
        {
            if (_backpack is not SearchableItem searchableItem
                || string.IsNullOrWhiteSpace(searchableItem.SearchSound))
            {
                return;
            }

            var clip = Singleton<GUISounds>.Instance.GetLootingClip(searchableItem.SearchSound);
            if (!clip)
            {
                return;
            }

            _searchSource = _player.GetSearchSource(clip);
            if (!_searchSource)
            {
                return;
            }

            _searchSource.Loop = true;
            _searchSource.Position = _player.CameraPosition
                ? _player.CameraPosition.position
                : _player.Position;
            _searchSource.SetActive(true);
            _searchSource.Play(clip, null, 1f, 0.42f, true, false);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UseItemsAnywhere] Could not play backpack search sound: {exception.Message}");
            TryCleanup(StopSearchSound, "stop backpack search sound");
        }
    }

    private void StopSearchSound()
    {
        var searchSource = _searchSource;
        if (!searchSource)
        {
            return;
        }

        searchSource!.Stop(0.1f);

        if (_player)
        {
            _player.ReleaseSearchSource();
        }

        _searchSource = null;
    }

    private void ReleaseBackpackModel()
    {
        var backpackModel = _backpackModel;
        var backpackPivot = _backpackPivot;
        _backpackModel = null;
        _backpackPivot = null;

        try
        {
            if (!backpackModel)
            {
                RestoreBackpackModelState();
                return;
            }

            var originalName = _originalModelName;
            if (_hasOriginalModelScale)
            {
                backpackModel!.transform.localScale = _originalModelScale;
            }

            backpackModel!.transform.SetParent(null, false);
            RestoreBackpackModelState();
            if (originalName != null)
            {
                backpackModel.name = originalName;
            }

            AssetPoolObject.ReturnToPool(backpackModel, true);
        }
        finally
        {
            if (backpackPivot)
            {
                UnityEngine.Object.Destroy(backpackPivot);
            }
        }
    }

    private void DisablePhysics(GameObject model)
    {
        _modelColliders = model.GetComponentsInChildren<Collider>(true);
        _modelColliderStates = new bool[_modelColliders.Length];
        for (var index = 0; index < _modelColliders.Length; index++)
        {
            var collider = _modelColliders[index];
            _modelColliderStates[index] = collider.enabled;
            collider.enabled = false;
        }

        _modelRigidbodies = model.GetComponentsInChildren<Rigidbody>(true);
        _modelRigidbodyKinematicStates = new bool[_modelRigidbodies.Length];
        _modelRigidbodyCollisionStates = new bool[_modelRigidbodies.Length];
        for (var index = 0; index < _modelRigidbodies.Length; index++)
        {
            var rigidbody = _modelRigidbodies[index];
            _modelRigidbodyKinematicStates[index] = rigidbody.isKinematic;
            _modelRigidbodyCollisionStates[index] = rigidbody.detectCollisions;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
        }
    }

    private void SetLayerRecursively(GameObject model, int layer)
    {
        _modelTransforms = model.GetComponentsInChildren<Transform>(true);
        _modelLayers = new int[_modelTransforms.Length];
        for (var index = 0; index < _modelTransforms.Length; index++)
        {
            var child = _modelTransforms[index];
            _modelLayers[index] = child.gameObject.layer;
            child.gameObject.layer = layer;
        }
    }

    private void RestoreBackpackModelState()
    {
        if (_modelTransforms != null && _modelLayers != null)
        {
            var count = Math.Min(_modelTransforms.Length, _modelLayers.Length);
            for (var index = 0; index < count; index++)
            {
                var child = _modelTransforms[index];
                if (child)
                {
                    child.gameObject.layer = _modelLayers[index];
                }
            }
        }

        if (_modelColliders != null && _modelColliderStates != null)
        {
            var count = Math.Min(_modelColliders.Length, _modelColliderStates.Length);
            for (var index = 0; index < count; index++)
            {
                var collider = _modelColliders[index];
                if (collider)
                {
                    collider.enabled = _modelColliderStates[index];
                }
            }
        }

        if (_modelRigidbodies != null
            && _modelRigidbodyKinematicStates != null
            && _modelRigidbodyCollisionStates != null)
        {
            var count = Math.Min(
                _modelRigidbodies.Length,
                Math.Min(
                    _modelRigidbodyKinematicStates.Length,
                    _modelRigidbodyCollisionStates.Length));
            for (var index = 0; index < count; index++)
            {
                var rigidbody = _modelRigidbodies[index];
                if (rigidbody)
                {
                    rigidbody.isKinematic = _modelRigidbodyKinematicStates[index];
                    rigidbody.detectCollisions = _modelRigidbodyCollisionStates[index];
                }
            }
        }

        if (_modelRenderers != null
            && _modelRendererStates != null
            && _modelForceRenderingOffStates != null)
        {
            var count = Math.Min(
                _modelRenderers.Length,
                Math.Min(_modelRendererStates.Length, _modelForceRenderingOffStates.Length));
            for (var index = 0; index < count; index++)
            {
                var renderer = _modelRenderers[index];
                if (renderer)
                {
                    renderer.enabled = _modelRendererStates[index];
                    renderer.forceRenderingOff = _modelForceRenderingOffStates[index];
                }
            }
        }

        _modelTransforms = null;
        _modelLayers = null;
        _modelColliders = null;
        _modelColliderStates = null;
        _modelRigidbodies = null;
        _modelRigidbodyKinematicStates = null;
        _modelRigidbodyCollisionStates = null;
        _modelRenderers = null;
        _modelRendererStates = null;
        _modelForceRenderingOffStates = null;
        _originalModelName = null;
        _originalModelScale = Vector3.one;
        _hasOriginalModelScale = false;
    }

    private static void CenterAndNormalizeModel(
        GameObject model,
        Transform pivot,
        float targetModelSize)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (!TryGetLocalRendererBounds(renderers, pivot, out var bounds))
        {
            return;
        }

        var largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension > 0.01f)
        {
            var scale = Mathf.Clamp(targetModelSize / largestDimension, 0.25f, 1.4f);
            model.transform.localScale *= scale;

            if (!TryGetLocalRendererBounds(renderers, pivot, out bounds))
            {
                return;
            }
        }

        model.transform.localPosition -= bounds.center;
    }


    internal static bool TryGetLocalRendererBounds(Renderer[] renderers, Transform frame, out Bounds bounds)
    {
        bounds = default;
        var hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (!renderer
                || !renderer.enabled
                || renderer.forceRenderingOff
                || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            var localBounds = renderer.localBounds;
            var toFrame = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var point = toFrame.MultiplyPoint3x4(localBounds.center
                            + Vector3.Scale(localBounds.extents, new Vector3(x, y, z)));
                        if (!hasBounds)
                        {
                            bounds = new Bounds(point, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(point);
                        }
                    }
                }
            }
        }

        return hasBounds;
    }


    private static void TryCleanup(Action cleanup, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UseItemsAnywhere] Could not {operation}: {exception.Message}");
        }
    }
}
