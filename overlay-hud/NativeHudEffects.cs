using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    // Remaining is a fraction: 1 covers the card, 0 clears it. Use black at .78
    // alpha and a parent rounded stencil mask; this primitive clips to a square.
    public sealed class NativeCooldownSector : MaskableGraphic
    {
        private float remaining;
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
            if (remaining <= 0 || rect.width <= 0 || rect.height <= 0) return;

            float start = (1 - remaining) * 360;
            // Corner angles depend on aspect ratio, not just multiples of 45.
            float corner = Mathf.Atan2(rect.width, rect.height) * Mathf.Rad2Deg;
            mesh.AddVert(rect.center, color, Vector2.zero);
            AddRay(mesh, rect, start);
            float angle = start;
            while (angle < 360)
            {
                float next = Mathf.Min(angle + 5, 360);
                next = IncludeCorner(angle, next, corner);
                next = IncludeCorner(angle, next, 180 - corner);
                next = IncludeCorner(angle, next, 180 + corner);
                next = IncludeCorner(angle, next, 360 - corner);
                AddRay(mesh, rect, next);
                int last = mesh.currentVertCount - 1;
                mesh.AddTriangle(0, last - 1, last);
                angle = next;
            }
        }

        private static float IncludeCorner(float angle, float next, float corner)
        {
            return corner > angle && corner < next ? corner : next;
        }

        private void AddRay(VertexHelper mesh, Rect rect, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            var ray = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)).normalized;
            // Intersect the ray with the square instead of using an inscribed
            // circle. Its diagonal is an upper bound even at an axial ray.
            float distance = rect.size.magnitude;
            if (Mathf.Abs(ray.x) > .000001f) distance = Mathf.Min(distance, rect.width * .5f / Mathf.Abs(ray.x));
            if (Mathf.Abs(ray.y) > .000001f) distance = Mathf.Min(distance, rect.height * .5f / Mathf.Abs(ray.y));
            mesh.AddVert(rect.center + ray * distance, color, Vector2.zero);
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
