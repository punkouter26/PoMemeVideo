import os
import struct
import zlib

def make_png(width, height, rgba_bytes):
    raw = bytearray()
    row_bytes = width * 4
    for y in range(height):
        raw.append(0)  # filter type 0: None
        raw.extend(rgba_bytes[y * row_bytes : (y + 1) * row_bytes])

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        crc = struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
        return c + crc

    ihdr = struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw), 9)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', idat) + chunk(b'IEND', b'')

def set_pixel(buf, w, x, y, r, g, b, a):
    if 0 <= x < w and 0 <= y < len(buf) // (w * 4):
        idx = (y * w + x) * 4
        buf[idx] = r
        buf[idx + 1] = g
        buf[idx + 2] = b
        buf[idx + 3] = a

def generate_deal_with_it(output_path):
    w, h = 320, 80
    buf = bytearray(w * h * 4)
    scale = 8
    pixel_art = [
        "  BBBBBBBBB      BBBBBBBBB  ",
        " BBBWWBBBBBB    BBBWWBBBBBB ",
        "BBBBWBBBBBBBB  BBBBWBBBBBBBB",
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBB",
        " BBBBBBBBBBB    BBBBBBBBBBB ",
        "  BBBBBBBBB      BBBBBBBBB  ",
    ]
    grid_w = len(pixel_art[0])
    grid_h = len(pixel_art)
    start_x = (w - grid_w * scale) // 2
    start_y = (h - grid_h * scale) // 2

    for gy, row in enumerate(pixel_art):
        for gx, ch in enumerate(row):
            if ch == ' ':
                continue
            r, g, b, a = (0, 0, 0, 255) if ch == 'B' else (255, 255, 255, 255)
            for dy in range(scale):
                for dx in range(scale):
                    set_pixel(buf, w, start_x + gx * scale + dx, start_y + gy * scale + dy, r, g, b, a)

    with open(output_path, 'wb') as f:
        f.write(make_png(w, h, buf))

def generate_laser_eyes(output_path):
    w, h = 320, 100
    buf = bytearray(w * h * 4)
    eyes = [(110, 50), (210, 50)]
    for ex, ey in eyes:
        for y in range(h):
            for x in range(w):
                dx = x - ex
                dy = y - ey
                dist = (dx * dx + dy * dy) ** 0.5
                flare_y = abs(dy)
                if dist < 12:
                    intensity = max(0, 1.0 - dist / 12)
                    set_pixel(buf, w, x, y, 255, 255, int(255 * intensity), 255)
                elif dist < 35:
                    alpha = int(255 * (1.0 - (dist - 12) / 23))
                    set_pixel(buf, w, x, y, 255, 30, 30, alpha)
                elif flare_y < 8 and ((ex < 160 and dx < 0) or (ex > 160 and dx > 0)):
                    beam_alpha = int(180 * (1.0 - flare_y / 8))
                    set_pixel(buf, w, x, y, 255, 20, 20, beam_alpha)

    with open(output_path, 'wb') as f:
        f.write(make_png(w, h, buf))

def generate_red_circle(output_path):
    w, h = 240, 240
    buf = bytearray(w * h * 4)
    cx, cy = 120, 120
    radius = 90
    thickness = 14
    for y in range(h):
        for x in range(w):
            dx = x - cx
            dy = y - cy
            dist = (dx * dx + dy * dy) ** 0.5
            if abs(dist - radius) <= thickness:
                alpha = 240 if abs(dist - radius) < thickness - 2 else 160
                set_pixel(buf, w, x, y, 255, 10, 10, alpha)

    for t in range(25, 75):
        for offset in range(-6, 7):
            set_pixel(buf, w, t + offset, t - offset, 255, 15, 15, 240)
    for s in range(16):
        set_pixel(buf, w, 75 - s, 75, 255, 15, 15, 240)
        set_pixel(buf, w, 75, 75 - s, 255, 15, 15, 240)

    with open(output_path, 'wb') as f:
        f.write(make_png(w, h, buf))

def generate_explosion(output_path):
    w, h = 240, 240
    buf = bytearray(w * h * 4)
    cx, cy = 120, 120
    import math
    for y in range(h):
        for x in range(w):
            dx = x - cx
            dy = y - cy
            dist = (dx * dx + dy * dy) ** 0.5
            angle = math.atan2(dy, dx)
            spikes = math.cos(angle * 8)
            star_radius = 65 + 35 * spikes
            if dist < star_radius:
                if dist < star_radius * 0.5:
                    set_pixel(buf, w, x, y, 255, 240, 30, 255)
                elif dist < star_radius * 0.85:
                    set_pixel(buf, w, x, y, 255, 120, 20, 255)
                else:
                    set_pixel(buf, w, x, y, 230, 30, 20, 255)

    with open(output_path, 'wb') as f:
        f.write(make_png(w, h, buf))

def generate_thug_life(output_path):
    w, h = 300, 70
    buf = bytearray(w * h * 4)
    banner_pad = 4
    for y in range(banner_pad, h - banner_pad):
        for x in range(banner_pad, w - banner_pad):
            set_pixel(buf, w, x, y, 10, 10, 10, 230)
    for x in range(w):
        for t in range(3):
            set_pixel(buf, w, x, t, 218, 165, 32, 255)
            set_pixel(buf, w, x, h - 1 - t, 218, 165, 32, 255)
    for y in range(h):
        for t in range(3):
            set_pixel(buf, w, t, y, 218, 165, 32, 255)
            set_pixel(buf, w, w - 1 - t, y, 218, 165, 32, 255)

    with open(output_path, 'wb') as f:
        f.write(make_png(w, h, buf))

def generate_clown_wig(output_path):
    w, h = 240, 160
    buf = bytearray(w * h * 4)
    import math
    for y in range(h // 2):
        for x in range(w):
            dx = x - 120
            dy = y - 60
            dist = (dx * dx + dy * dy) ** 0.5
            if 30 < dist < 70:
                angle = (math.atan2(dy, dx) + math.pi) / (2 * math.pi)
                if angle < 0.25: r, g, b = 255, 50, 50
                elif angle < 0.5: r, g, b = 255, 200, 30
                elif angle < 0.75: r, g, b = 30, 200, 50
                else: r, g, b = 50, 100, 255
                set_pixel(buf, w, x, y, r, g, b, 255)
    for y in range(h):
        for x in range(w):
            dx = x - 120
            dy = y - 110
            if dx * dx + dy * dy < 22 * 22:
                dist = (dx * dx + dy * dy) ** 0.5
                shade = max(0, 1.0 - dist / 22)
                set_pixel(buf, w, x, y, 240, int(30 * shade), int(30 * shade), 255)

    with open(output_path, 'wb') as f:
        f.write(make_png(w, h, buf))

if __name__ == '__main__':
    target_dir = os.path.join(os.path.dirname(__file__), '..', 'src', 'PoMemeVideo.Client', 'wwwroot', 'overlays')
    os.makedirs(target_dir, exist_ok=True)
    generate_deal_with_it(os.path.join(target_dir, 'deal-with-it.png'))
    generate_laser_eyes(os.path.join(target_dir, 'laser-eyes.png'))
    generate_red_circle(os.path.join(target_dir, 'red-circle.png'))
    generate_explosion(os.path.join(target_dir, 'explosion.png'))
    generate_thug_life(os.path.join(target_dir, 'thug-life.png'))
    generate_clown_wig(os.path.join(target_dir, 'clown-wig.png'))
    print('Generated 6 meme overlays successfully in:', target_dir)

