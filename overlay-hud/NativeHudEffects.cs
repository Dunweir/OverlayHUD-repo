using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    // Animate a visual child independently of layout, timer updates and damage/loss effects.
    public sealed class NativeTextEntrance : MonoBehaviour
    {
        private NativeHud hud;
        private RectTransform motion;

        internal void Initialize(NativeHud owner, RectTransform visual)
        {
            hud = owner;
            motion = visual;
            Play();
        }

        private void OnEnable() => Play();

        internal void Play()
        {
            if (hud == null || !isActiveAndEnabled) return;
            StopAllCoroutines();
            StartCoroutine(Enter());
        }

        private System.Collections.IEnumerator Enter()
        {
            float start = Time.unscaledTime;
            while (hud.AnimateTextEntrance(motion, Time.unscaledTime - start)) yield return null;
            ResetMotion();
        }

        private void OnDisable() => ResetMotion();

        private void ResetMotion()
        {
            if (motion == null) return;
            motion.anchoredPosition = Vector2.zero;
            motion.localScale = Vector3.one;
        }
    }

    // Remaining is a fraction: 1 covers the entire rect, 0 clears it.
    public sealed class NativeCooldownSector : MaskableGraphic
    {
        private float remaining;
        internal bool ShowHand;
        internal float Remaining
        {
            get => remaining;
            set
            {
                value = float.IsNaN(value) ? 0 : Mathf.Clamp01(value);
                if (remaining == value) return;
                remaining = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            if (remaining <= 0 || rect.width <= 0 || rect.height <= 0 || color.a <= 0) return;

            float start = (1 - remaining) * 360;
            // Corner angles depend on aspect ratio, not just multiples of 45.
            float corner = Mathf.Atan2(rect.width, rect.height) * Mathf.Rad2Deg;
            mesh.AddVert(rect.center, color, Vector2.zero);
            Vector2 edge = RayEnd(rect, start);
            mesh.AddVert(edge, color, Vector2.zero);
            float angle = start;
            while (angle < 360)
            {
                float next = Mathf.Min(angle + 5, 360);
                next = IncludeCorner(angle, next, corner);
                next = IncludeCorner(angle, next, 180 - corner);
                next = IncludeCorner(angle, next, 180 + corner);
                next = IncludeCorner(angle, next, 360 - corner);
                mesh.AddVert(RayEnd(rect, next), color, Vector2.zero);
                int last = mesh.currentVertCount - 1;
                mesh.AddTriangle(0, last - 1, last);
                angle = next;
            }
            if (ShowHand)
            {
                Vector2 dir = (edge - rect.center).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x);
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(rect.center));
                Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(rect.center + normal));
                float pixel = 1 / Mathf.Max(.2f, Vector2.Distance(a, b));
                float core = Mathf.Max(1.35f - pixel / 2, .25f * pixel);
                int first = mesh.currentVertCount;
                for (int i = 0; i < 4; i++)
                {
                    // A one-render-pixel alpha ramp on either side also covers the low-res UI camera.
                    bool outside = i == 0 || i == 3;
                    float offset = (outside ? core + pixel : core) * (i < 2 ? -1 : 1);
                    Vector2 from = rect.center + normal * offset;
                    var tint = new Color(.72f, .72f, .72f, outside ? 0 : color.a);
                    mesh.AddVert(from, tint, Vector2.zero);
                    mesh.AddVert(RayEnd(rect, from, dir), tint, Vector2.zero);
                    if (i == 0) continue;
                    int n = first + 2 * i;
                    mesh.AddTriangle(n - 2, n, n + 1);
                    mesh.AddTriangle(n - 2, n + 1, n - 1);
                }
            }
        }

        private static float IncludeCorner(float angle, float next, float corner)
        {
            return corner > angle && corner < next ? corner : next;
        }

        private static Vector2 RayEnd(Rect rect, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            var ray = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)).normalized;
            return RayEnd(rect, rect.center, ray);
        }

        private static Vector2 RayEnd(Rect rect, Vector2 from, Vector2 ray)
        {
            float distance = rect.size.magnitude;
            if (Mathf.Abs(ray.x) > .000001f) distance = Mathf.Min(distance, ((ray.x > 0 ? rect.xMax : rect.xMin) - from.x) / ray.x);
            if (Mathf.Abs(ray.y) > .000001f) distance = Mathf.Min(distance, ((ray.y > 0 ? rect.yMax : rect.yMin) - from.y) / ray.y);
            return from + ray * distance;
        }
    }

    public sealed class NativeBatteryFrame : MaskableGraphic
    {
        private float ratio;

        internal void SetFill(float value, Color tint)
        {
            color = tint;
            if (ratio == value) return;
            ratio = Mathf.Clamp01(value);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            const int cells = 6;
            const float gap = 2;
            float width = (r.width - gap * (cells - 1)) / cells;
            for (int i = 0; i < cells; i++)
            {
                float x = r.xMin + i * (width + gap);
                AddRect(mesh, x, r.yMin, x + width, r.yMax, new Color32(17, 17, 17, 210));
                float fill = Mathf.Clamp01(ratio * cells - i);
                if (fill > 0) AddRect(mesh, x, r.yMin, x + width * fill, r.yMax, color);
            }
        }

        private static void AddRect(VertexHelper mesh, float left, float bottom, float right, float top, Color32 tint)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, bottom), tint, Vector2.zero);
            mesh.AddVert(new Vector3(left, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, bottom), tint, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2);
            mesh.AddTriangle(n, n + 2, n + 3);
        }
    }

    // The nominal outer edge is the RectTransform boundary; Thickness extends
    // inward. After changing fields or animated scale, call SetVerticesDirty().
    public sealed class NativeOutline : MaskableGraphic
    {
        internal float Radius = 11.2f;
        internal float Thickness = 3.28f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            float limit = Mathf.Min(rect.width, rect.height) * .5f;
            if (limit <= 0 || Thickness <= 0) return;
            float radius = Mathf.Clamp(Radius, 0, limit);
            float thickness = Mathf.Min(Thickness, limit);
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.zero));
            Vector2 screenX = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.right)) - origin;
            Vector2 screenY = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.up)) - origin;
            const int steps = 18;
            const int count = 4 * (steps + 1);
            for (int corner = 0; corner < 4; corner++)
            {
                for (int step = 0; step <= steps; step++)
                {
                    float angle = (corner * 90 + step * 90f / steps) * Mathf.Deg2Rad;
                    var normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    // Reciprocal-transpose normal length handles canvas scale,
                    // ring animation, rotation and non-uniform parent scaling.
                    float determinant = Mathf.Abs(screenX.x * screenY.y - screenX.y * screenY.x);
                    var screenNormal = new Vector2(screenY.y * normal.x - screenX.y * normal.y,
                        -screenY.x * normal.x + screenX.x * normal.y);
                    float pixel = determinant > .000001f ? screenNormal.magnitude / determinant : 1;
                    float feather = Mathf.Min(pixel, thickness);
                    Color solid = color;
                    solid.a *= Mathf.Min(1, thickness / Mathf.Max(pixel, .000001f));
                    Color transparent = solid;
                    transparent.a = 0;
                    mesh.AddVert(Point(rect, radius, -feather * .5f, corner, normal), transparent, Vector2.zero);
                    mesh.AddVert(Point(rect, radius, feather * .5f, corner, normal), solid, Vector2.zero);
                    mesh.AddVert(Point(rect, radius, thickness - feather * .5f, corner, normal), solid, Vector2.zero);
                    mesh.AddVert(Point(rect, radius, thickness + feather * .5f, corner, normal), transparent, Vector2.zero);
                }
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                for (int band = 0; band < 3; band++)
                {
                    int a = i * 4 + band, b = next * 4 + band;
                    mesh.AddTriangle(a, b, b + 1);
                    mesh.AddTriangle(a, b + 1, a + 1);
                }
            }
        }

        private static Vector2 Point(Rect rect, float radius, float inset, int corner, Vector2 normal)
        {
            inset = Mathf.Min(inset, Mathf.Min(rect.width, rect.height) * .5f);
            float r = Mathf.Max(0, radius - inset);
            float x = corner == 0 || corner == 3 ? rect.xMax - inset - r : rect.xMin + inset + r;
            float y = corner < 2 ? rect.yMax - inset - r : rect.yMin + inset + r;
            return new Vector2(x, y) + normal * r;
        }
    }

    // Caller supplies Phase = Repeat(unscaledTime, 8) / 8. Source is a cover
    // texture: UI/Default multiplies its RGB and alpha by the vertex color, so
    // use a white silhouette (new.png) for an unmodified shimmer tint. After
    // replacing Source call SetAllDirty(); texture lifetime belongs to caller.
    public sealed class NativeShimmer : MaskableGraphic
    {
        internal Texture2D Source = null;
        private float phase;
        private readonly List<Vector2> polygon = new List<Vector2>(8);
        private readonly List<Vector2> clipped = new List<Vector2>(8);
        private static readonly float[] Stops = { -.3f, -.12f, 0, .12f, .3f };
        private static readonly float[] Alphas = { 0, .25f, .8f, .25f, 0 };

        internal float Phase
        {
            get => phase;
            set
            {
                value = float.IsNaN(value) ? 0 : Mathf.Clamp01(value);
                if (phase == value) return;
                phase = value;
                SetVerticesDirty();
            }
        }

        public override Texture mainTexture => Source;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            if (Source == null || phase <= .75f || phase >= 1 || rect.width <= 0 || rect.height <= 0) return;
            float opacity = Mathf.InverseLerp(.75f, .77f, phase) * (1 - Mathf.InverseLerp(.96f, 1, phase));
            float progress = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.75f, .96f, phase));
            float tilt = Mathf.Tan(20 * Mathf.Deg2Rad);
            float reach = rect.width * .3f + rect.height * tilt * .5f;
            float center = Mathf.Lerp(rect.xMin - reach, rect.xMax + reach, progress);
            float bottom = center - rect.height * tilt * .5f;
            float top = center + rect.height * tilt * .5f;
            for (int strip = 0; strip < Stops.Length - 1; strip++)
            {
                float left = Stops[strip] * rect.width, right = Stops[strip + 1] * rect.width;
                polygon.Clear();
                polygon.Add(new Vector2(bottom + left, rect.yMin));
                polygon.Add(new Vector2(top + left, rect.yMax));
                polygon.Add(new Vector2(top + right, rect.yMax));
                polygon.Add(new Vector2(bottom + right, rect.yMin));
                Clip(polygon, clipped, rect.xMin, true);
                Clip(clipped, polygon, rect.xMax, false);
                int first = mesh.currentVertCount;
                foreach (var point in polygon)
                {
                    float across = point.x - center - (point.y - rect.center.y) * tilt;
                    Color tint = color;
                    tint.a *= opacity * Mathf.Lerp(Alphas[strip], Alphas[strip + 1], Mathf.InverseLerp(left, right, across));
                    var uv = new Vector2((point.x - rect.xMin) / rect.width, (point.y - rect.yMin) / rect.height);
                    mesh.AddVert(point, tint, uv);
                }
                for (int i = 1; i < polygon.Count - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }

        private static void Clip(List<Vector2> input, List<Vector2> output, float edge, bool keepRight)
        {
            output.Clear();
            if (input.Count == 0) return;
            Vector2 previous = input[input.Count - 1];
            bool wasInside = keepRight ? previous.x >= edge : previous.x <= edge;
            foreach (var current in input)
            {
                bool inside = keepRight ? current.x >= edge : current.x <= edge;
                if (inside != wasInside)
                {
                    float t = (edge - previous.x) / (current.x - previous.x);
                    output.Add(new Vector2(edge, Mathf.Lerp(previous.y, current.y, t)));
                }
                if (inside) output.Add(current);
                previous = current;
                wasInside = inside;
            }
        }
    }
}
