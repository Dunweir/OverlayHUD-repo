using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    internal sealed partial class NativeHud : IDisposable
    {
        private readonly NativeHudState state;
        private int columns;
        private bool horizontal;
        private NativeHudOptions options = new NativeHudOptions();
        private float layoutWidth;
        private CanvasScaler scaler;
        private float requestedScale = 1, offsetX, offsetY = 20;
        private int stripColumns = 11;
        private bool showHealth = true, showRespawn = true, showUpgrades = true, showStrength = true;
        private GameObject root;
        private RectTransform panel;
        private NativeFontAtlas teko, roboto, robotoBold;
        private CanvasGroup visibilityGroup;
        private float visibilityAlpha;
        private double displayedLevelStart = double.NaN;
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<int, Tile> tiles = new Dictionary<int, Tile>();
        private float nextRefresh;

        internal NativeHud(NativeHudState state, int columns, bool horizontal)
        {
            this.state = state;
            this.columns = columns;
            this.horizontal = horizontal;
            layoutWidth = NativeHudLayout.Width(horizontal, horizontal ? stripColumns : columns);
            try
            {
                teko = LoadFont("teko");
                roboto = LoadFont("roboto");
                robotoBold = LoadFont("roboto-bold");
                root = new GameObject("OverlayHUD.Native", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                UnityEngine.Object.DontDestroyOnLoad(root);
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;
                scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1;
                panel = Rect("Panel", root.transform, 0, 20, layoutWidth, 180);
                panel.anchorMin = panel.anchorMax = new Vector2(.5f, 1);
                panel.pivot = new Vector2(.5f, 1);
                visibilityGroup = panel.gameObject.AddComponent<CanvasGroup>();
                visibilityGroup.blocksRaycasts = false;
                visibilityGroup.interactable = false;
                CreateHeader();
                CreateAlerts();
                CreateTooltips();
                root.SetActive(false);
            }
            catch { Dispose(); throw; }
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static NativeBitmapText Text(Transform parent, NativeFontAtlas font, float size, float x, float y, float w, float h)
        {
            var label = Rect("Text", parent, x, y, w, h).gameObject.AddComponent<NativeBitmapText>();
            label.Atlas = font;
            label.Size = size;
            label.raycastTarget = false;
            return label;
        }

        private static Color TierColor(int? tier)
        {
            switch (tier)
            {
                case 1: return new Color32(76, 175, 80, 255);
                case 2: return new Color32(33, 150, 243, 255);
                case 3: return new Color32(244, 67, 54, 255);
                default: return new Color32(223, 184, 91, 255);
            }
        }

        internal void Configure(NativeHudOptions settings)
        {
            if (ReferenceEquals(options, settings)) return;
            options = settings;
            nextRefresh = 0;
            requestedScale = options.Scale;
            offsetX = options.OffsetX;
            offsetY = options.OffsetY;
            columns = options.Columns;
            horizontal = options.HorizontalLayout;
            stripColumns = options.HorizontalColumns;
            showHealth = options.ShowHealth;
            showRespawn = options.ShowRespawnTimer;
            showUpgrades = options.ShowUpgrades;
            showStrength = options.ShowStrength;
        }

        private void FitToScreen()
        {
            float scale = NativeHudLayout.FitScale(requestedScale, Screen.width, Screen.height, layoutWidth, panel.sizeDelta.y);
            scaler.referenceResolution = new Vector2(1920 / scale, 1080 / scale);
            float viewportHeight = 1080 / scale;
            float viewportWidth = viewportHeight * Screen.width / Math.Max(1, Screen.height);
            float edgeOffset = Mathf.Max(0, (viewportWidth - layoutWidth) / 2 - 20);
            float anchorOffset = options.Alignment == "Left" ? -edgeOffset : options.Alignment == "Right" ? edgeOffset : 0;
            panel.anchoredPosition = new Vector2(
                NativeHudLayout.ClampX(anchorOffset + offsetX, viewportWidth, layoutWidth),
                -NativeHudLayout.ClampY(offsetY, viewportHeight, panel.sizeDelta.y) - 18 * (1 - visibilityAlpha));
        }

        internal void Tick(float now)
        {
            UpdateAlerts(now);
            // Game UI suppression and the user's master switch must be immediate.
            if (state.Hidden || !options.Enabled)
            {
                HideTooltips();
                root.SetActive(false);
                visibilityAlpha = 0;
                nextRefresh = 0;
                return;
            }
            visibilityAlpha = options.Animations
                ? Mathf.MoveTowards(visibilityAlpha, state.Visible ? 1 : 0, Time.unscaledDeltaTime / .28f)
                : state.Visible ? 1 : 0;
            if (!state.Visible && visibilityAlpha <= 0)
            {
                HideTooltips();
                root.SetActive(false);
                return;
            }
            if (!root.activeSelf) { root.SetActive(true); nextRefresh = 0; }
            visibilityGroup.alpha = visibilityAlpha;
            if (state.Visible && (now >= nextRefresh || displayedLevelStart != state.StartedAt))
            {
                nextRefresh = now + .1f;
                // Instance IDs can be reused across levels; do not crossfade stale enemies.
                if (displayedLevelStart != state.StartedAt)
                {
                    foreach (var tile in tiles.Values) UnityEngine.Object.Destroy(tile.Root.gameObject);
                    tiles.Clear();
                    displayedLevelStart = state.StartedAt;
                }
                float gridY = RefreshHeader(now);
                var ordered = state.GetDisplayMonsters(now);
                ordered.RemoveAll(monster => !options.ShouldDisplayMonster(monster));
                var displayedIds = new HashSet<int>();
                foreach (var monster in ordered) displayedIds.Add(monster.Id);
                foreach (int id in new List<int>(tiles.Keys))
                    if (!displayedIds.Contains(id))
                    {
                        UnityEngine.Object.Destroy(tiles[id].Root.gameObject);
                        tiles.Remove(id);
                    }
                int gridColumns = horizontal ? stripColumns : columns;
                float rowPitch = NativeHudLayout.MonsterRowPitch(options.MonsterSize, showHealth);
                for (int index = 0; index < ordered.Count; index++)
                {
                    var monster = ordered[index];
                    bool created = !tiles.TryGetValue(monster.Id, out Tile tile);
                    if (created) tiles.Add(monster.Id, tile = CreateTile(now));
                    int rowCount = Math.Min(gridColumns, ordered.Count - index / gridColumns * gridColumns);
                    float rowWidth = rowCount * (options.MonsterSize + 10) - 10;
                    float rowX = options.Alignment == "Left" ? 10 : options.Alignment == "Right" ? layoutWidth - 10 - rowWidth : (layoutWidth - rowWidth) / 2;
                    tile.TargetPosition = new Vector2(rowX + index % gridColumns * (options.MonsterSize + 10),
                        -gridY - index / gridColumns * rowPitch);
                    tile.Root.localScale = Vector3.one * (options.MonsterSize / NativeHudLayout.MonsterSize);
                    if (created) tile.Position = tile.TargetPosition;
                    UpdateTile(tile, monster, now, created);
                }
                int rows = (ordered.Count + gridColumns - 1) / gridColumns;
                panel.sizeDelta = new Vector2(layoutWidth, gridY + rows * rowPitch);
            }
            AnimateHeader(Time.unscaledDeltaTime);
            Animate(now);
            FitToScreen();
            PositionAlerts();
            if (state.Visible) UpdateTooltips(); else HideTooltips();
        }

        private Texture2D Icon(string name)
        {
            string key = (name ?? "new").ToLowerInvariant();
            switch (key)
            {
                case "peeper": case "peepers": key = "eye_monster"; break;
                case "bangers": key = "banger"; break;
                case "animals": key = "animal"; break;
                case "upscreams": key = "upscream"; break;
                case "bowties": key = "bowtie"; break;
                case "mentalists": key = "mentalist"; break;
                case "spewers": key = "spewer"; break;
                case "gnomes": key = "gnome"; break;
                case "chef": case "chefs": key = "chef_frog"; break;
                case "clown": key = "clown_beamer"; break;
                case "shadow child": key = "shadow_child"; break;
                case "apex predator": key = "apex_predator"; break;
                case "pony": key = "snatcher"; break;
            }
            if (!HasResource(key + ".png")) key = "new";
            return Texture(key + ".png");
        }

        private NativeFontAtlas LoadFont(string name)
        {
            var atlas = JsonConvert.DeserializeObject<NativeFontAtlas>(System.Text.Encoding.UTF8.GetString(Read(name + "-atlas.json")));
            atlas.Texture = Texture(name + "-atlas.png");
            atlas.Index = new Dictionary<int, NativeGlyph>();
            foreach (var glyph in atlas.glyphs) atlas.Index.Add(glyph.code, glyph);
            return atlas;
        }

        private Texture2D Texture(string name)
        {
            if (textures.TryGetValue(name, out var texture)) return texture;
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            // Bind Unity 6's netstandard 2.1 image API at runtime; the plugin is net472.
            try
            {
                var conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
                var loadImage = conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (loadImage == null || !(bool)loadImage.Invoke(null, new object[] { texture, Read(name), true }))
                    throw new InvalidDataException("Cannot decode native HUD asset " + name);
                texture.name = name;
                texture.filterMode = FilterMode.Trilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                textures.Add(name, texture);
                return texture;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
        }

        private static string ResourceName(string name) => "OverlayHUD.NativeAssets." + name;
        private static bool HasResource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ResourceName(name)) != null;
        private static byte[] Read(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(name)))
            using (var memory = new MemoryStream())
            {
                if (stream == null) throw new FileNotFoundException("Missing native HUD resource " + name);
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            foreach (var texture in textures.Values) UnityEngine.Object.Destroy(texture);
            textures.Clear();
        }
    }

    // Rounded fill also serves as the stencil mask for monster artwork.
    public sealed class NativeRoundedPanel : MaskableGraphic
    {
        internal float Radius = 9;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            if (rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Min(Radius, Mathf.Min(rect.width, rect.height) / 2);
            mesh.AddVert(rect.center, color, Vector2.zero);
            const int steps = 12;
            const int count = 4 * (steps + 1);
            float feather = 1f / Mathf.Max(.01f, canvas != null ? canvas.scaleFactor : 1);
            for (int ring = 0; ring < 2; ring++)
            {
                Color tint = color;
                if (ring == 1) tint.a = 0;
                for (int corner = 0; corner < 4; corner++)
                {
                    float cx = corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius;
                    float cy = corner < 2 ? rect.yMax - radius : rect.yMin + radius;
                    for (int step = 0; step <= steps; step++)
                    {
                        float angle = (corner * 90 + step * 90f / steps) * Mathf.Deg2Rad;
                        mesh.AddVert(new Vector3(cx + Mathf.Cos(angle) * (radius + ring * feather),
                            cy + Mathf.Sin(angle) * (radius + ring * feather)), tint, Vector2.zero);
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                mesh.AddTriangle(0, i + 1, next + 1);
                mesh.AddTriangle(i + 1, count + i + 1, count + next + 1);
                mesh.AddTriangle(i + 1, count + next + 1, next + 1);
            }
        }
    }

    internal sealed class NativeGlyph
    {
        public int code { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public int left { get; set; }
        public int top { get; set; }
        public float advance { get; set; }
    }
    internal sealed class NativeFontAtlas
    {
        public float size { get; set; }
        public float lineTop { get; set; }
        public float lineBottom { get; set; }
        public NativeGlyph[] glyphs { get; set; }
        [JsonIgnore] internal Texture2D Texture;
        [JsonIgnore] internal Dictionary<int, NativeGlyph> Index;
    }

    // One UI mesh per label, with embedded glyphs rather than OS font fallback.
    public sealed class NativeBitmapText : MaskableGraphic
    {
        internal NativeFontAtlas Atlas;
        internal float Size;
        internal bool Centered;
        private string value = "";
        internal string Value
        {
            set { if (this.value == value) return; this.value = value; SetVerticesDirty(); }
        }
        public override Texture mainTexture => Atlas?.Texture;
        internal float Measure(string text)
        {
            float advance = 0;
            foreach (char c in text)
                advance += (Atlas.Index.TryGetValue(c, out var glyph) ? glyph : Atlas.Index['?']).advance;
            return advance * Size / Atlas.size;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Atlas == null) return;
            float scale = Size / Atlas.size;
            float totalAdvance = Measure(value) / scale;
            if (totalAdvance > 0) scale = Mathf.Min(scale, rectTransform.rect.width / totalAdvance);
            var bounds = rectTransform.rect;
            float x = bounds.xMin + (Centered ? (bounds.width - totalAdvance * scale) / 2f : 0);
            float baseline = NativeHudLayout.TextBaseline(bounds.center.y, Atlas.lineTop, Atlas.lineBottom, scale);
            foreach (char c in value)
            {
                if (!Atlas.Index.TryGetValue(c, out var glyph)) glyph = Atlas.Index['?'];
                float left = x + glyph.left * scale;
                float top = baseline - glyph.top * scale;
                float right = left + glyph.width * scale;
                float bottom = top - glyph.height * scale;
                float u = (float)glyph.x / Atlas.Texture.width;
                float v = 1f - (float)glyph.y / Atlas.Texture.height;
                float u2 = (float)(glyph.x + glyph.width) / Atlas.Texture.width;
                float v2 = 1f - (float)(glyph.y + glyph.height) / Atlas.Texture.height;
                int start = mesh.currentVertCount;
                mesh.AddVert(new Vector3(left, bottom), color, new Vector2(u, v2));
                mesh.AddVert(new Vector3(left, top), color, new Vector2(u, v));
                mesh.AddVert(new Vector3(right, top), color, new Vector2(u2, v));
                mesh.AddVert(new Vector3(right, bottom), color, new Vector2(u2, v2));
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start + 2, start + 3, start);
                x += glyph.advance * scale;
            }
        }
    }
}
