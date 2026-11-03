using System;
using System.Reflection;
using UnityEngine;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private bool gameUIEffectsActive;
        private float nextGameUIEffectsRetry;
        private FieldInfo gameHudInstanceField, gameOverlayCameraInstanceField;
        private Canvas gameUIEffectsSourceCanvas;
        private RectTransform gameUIEffectsSourceRect;
        private Camera gameUIEffectsCamera;
        private GameUIEffectsStatus gameUIEffectsStatus;

        private enum GameUIEffectsStatus { Off, Active, Unavailable }

        // The game's compositor preserves its own aspect rather than stretching
        // to ultrawide/4:3 screens. Layout and world scaling must use that viewport.
        private float HudViewportAspect => gameUIEffectsActive && IsGameUIEffectsSourceReady()
            ? gameUIEffectsSourceRect.rect.width / gameUIEffectsSourceRect.rect.height
            : (float)Math.Max(1, Screen.width) / Math.Max(1, Screen.height);

        private void UpdateGameUIEffects()
        {
            if (root == null || scaler == null) return;
            if (!options.UseGameUIEffects)
            {
                RestoreScreenSpaceHud();
                gameUIEffectsSourceCanvas = null;
                gameUIEffectsSourceRect = null;
                gameUIEffectsCamera = null;
                nextGameUIEffectsRetry = 0;
                LogGameUIEffectsStatus(GameUIEffectsStatus.Off);
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now >= nextGameUIEffectsRetry)
            {
                nextGameUIEffectsRetry = now + .5f;
                try
                {
                    // Cache reflection metadata, not singleton values: scene loads replace them.
                    if (gameHudInstanceField == null)
                        gameHudInstanceField = FindGameUIInstanceField("HUDCanvas");
                    if (gameOverlayCameraInstanceField == null)
                        gameOverlayCameraInstanceField = FindGameUIInstanceField("CameraOverlay");
                    var hud = gameHudInstanceField?.GetValue(null) as Component;
                    var overlay = gameOverlayCameraInstanceField?.GetValue(null) as Component;
                    gameUIEffectsSourceCanvas = hud != null ? hud.GetComponent<Canvas>() : null;
                    gameUIEffectsSourceRect = hud != null ? hud.transform as RectTransform : null;
                    gameUIEffectsCamera = overlay != null ? overlay.GetComponent<Camera>() : null;
                }
                catch (Exception)
                {
                    // An optional game binding must not take down the normal HUD.
                    gameUIEffectsSourceCanvas = null;
                    gameUIEffectsSourceRect = null;
                    gameUIEffectsCamera = null;
                }
            }

            if (!IsGameUIEffectsSourceReady())
            {
                RestoreScreenSpaceHud();
                LogGameUIEffectsStatus(GameUIEffectsStatus.Unavailable);
                return;
            }

            gameUIEffectsActive = true;
            ApplyGameUIEffects();
        }

        private static FieldInfo FindGameUIInstanceField(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName, false);
                if (type == null || !typeof(Component).IsAssignableFrom(type)) continue;
                var field = type.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                if (field != null) return field;
            }
            return null;
        }

        private bool IsGameUIEffectsSourceReady()
        {
            return gameUIEffectsSourceCanvas != null && gameUIEffectsSourceCanvas.isActiveAndEnabled
                && gameUIEffectsSourceRect != null
                && gameUIEffectsSourceRect.rect.width > 0 && gameUIEffectsSourceRect.rect.height > 0
                && gameUIEffectsCamera != null && gameUIEffectsCamera.isActiveAndEnabled
                && gameUIEffectsCamera.targetTexture != null
                && (gameUIEffectsCamera.cullingMask & (1 << gameUIEffectsSourceCanvas.gameObject.layer)) != 0;
        }

        private void ApplyGameUIEffects()
        {
            if (root == null || scaler == null) return;
            if (!options.UseGameUIEffects || !gameUIEffectsActive || !IsGameUIEffectsSourceReady())
            {
                RestoreScreenSpaceHud();
                // CanvasScaler updates earlier than this tick; use the new FitToScreen scale now.
                root.GetComponent<Canvas>().scaleFactor = ScreenSpaceHudScale();
                LogGameUIEffectsStatus(options.UseGameUIEffects
                    ? GameUIEffectsStatus.Unavailable : GameUIEffectsStatus.Off);
                return;
            }

            var canvas = root.GetComponent<Canvas>();
            var rect = (RectTransform)root.transform;
            bool enteringWorldSpace = canvas.renderMode != RenderMode.WorldSpace;
            scaler.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = gameUIEffectsCamera;
            canvas.overrideSorting = true;
            canvas.sortingLayerID = gameUIEffectsSourceCanvas.sortingLayerID;
            canvas.sortingOrder = gameUIEffectsSourceCanvas.sortingOrder + 1;
            // Bitmap text meshes are already baked; no dynamic font pixel-density scaling.
            canvas.scaleFactor = 1;

            float height = Mathf.Max(1, scaler.referenceResolution.y);
            float width = height * HudViewportAspect;
            Rect hudRect = gameUIEffectsSourceRect.rect;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            // Never parent our persistent root to the scene-owned HUD.
            rect.position = gameUIEffectsSourceRect.TransformPoint(hudRect.center);
            rect.rotation = gameUIEffectsSourceRect.rotation;
            rect.localScale = Vector3.Scale(gameUIEffectsSourceRect.lossyScale,
                new Vector3(hudRect.height / height, hudRect.height / height, 1));

            int layer = gameUIEffectsSourceCanvas.gameObject.layer;
            if (enteringWorldSpace || root.layer != layer)
                SetGameUIEffectsLayer(rect, layer);
            LogGameUIEffectsStatus(GameUIEffectsStatus.Active);
        }

        private void RestoreScreenSpaceHud()
        {
            if (!gameUIEffectsActive) return;
            gameUIEffectsActive = false;
            var canvas = root.GetComponent<Canvas>();
            var rect = (RectTransform)root.transform;
            canvas.worldCamera = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = false;
            canvas.sortingLayerID = 0;
            canvas.sortingOrder = 30000;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.rotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            // Screen-space Canvas drives its own sizeDelta; do not retain the world viewport.
            SetGameUIEffectsLayer(rect, 0);
            scaler.enabled = true;
            canvas.scaleFactor = ScreenSpaceHudScale();
        }

        private float ScreenSpaceHudScale()
        {
            return Mathf.Max(1, Screen.height) / Mathf.Max(1, scaler.referenceResolution.y);
        }

        private static void SetGameUIEffectsLayer(Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            for (int i = 0; i < transform.childCount; i++)
                SetGameUIEffectsLayer(transform.GetChild(i), layer);
        }

        private void LogGameUIEffectsStatus(GameUIEffectsStatus status)
        {
            if (gameUIEffectsStatus == status) return;
            gameUIEffectsStatus = status;
            string message = status == GameUIEffectsStatus.Active
                ? "Native HUD game UI effects active: using the game's lower-resolution UI rendering; hover tooltips disabled."
                : status == GameUIEffectsStatus.Unavailable
                    ? "Native HUD game UI effects unavailable: using the normal HUD; retrying every 0.5 seconds."
                    : "Native HUD game UI effects disabled: using the normal HUD.";
            logRenderingStatus?.Invoke(message);
        }
    }
}
