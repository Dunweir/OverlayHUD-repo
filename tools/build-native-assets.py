"""Run: uv run --with pillow python tools/build-native-assets.py

Bake existing artwork and fonts for the experimental Unity renderer.
Requires Windows Segoe UI Emoji (the installed font is not redistributed).
No font installation or Unity Editor is required.

Monster textures are centered 256x256 object-fit: cover crops. Glow textures
are 384x384: the SAME cover alpha at (64, 64), blurred with Gaussian radius
16, with white RGB for runtime tinting. At 105x105 the footprint is 70x70.
Card shadow uses that same padded footprint and a .16 corner radius. Draw
at 105x105, offset 5px down, tint black at .28 alpha for the CSS 70px card.
CSS blur 14px is Gaussian sigma 7px (25.6 texture pixels at this scale).
All output and glyph bounds are asserted during generation.
Atlas glyph rectangles exclude padding; lineTop/lineBottom use the same
Pillow default text anchor coordinates as glyph top, at font size 72.
"""
from pathlib import Path
import json
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "overlay-hud" / "NativeAssets"
ICON_SIZE = 256
PADDING = 64
PADDED_SIZE = ICON_SIZE + PADDING * 2


def assert_bounds(bounds, size):
    left, top, right, bottom = bounds
    assert 0 <= left <= right <= size[0], (bounds, size)
    assert 0 <= top <= bottom <= size[1], (bounds, size)


def cover(image):
    return ImageOps.fit(image, (ICON_SIZE, ICON_SIZE), method=Image.Resampling.LANCZOS)


def padded_alpha(icon):
    assert icon.size == (ICON_SIZE, ICON_SIZE)
    mask = Image.new("L", (PADDED_SIZE, PADDED_SIZE))
    bounds = (PADDING, PADDING, PADDING + ICON_SIZE, PADDING + ICON_SIZE)
    assert_bounds(bounds, mask.size)
    mask.paste(icon.getchannel("A"), (PADDING, PADDING))
    assert ImageChops.difference(mask.crop(bounds), icon.getchannel("A")).getbbox() is None
    return mask


def tinted_mask(alpha):
    image = Image.new("RGBA", alpha.size, (255, 255, 255, 0))
    image.putalpha(alpha)
    return image


def save_checked(image, filename, expected_size):
    assert image.size == expected_size, (filename, image.size, expected_size)
    assert image.mode == "RGBA", filename
    bounds = image.getchannel("A").getbbox()
    assert bounds is not None, filename
    assert_bounds(bounds, image.size)
    image.save(OUT / filename)
    with Image.open(OUT / filename) as saved:
        assert saved.size == expected_size and saved.mode == "RGBA", filename
        assert saved.tobytes() == image.tobytes(), filename


def test_cover_bounds():
    # Opaque center / transparent ends catch contain and stretch regressions,
    # for both portrait and landscape art, without depending on source artwork.
    for size, bounds in [((512, 256), (128, 0, 383, 255)),
                         ((256, 512), (0, 128, 255, 383))]:
        source = Image.new("RGBA", size)
        ImageDraw.Draw(source).rectangle(bounds, fill="white")
        icon = cover(source)
        assert icon.size == (ICON_SIZE, ICON_SIZE)
        assert icon.getchannel("A").getextrema() == (255, 255)
        mask = padded_alpha(icon)
        assert mask.getbbox() == (PADDING, PADDING, PADDING + ICON_SIZE, PADDING + ICON_SIZE)


test_cover_bounds()
# Fail before updating assets if the system emoji font is unavailable.
emoji_font_path = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/seguiemj.ttf"
emoji_font = ImageFont.truetype(str(emoji_font_path), 256)
OUT.mkdir(exist_ok=True)
for source in list((ROOT / "assets/monsters").glob("*.webp")) + list((ROOT / "assets/upgrades").glob("*.webp")):
    icon = Image.open(source).convert("RGBA")
    if source.parent.name == "monsters":
        icon = cover(icon)
        save_checked(icon, source.stem + ".png", (ICON_SIZE, ICON_SIZE))
        mask = padded_alpha(icon)
        glow = tinted_mask(mask.filter(ImageFilter.GaussianBlur(16)))
        save_checked(glow, "glow-" + source.stem + ".png", (PADDED_SIZE, PADDED_SIZE))
    else:
        # Upgrade artwork retains its original contain sizing and aspect ratio.
        icon.thumbnail((256, 256), Image.Resampling.LANCZOS)
        assert 0 < icon.width <= 256 and 0 < icon.height <= 256
        save_checked(icon, "upgrade-" + source.stem + ".png", icon.size)

# A 256px rounded silhouette plus 64px padding on each side. Pillow's blur
# radius is sigma, whereas CSS box-shadow blur is approximately twice sigma.
shadow_mask = Image.new("L", (PADDED_SIZE, PADDED_SIZE))
ImageDraw.Draw(shadow_mask).rounded_rectangle(
    (PADDING, PADDING, PADDING + ICON_SIZE - 1, PADDING + ICON_SIZE - 1),
    radius=ICON_SIZE * .16, fill=255)
assert shadow_mask.getbbox() == (PADDING, PADDING, PADDING + ICON_SIZE, PADDING + ICON_SIZE)
shadow_sigma = (14 / 2) * PADDED_SIZE / 105
shadow_alpha = shadow_mask.filter(ImageFilter.GaussianBlur(shadow_sigma))
assert shadow_alpha.getpixel((PADDED_SIZE // 2, PADDED_SIZE // 2)) >= 250
# Padding keeps the residual edge alpha below 1%, avoiding a visible cutoff.
assert shadow_alpha.crop((0, 0, PADDED_SIZE, 1)).getextrema()[1] <= 2
assert shadow_alpha.crop((0, 0, 1, PADDED_SIZE)).getextrema()[1] <= 2
assert shadow_alpha.crop((0, PADDED_SIZE - 1, PADDED_SIZE, PADDED_SIZE)).getextrema()[1] <= 2
assert shadow_alpha.crop((PADDED_SIZE - 1, 0, PADDED_SIZE, PADDED_SIZE)).getextrema()[1] <= 2
save_checked(tinted_mask(shadow_alpha), "card-shadow.png", (PADDED_SIZE, PADDED_SIZE))

# Rasterize the installed Windows color glyphs; ship only the resulting PNGs.
for name, emoji in [("strength", "\U0001f4aa"), ("destruction", "\U0001f4a5")]:
    left, top, right, bottom = emoji_font.getbbox(emoji)
    emoji_image = Image.new("RGBA", (right - left, bottom - top))
    ImageDraw.Draw(emoji_image).text((-left, -top), emoji, font=emoji_font, embedded_color=True)
    emoji_image = emoji_image.crop(emoji_image.getbbox())
    emoji_image.thumbnail((256, 256), Image.Resampling.LANCZOS)
    emoji_square = Image.new("RGBA", (256, 256))
    emoji_square.paste(emoji_image, ((256 - emoji_image.width) // 2, (256 - emoji_image.height) // 2))
    save_checked(emoji_square, name + "-emoji.png", (256, 256))

# Rasterize at 3x the usual text size. Keep original typefaces, including
# Roboto for Cyrillic and health labels. Atlas coordinates use a top-left origin.
characters = ''.join(chr(c) for c in list(range(32, 127)) + list(range(0x400, 0x460)))
for name, filename in [("teko", "Teko.ttf"), ("roboto", "Roboto-ExtraBold.ttf"),
                       ("roboto-bold", "Roboto-Bold.ttf")]:
    font = ImageFont.truetype(str(ROOT / "assets/fonts" / filename), 72)
    # Teko is a static regular face; synthesize the heavier browser weight.
    stroke = 1 if name == "teko" else 0
    # White transparent texels avoid dark fringes when Unity filters straight alpha.
    atlas = Image.new("RGBA", (2048, ((len(characters) + 15) // 16) * 128), (255, 255, 255, 0))
    draw = ImageDraw.Draw(atlas)
    glyphs = []
    for i, char in enumerate(characters):
        x, y = (i % 16) * 128, (i // 16) * 128
        left, top, right, bottom = font.getbbox(char, stroke_width=stroke)
        if right - left > 96 or bottom - top > 96:
            raise ValueError(f"{name}: {char!r} exceeds the 96x96 padded glyph area")
        draw.text((x + 16 - left, y + 16 - top), char, font=font,
                  fill="white", stroke_width=stroke, stroke_fill="white")
        width, height = right - left, bottom - top
        assert_bounds((x + 16, y + 16, x + 16 + width, y + 16 + height), atlas.size)
        ink = atlas.getchannel("A").crop((x, y, x + 128, y + 128)).getbbox()
        if ink is not None:
            assert 16 <= ink[0] <= ink[2] <= 16 + width, (name, char, ink)
            assert 16 <= ink[1] <= ink[3] <= 16 + height, (name, char, ink)
        glyphs.append(dict(code=ord(char), x=x+16, y=y+16, width=width,
                           height=height, left=left, top=top, advance=font.getlength(char)))
    save_checked(atlas, name + "-atlas.png", (2048, ((len(characters) + 15) // 16) * 128))
    _, line_top, _, line_bottom = font.getbbox("Hg")
    (OUT / (name + "-atlas.json")).write_text(
        json.dumps(dict(size=72, lineTop=line_top, lineBottom=line_bottom, glyphs=glyphs)),
        encoding="utf-8")

print("Native assets generated; cover, glow alignment, shadow, PNG and glyph bounds checks passed.")
