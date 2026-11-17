"""Draw the app icon: an orange stopwatch with a yen sign. Writes assets/app.ico and assets/icon_256.png."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFont

S = 1024
ORANGE = (238, 107, 20, 255)
DEEP = (196, 78, 6, 255)
FACE = (255, 252, 247, 255)

img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
cx, cy, R = S // 2, S // 2 + 60, 400
# crown and side button
d.rounded_rectangle((cx - 70, cy - R - 120, cx + 70, cy - R + 10), 30, fill=DEEP)
d.rounded_rectangle((cx - 120, cy - R - 150, cx + 120, cy - R - 90), 28, fill=ORANGE)
ang = math.radians(-45)
bx, by = cx + (R + 20) * math.cos(ang), cy + (R + 20) * math.sin(ang)
d.polygon([(bx + 60 * math.cos(ang + a), by + 60 * math.sin(ang + a)) for a in (math.pi * 0.75, -math.pi * 0.75, -math.pi * 0.25, math.pi * 0.25)], fill=DEEP)
# ring and face
d.ellipse((cx - R, cy - R, cx + R, cy + R), fill=ORANGE)
r2 = R - 70
d.ellipse((cx - r2, cy - r2, cx + r2, cy + r2), fill=FACE)
for k in range(12):
    a = math.radians(k * 30 - 90)
    r_out, r_in = r2 - 28, r2 - (80 if k % 3 == 0 else 55)
    d.line((cx + r_in * math.cos(a), cy + r_in * math.sin(a), cx + r_out * math.cos(a), cy + r_out * math.sin(a)),
           fill=DEEP if k % 3 == 0 else (240, 170, 120, 255), width=26 if k % 3 == 0 else 16)
font = ImageFont.truetype(os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', 'segoeuib.ttf'), 430)
d.text((cx, cy + 10), '¥', font=font, fill=ORANGE, anchor='mm')

out = sys.argv[1]
img.resize((256, 256), Image.LANCZOS).save(os.path.join(out, 'icon_256.png'))
img.save(os.path.join(out, 'app.ico'), sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print('ok')
