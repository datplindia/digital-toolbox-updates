"""Generate app.ico for Battery Guard (battery with a lightning bolt)."""
import sys
from PIL import Image, ImageDraw

out = sys.argv[1]
S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# rounded background tile
d.rounded_rectangle([8, 8, S - 8, S - 8], radius=56, fill=(31, 58, 147, 255))

# battery body
bx0, by0, bx1, by1 = 52, 78, 188, 178
d.rounded_rectangle([bx0, by0, bx1, by1], radius=18, outline=(255, 255, 255, 255), width=12)
# battery cap
d.rounded_rectangle([bx1 + 4, 108, bx1 + 22, 148], radius=6, fill=(255, 255, 255, 255))
# charge fill (80%)
d.rounded_rectangle([bx0 + 18, by0 + 18, bx0 + 18 + int((bx1 - bx0 - 36) * 0.8), by1 - 18],
                    radius=8, fill=(52, 211, 153, 255))
# lightning bolt
bolt = [(132, 66), (92, 134), (120, 134), (106, 192), (152, 118), (124, 118), (140, 66)]
d.polygon(bolt, fill=(255, 214, 10, 255), outline=(31, 58, 147, 255))

img.save(out, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
img.save(out.replace(".ico", "_preview.png"))
print("ok", out)
