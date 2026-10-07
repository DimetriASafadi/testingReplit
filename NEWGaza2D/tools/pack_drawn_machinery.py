#!/usr/bin/env python3
"""Pack newly generated 2D machinery paintings (art-src/drawn-machinery/*.png) into
trimmed Phaser atlases. Pure 2D: sheet cells are keyed, scaled to one apparent size per
machine, pivoted on the centre of the wheel/track footprint; the excavator work cycle is a
72-frame 2D cut-out rig (painted chassis + painted boom/stick/bucket) solved with IK so the
bucket tooth tip reaches the dig point and the real truck-bed centre; the truck's loaded
travel frames get a painted rubble heap in every heading. No 3D / Unity sources are read."""
import json, hashlib, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter, ImageOps, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'art-src/drawn-machinery'
OUT = ROOT / 'public/art/machinery'
SIZE, ORTHO = 256, 13.2 * 256 / 224
FOOT = (128, 196)
HEADINGS = 8
WORK_FRAMES = 72
ATLAS_W = 2048
SCREEN = .66                      # fleet-animation sprite scale (atlas px -> world px)
TRUCK_OFFSET = (-36.48 / SCREEN, 27.84 / SCREEN)   # iso([1.65,1.75]) - iso([1.50,1.03]); fleet-motion workDock
DIG_TARGET = (-50 / SCREEN, -5 / SCREEN)           # foreground heap, not the street or beneath the cab
TARGET = {'excavator': 70, 'bulldozer': 65, 'truck': 80}   # sqrt(opaque area) of travel frames
EXC_S = .21                      # rig scale: src px -> atlas px (chassis ~ 40 world px)

def cell(r, c): return (c * 256 + 5, r * 256 + 5, c * 256 + 251, r * 256 + 251)
TRAVEL = {
  'excavator': {0: cell(0, 0), 1: (5, 5, 507, 507, 'portrait'), 2: cell(0, 2), 3: cell(1, 1), 4: cell(2, 3), 5: (517, 5, 1019, 507, 'portrait'), 6: cell(2, 3), 7: cell(1, 1)},
  'bulldozer': {0: cell(0, 0), 1: (5, 517, 507, 1019, 'portrait'), 2: cell(0, 2), 3: cell(1, 2), 4: 'rear-dozer', 5: cell(3, 0), 6: 'rear-dozer', 7: cell(1, 1)},
  'truck':     {0: cell(0, 0), 1: 'front-axial', 2: cell(0, 0), 3: cell(0, 1), 4: cell(0, 2), 5: 'rear-axial', 6: cell(0, 2), 7: cell(0, 1)},
}
MIRRORED = {'excavator': {6, 7}, 'bulldozer': {4}, 'truck': {0, 4, 7}}
DOZER = {'lowering': (258, 20, 510, 228), 'lowered': (514, 20, 766, 228), 'raised': (514, 530, 766, 740)}
TRUCKW = {'fill0': (2, 45, 252, 245), 'fill1': (258, 45, 508, 245), 'fill2': (514, 45, 764, 245),
          'fill3': (770, 40, 1018, 245), 'tip0': (620, 780, 925, 1020), 'tip1': (620, 520, 950, 760),
          'tip2': (95, 775, 445, 1015)}

def key(img, box, erase=None, tol=22, hole=250):
    im = img.crop(box).convert('RGB'); a = np.asarray(im).astype(int); h, w, _ = a.shape
    border = np.concatenate([a[:4].reshape(-1, 3), a[:, :4].reshape(-1, 3), a[:, -4:].reshape(-1, 3)])
    bg = np.median(border, axis=0)
    like = (np.abs(a - bg).max(axis=2) <= tol)
    lab = np.zeros((h, w), int); n = 0; isbg = [False]
    for y in range(h):
        for x in range(w):
            if like[y, x] and not lab[y, x]:
                n += 1; stack = [(y, x)]; lab[y, x] = n; cnt = 0; edge = False
                while stack:
                    cy, cx = stack.pop(); cnt += 1
                    if cy in (0, h - 1) or cx in (0, w - 1): edge = True
                    for ny, nx in ((cy+1, cx), (cy-1, cx), (cy, cx+1), (cy, cx-1)):
                        if 0 <= ny < h and 0 <= nx < w and like[ny, nx] and not lab[ny, nx]:
                            lab[ny, nx] = n; stack.append((ny, nx))
                isbg.append(edge or cnt > hole)
    bgmask = np.array(isbg)[lab] & (lab > 0)
    fg = ~bgmask
    if erase:
        x1, y1 = erase; fg[y1:, :x1] = False
    # keep the largest component and sizeable satellites (rubble), drop lines/labels
    lab2 = np.zeros((h, w), int); sizes = []; n = 0
    for y in range(h):
        for x in range(w):
            if fg[y, x] and not lab2[y, x]:
                n += 1; stack = [(y, x)]; lab2[y, x] = n; cnt = 0
                while stack:
                    cy, cx = stack.pop(); cnt += 1
                    for dy in (-2, -1, 0, 1, 2):
                        for dx in (-2, -1, 0, 1, 2):
                            ny, nx = cy + dy, cx + dx
                            if 0 <= ny < h and 0 <= nx < w and fg[ny, nx] and not lab2[ny, nx]:
                                lab2[ny, nx] = n; stack.append((ny, nx))
                sizes.append(cnt)
    keep = [i + 1 for i, s in enumerate(sizes) if s >= max(sizes) * .02 and s > 120]
    fg = np.isin(lab2, keep)
    alpha = Image.fromarray((fg * 255).astype('uint8')).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(.6))
    out = im.convert('RGBA'); out.putalpha(alpha)
    return out

def lum(a): return a[..., 0] * .3 + a[..., 1] * .59 + a[..., 2] * .11

def foot_of(img):
    """Centre of the wheel/track footprint: middle of the dark ground-contact band."""
    a = np.asarray(img).astype(float); al = a[..., 3] > 128
    ys, xs = np.nonzero(al); H = ys.max() - ys.min() + 1
    sel = al & (lum(a) < 105); sel[: int(ys.max() - .42 * H)] = False
    if sel.sum() < 30:
        return ((xs.min() + xs.max()) / 2, ys.max() - .15 * H)
    yy, xx = np.nonzero(sel)
    return ((xx.min() + xx.max()) / 2, (yy.min() + yy.max()) / 2)

def area_sqrt(img): return math.sqrt(np.count_nonzero(np.asarray(img.getchannel('A')) > 128))

def place(img, foot, s, dy=0, mirror=False):
    if mirror:
        img = ImageOps.mirror(img); foot = (img.width - foot[0], foot[1])
    im = img.resize((max(1, round(img.width * s)), max(1, round(img.height * s))), Image.LANCZOS)
    ox, oy = round(FOOT[0] - foot[0] * s), round(FOOT[1] - foot[1] * s + dy)
    big = Image.new('RGBA', (SIZE * 3, SIZE * 3), (0, 0, 0, 0)); big.alpha_composite(im, (ox + SIZE, oy + SIZE))
    fr = big.crop((SIZE, SIZE, SIZE * 2, SIZE * 2))
    clipped = np.count_nonzero(np.asarray(big.getchannel('A')) > 6) - np.count_nonzero(np.asarray(fr.getchannel('A')) > 6)
    return fr, clipped

# ---------------------------------------------------------------- rubble material
def rubble_patch(fill3):
    """Pure stone texture cut from the painted heap in the new fill3 truck frame."""
    g = ImageOps.autocontrast(ImageOps.grayscale(fill3.convert('RGB').crop((112, 33, 200, 62))), cutoff=1)
    return ImageOps.colorize(g, (58, 55, 50), (214, 207, 194))

def heap_layer(size, patch, cx, cy, rx, ry, seed):
    """Rubble mound painted from the generated rubble texture, with a ragged deterministic rim."""
    rng = np.random.RandomState(seed); ph = rng.uniform(0, 6.28, 4)
    W, H = size; mask = Image.new('L', (W * 2, H * 2), 0); d = ImageDraw.Draw(mask)
    pts = []
    for i in range(72):
        t = i / 72 * 6.2832
        r = 1 + .13 * math.sin(3 * t + ph[0]) + .09 * math.sin(7 * t + ph[1]) + .05 * math.sin(13 * t + ph[2])
        pts.append(((cx + rx * r * math.cos(t)) * 2, (cy + ry * r * math.sin(t) * (1.18 if math.sin(t) < 0 else .8)) * 2))
    d.polygon(pts, fill=255); mask = mask.resize((W, H), Image.LANCZOS)
    tile = Image.new('RGB', (W, H)); pw, ph_ = patch.size
    for ty in range(0, H, ph_):
        for tx in range(0, W, pw):
            p = patch if ((tx // pw + ty // ph_) % 2 == 0) else ImageOps.mirror(patch)
            tile.paste(p, (tx, ty))
    # soft top-light shading so the mound reads as 3D
    sh = Image.linear_gradient('L').resize((W, H)); sh = ImageOps.invert(sh)
    tile = Image.composite(tile, Image.new('RGB', (W, H), (60, 58, 54)), Image.eval(sh, lambda v: 185 + v // 5))
    out = tile.convert('RGBA'); out.putalpha(mask.filter(ImageFilter.GaussianBlur(.5)))
    return out

def green_mask(img):
    a = np.asarray(img).astype(int)
    return (a[..., 3] > 128) & (a[..., 1] > a[..., 0] + 12) & (a[..., 1] > a[..., 2] + 6) & (a[..., 1] > 55)

def bed_geom(img):
    g = green_mask(img); ys, xs = np.nonzero(g)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max(); w, h = x1 - x0, y1 - y0
    return g, (x0, y0, x1, y1), ((x0 + x1) / 2, y0 + (.30 if w > .55 * h else .45) * h)

def load_truck(img, patch, seed, amount=1):
    """Paint a heaped load into the visible bed opening of any heading."""
    g, (x0, y0, x1, y1), (cx, cy) = bed_geom(img); w, h = x1 - x0, y1 - y0
    if h > 1.5 * w:   rx, ry, cy = .36 * w, .30 * h, y0 + .46 * h          # looking along the bed
    elif w > 1.9 * h: rx, ry, cy = .36 * w, .26 * h, y0 + .34 * h          # side-on
    else:             rx, ry, cy = .33 * w, .26 * h, y0 + .34 * h    # three-quarter
    lay = heap_layer(img.size, patch, cx, cy + ry * (1-amount)*.3, rx*max(.35,amount), ry*max(.35,amount), seed)
    a = np.asarray(img).astype(int)
    # never paint over cab, wheels or chassis: only bed-green or empty pixels
    l = lum(a); sat = a[..., :3].max(2) - a[..., :3].min(2)
    ok = (a[..., 3] >= 20) & ~(((l > 172) & (sat < 34)) | (l < 38))
    la = np.asarray(lay.getchannel('A')).astype(float) * ok
    lay.putalpha(Image.fromarray(la.astype('uint8')))
    out = img.copy(); out.alpha_composite(lay); return out

# ---------------------------------------------------------------- excavator 2D cut-out rig
def rot(v, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return (v[0] * c + v[1] * s, -v[0] * s + v[1] * c)
def ang(v): return math.degrees(math.atan2(-v[1], v[0]))

class Rig:
    Kb, Ks, Kk = .60, .86, .35   # bucket width must be smaller than the tracked chassis
    K = .78                                  # arm scale relative to chassis paint
    def __init__(self, patch):
        sheet = Image.open(SRC / 'excavator-rig.png').convert('RGB')
        ch = key(sheet, (30, 130, 490, 520))
        self.chassis = ImageOps.mirror(ch)                        # faces screen right
        self.cfoot = foot_of(self.chassis)
        # Chassis and tracks are one immutable painting. Never mirror/squash
        # their separate fragments halfway through a digging cycle.
        self.mount = (ch.width - 155.0, 165.0)                    # boom foot bracket (src px, chassis crop)
        arm = key(sheet, (520, 40, 1000, 360)); self.R0, self.E0, self.S0 = (20, 252), (395, 98), (400, 298)
        a = np.asarray(arm.getchannel('A')).copy()
        # A vertical split retained the left edge of the hanging stick in the
        # boom, creating a second arm. Follow the actual joint silhouette.
        masks = []
        for polygon in ([(0,0),(390,0),(390,85),(415,96),(415,119),(376,137),(376,320),(0,320)],
                        [(425,0),(480,0),(480,320),(382,320),(382,138),(407,122),(413,92)]):
            mask = Image.new('L', arm.size, 0); ImageDraw.Draw(mask).polygon(polygon, fill=255)
            masks.append((a.astype(float)*np.asarray(mask)/255).astype('uint8'))
        la, ra = masks
        self.boom, self.stick = arm.copy(), arm.copy()
        self.boom.putalpha(Image.fromarray(la)); self.stick.putalpha(Image.fromarray(ra))
        bk = ImageOps.mirror(key(sheet, (620, 680, 915, 970)))
        self.bp, self.btip = (112, 52), (282, 205)
        self.buckets = []
        for lv in (0, .25, .5, .75, 1):
            b = bk.copy()
            if lv:
                b.alpha_composite(heap_layer(b.size, patch, 172, 126, 52 + 34 * lv, 18 + 30 * lv, 11 + int(lv * 8)))
            self.buckets.append(b)
        self.CW, self.CH, self.F = 2400, 2300, (1200, 1750)
        self.L2 = self.Ks * math.dist(self.E0, self.S0)
        self.M = (self.F[0] + self.mount[0] - self.cfoot[0], self.F[1] + self.mount[1] - self.cfoot[1])

    def ik(self, tip, phi):
        """Bucket tip (canvas src px) + bucket attitude -> boom angle, stick attitude."""
        bt = (self.Kk * (self.btip[0] - self.bp[0]), self.Kk * (self.btip[1] - self.bp[1]))
        r = rot(bt, phi); P = (tip[0] - r[0], tip[1] - r[1])
        b0 = (self.Kb * (self.E0[0] - self.R0[0]), self.Kb * (self.E0[1] - self.R0[1]))
        s0 = (self.Ks * (self.S0[0] - self.E0[0]), self.Ks * (self.S0[1] - self.E0[1]))
        vx, vy = P[0]-self.M[0], P[1]-self.M[1]
        distance = max(.000001, math.hypot(vx,vy))
        L1 = math.hypot(*b0)
        reach = max(abs(L1-self.L2)+.00001, min(L1+self.L2-.00001,distance))
        # Clamp a too-close bucket pin; never detach it from the rigid stick.
        P = (self.M[0]+vx*reach/distance,self.M[1]+vy*reach/distance)
        theta = math.atan2(vy,vx)
        bend = math.acos(max(-1,min(1,(reach*reach+L1*L1-self.L2*self.L2)/(2*reach*L1))))
        elbows = [(self.M[0]+L1*math.cos(theta+a),self.M[1]+L1*math.sin(theta+a)) for a in (-bend,bend)]
        E = min(elbows,key=lambda e:e[1])
        a1 = ang((E[0]-self.M[0],E[1]-self.M[1]))-ang(b0)
        v = (P[0] - E[0], P[1] - E[1]); a12 = ang(v) - ang(s0)
        return a1, a12, E, P, abs(reach-distance)

    def render(self, tip, phi, load, left):
        a1, a12, E, P, err = self.ik(tip, phi)
        cv = Image.new('RGBA', (self.CW, self.CH), (0, 0, 0, 0))
        cv.alpha_composite(self.chassis, (round(self.F[0] - self.cfoot[0]), round(self.F[1] - self.cfoot[1])))
        def part(img, pivot, at, deg, k):
            im = img.resize((round(img.width * k), round(img.height * k)), Image.LANCZOS)
            lay = Image.new('RGBA', (self.CW, self.CH), (0, 0, 0, 0))
            px, py = round(at[0] - pivot[0] * k), round(at[1] - pivot[1] * k)
            lay.alpha_composite(im, (px, py))
            return lay.rotate(deg, Image.BICUBIC, center=at)
        cv.alpha_composite(part(self.boom, self.R0, self.M, a1, self.Kb))
        cv.alpha_composite(part(self.stick, self.E0, E, a12, self.Ks))
        bk = self.buckets[int(round(load * 4))]
        cv.alpha_composite(part(bk, self.bp, P, phi, self.Kk))
        bt = rot((self.Kk * (self.btip[0] - self.bp[0]), self.Kk * (self.btip[1] - self.bp[1])), phi)
        T = (P[0] + bt[0], P[1] + bt[1])
        return cv, T, err

def smooth(t): return t * t * (3 - 2 * t)

def build_excavator_cycle(rig, bed):
    """bed = truck bed centre relative to excavator anchor (atlas px). Returns frames, tips, phases."""
    dx, dy = -DIG_TARGET[0], DIG_TARGET[1]      # right-facing equivalent of the left dig point
    bx, by = -bed[0], bed[1]  # all work frames keep the same left-facing chassis
    # (t, tipX, tipY, bucket attitude, load, faces_left)  tip relative to anchor, atlas px
    K = [(0.00, dx + 6, dy - 38, -12, 0, True),
         (0.12, dx, dy, 8, 0, True),                       # dig_in: teeth on the rubble
         (0.20, dx - 10, dy - 4, -35, .5, True),
         (0.28, dx - 16, dy - 12, -75, 1, True),           # scooped
         (0.40, dx * .80, dy - 58, -82, 1, True),          # keep the tool clear of the cab
         (0.50, bx, by - 48, -84, 1, True),
          (0.60, bx, by - 38, -80, 1, True),
          (0.68, bx, by - 14, -74, 1, True),              # over actual bed
          (0.76, bx, by - 8, -20, .5, True),             # dump
          (0.82, bx, by - 4, 22, 0, True),
          (0.90, dx + 6, dy - 38, -30, 0, True),
         (1.00, dx + 6, dy - 38, -12, 0, True)]
    def sample(t):
        for i in range(len(K) - 1):
            a, b = K[i], K[i + 1]
            if a[0] <= t <= b[0]:
                u = 0 if b[0] == a[0] else (t - a[0]) / (b[0] - a[0]); u = smooth(u)
                if abs(b[0] - a[0]) < 1e-3: u = 0
                lerp = lambda p, q: p + (q - p) * u
                return lerp(a[1], b[1]), lerp(a[2], b[2]), lerp(a[3], b[3]), lerp(a[4], b[4]), a[5]
        return K[-1][1:]
    frames, tips, worst, bad = [], [], 0, []
    for n in range(WORK_FRAMES):
        t = n / WORK_FRAMES
        x, y, phi, load, left = sample(t)
        # left-facing frames are the mirror image, so the right-facing target is (-x) for dig side
        tipc = (rig.F[0] + (x / EXC_S), rig.F[1] + (y / EXC_S))
        cv, T, err = rig.render(tipc, phi, load, left); worst = max(worst, err)
        if err > 4: bad.append([n, round(err, 1)])
        s = EXC_S
        im = cv.resize((round(cv.width * s), round(cv.height * s)), Image.LANCZOS)
        fx, fy = rig.F[0] * s, rig.F[1] * s
        tx, ty = (T[0]) * s, (T[1]) * s
        if left:
            im = ImageOps.mirror(im); fx = im.width - fx; tx = im.width - tx
        ox, oy = round(FOOT[0] - fx), round(FOOT[1] - fy)
        big = Image.new('RGBA', (SIZE * 3, SIZE * 3), (0, 0, 0, 0))
        big.alpha_composite(im, (ox + SIZE, oy + SIZE))
        fr = big.crop((SIZE, SIZE, SIZE * 2, SIZE * 2))
        clip = np.count_nonzero(np.asarray(big.getchannel('A')) > 6) - np.count_nonzero(np.asarray(fr.getchannel('A')) > 6)
        frames.append((fr, clip)); tips.append([round(tx + ox, 2), round(ty + oy, 2)])
    return frames, tips, (worst, bad)

def pack(frames):
    """Trim + shelf-pack into <=ATLAS_W atlas. frames: {name: RGBA 256x256}"""
    items = []
    for n, im in frames.items():
        bb = im.getchannel('A').point(lambda v: 255 if v > 3 else 0).getbbox() or (0, 0, 1, 1)
        bb = (max(0, bb[0] - 1), max(0, bb[1] - 1), min(SIZE, bb[2] + 1), min(SIZE, bb[3] + 1))
        items.append((n, im.crop(bb), bb))
    items.sort(key=lambda i: -i[1].height)
    x = y = rowh = 0; place_ = []
    for n, c, bb in items:
        if x + c.width + 2 > ATLAS_W: x = 0; y += rowh + 2; rowh = 0
        place_.append((n, c, bb, x, y)); x += c.width + 2; rowh = max(rowh, c.height)
    H = y + rowh + 2; H = (H + 3) // 4 * 4
    atlas = Image.new('RGBA', (ATLAS_W, H), (0, 0, 0, 0)); js = {}
    for n, c, bb, px, py in place_:
        atlas.paste(c, (px, py))
        js[n] = {'frame': {'x': px, 'y': py, 'w': c.width, 'h': c.height}, 'rotated': False, 'trimmed': True,
                 'sourceSize': {'w': SIZE, 'h': SIZE},
                 'spriteSourceSize': {'x': bb[0], 'y': bb[1], 'w': c.width, 'h': c.height}}
    return atlas, js

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    M = {'size': SIZE, 'ortho': ORTHO, 'foot': [float(FOOT[0]), float(FOOT[1])], 'headings': HEADINGS,
         'cycleFrames': WORK_FRAMES, 'clips': {}, 'frames': {}, 'phases': {}, 'bed': {}, 'atlases': {},
         'source': 'generated 2D paintings (art-src/drawn-machinery); cut-out rig for excavator'}
    h = hashlib.sha1(); notes = {}
    tsheet = Image.open(SRC / 'truck-work.png').convert('RGB')
    truckw = {n: key(tsheet, b, None, 7, 10 ** 9) for n, b in TRUCKW.items()}
    patch = rubble_patch(truckw['fill3'])

    def travel_frames(kind):
        sheet = Image.open(SRC / f'{kind}-travel.png').convert('RGB'); out = []
        for k in range(HEADINGS):
            b = TRAVEL[kind][k]
            if isinstance(b, str):
                if b == 'rear-dozer':
                    axial = Image.open(SRC / 'bulldozer-rear.png').convert('RGB')
                    im = key(axial, (5,5,axial.width-5,axial.height-5))
                else:
                    axial = Image.open(SRC / 'truck-axial.png').convert('RGB')
                    half = axial.width // 2; left = 0 if b == 'front-axial' else half
                    im = key(axial, (left + 5, 5, left + half - 5, axial.height - 5))
            elif len(b) == 5:
                im = key(Image.open(SRC / f'{kind}-portraits.png').convert('RGB'), b[:4])
            else: im = key(sheet, b)
            out.append(ImageOps.mirror(im) if k in MIRRORED[kind] else im)
        return out

    for kind in ('truck', 'bulldozer', 'excavator'):
        frames, clipped = {}, 0
        def add(name, im, foot, s, dy=0, mirror=False, tip=None, truck=False):
            nonlocal clipped
            fr, c = place(im, foot, s, dy, mirror); clipped += c; frames[name] = fr
            M['frames'][f'{kind}:{name}'] = {'tip': tip or [FOOT[0], FOOT[1]], 'worldTip': [0, 0]}
        trav = travel_frames(kind)
        s_t = TARGET[kind] / float(np.median([area_sqrt(t) for t in trav]))
        sk = [TARGET[kind] / area_sqrt(t) for t in trav]
        drive = []
        for k in range(HEADINGS):
            for roll in (0, 1):
                n = f'drive_{k}_{roll}'; add(n, trav[k], foot_of(trav[k]), sk[k]); drive.append(n)
        M['clips'][f'{kind}:drive'] = drive
        if kind == 'truck':
            wf = {}
            for n, im in truckw.items(): wf[n] = im
            s_w = TARGET['truck'] / area_sqrt(wf['fill0'])
            base = wf['fill0']
            for level in range(1,4):
                wf[f'fill{level}'] = load_truck(base, patch, 113, level/3)
            for n in ('fill0', 'fill1', 'fill2', 'fill3', 'tip0', 'tip1', 'tip2'):
                add('w_' + n, wf[n], foot_of(base) if n.startswith('fill') else foot_of(wf[n]), s_w)
            loaded = []
            for k in range(HEADINGS):
                ld = load_truck(trav[k], patch, 100 + k)
                for roll in (0, 1):
                    n = f'loaded_{k}_{roll}'; add(n, ld, foot_of(trav[k]), sk[k]); loaded.append(n)
            M['clips']['truck:loaded'] = loaded
            M['clips']['truck:fill'] = ['w_fill0', 'w_fill1', 'w_fill2', 'w_fill3']
            M['clips']['truck:work'] = ['w_fill3', 'w_tip0', 'w_tip1', 'w_tip2']
            # authoritative bed centre (atlas px, absolute frame coords) measured on the painted bed
            bed = {}
            for n in ('fill0', 'fill3'):
                fr = frames['w_' + n]; g, (x0, y0, x1, y1), c = bed_geom(fr)
                bed[n] = {'centre': [round(float(c[0]), 2), round(float(c[1]), 2)], 'rim': [int(x0), int(y0), int(x1), int(y1)]}
            M['bed'] = bed
        elif kind == 'bulldozer':
            ds = Image.open(SRC / 'bulldozer-work.png').convert('RGB')
            dz = {n: key(ds, b) for n, b in DOZER.items()}
            s_w = TARGET['bulldozer'] / float(np.median([area_sqrt(v) for v in dz.values()]))
            ref = foot_of(dz['lowered'])
            for n, im in dz.items():
                bb = np.asarray(im.getchannel('A')) > 128; ys, xs = np.nonzero(bb)
                f = foot_of(im)
                for roll in (0, 1):
                    fr, c = place(im, f, s_w, roll); clipped += c
                    a = np.asarray(fr.getchannel('A')) > 128; yy, xx = np.nonzero(a)
                    ytip = yy[xx <= xx.min() + 6].max()      # blade lower-left corner
                    nm = f'w_{n}_{roll}'; frames[nm] = fr
                    M['frames'][f'bulldozer:{nm}'] = {'tip': [int(xx.min()), int(ytip)], 'worldTip': [0, 0]}
            cyc = []
            # raised -> lowering -> lowered (push, 2-frame track shimmy) -> raised (reverse)
            plan = [('raised', 8), ('lowering', 4), ('lowered', 34), ('lowering', 3), ('raised', 23)]
            i = 0
            for n, cnt in plan:
                for _ in range(cnt): cyc.append(f'w_{n}_{(i // 2) % 2}'); i += 1
            assert len(cyc) == WORK_FRAMES, len(cyc)
            M['clips']['bulldozer:work'] = cyc
            f0 = lambda a, b: round(a / WORK_FRAMES, 4)
            M['phases']['bulldozer'] = {'approach': [0, .11], 'lower': [.11, .17], 'push': [.17, .64], 'lift': [.64, .69], 'reverse': [.69, 1]}
        else:
            rig = Rig(patch)
            tf = M['frames']
            # bed centre in truck frame vs excavator anchor: truck anchor offset + painted bed centre rel truck foot
            bc = M['bed']['fill0']['centre']
            bed_rel = (TRUCK_OFFSET[0] + bc[0] - FOOT[0], TRUCK_OFFSET[1] + bc[1] - FOOT[1])
            cyc, tips, (worst, bad) = build_excavator_cycle(rig, bed_rel)
            names = []
            for n, (fr, c) in enumerate(cyc):
                nm = f'work_{n:03d}'; frames[nm] = fr; clipped += c; names.append(nm)
                M['frames'][f'excavator:{nm}'] = {'tip': tips[n], 'worldTip': [round((tips[n][0]-FOOT[0])*SCREEN,2), round((tips[n][1]-FOOT[1])*SCREEN,2)]}
            M['clips']['excavator:work'] = names
            M['phases']['excavator'] = {'dig_in': [.04, .12], 'dig': [.12, .28], 'lift': [.28, .42], 'swing': [.42, .58],
                                        'over_bed': [.58, .70], 'dump': [.70, .84], 'return': [.84, 1]}
            M['phases']['excavator']['ikWorstErrorSrcPx'] = round(worst, 2); M['phases']['excavator']['ikUnreachedFrames'] = bad; print('unreached', bad)
            M['bed']['excavatorTarget'] = [round(bed_rel[0], 2), round(bed_rel[1], 2)]
            M['bed']['digTarget'] = [round(DIG_TARGET[0], 2), round(DIG_TARGET[1], 2)]
        atlas, js = pack(frames)
        assert atlas.height <= 4096
        atlas.save(OUT / f'{kind}.webp', quality=88, method=6, exact=True)
        h.update((OUT / f'{kind}.webp').read_bytes())
        (OUT / f'{kind}.json').write_text(json.dumps({'frames': js, 'meta': {'app': 'pack_drawn_machinery', 'image': f'{kind}.webp',
            'size': {'w': atlas.width, 'h': atlas.height}, 'scale': '1'}}))
        pv = frames['drive_2_0'] if kind != 'excavator' else frames['work_020']
        pc = pv.crop(pv.getbbox()); s = 240 / max(pc.size); pc = pc.resize((round(pc.width * s), round(pc.height * s)), Image.LANCZOS)
        pvf = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); pvf.alpha_composite(pc, ((256 - pc.width) // 2, (256 - pc.height) // 2))
        pvf.save(OUT / f'{kind}-preview.webp', quality=90)
        M['atlases'][kind] = {'w': atlas.width, 'h': atlas.height, 'count': len(frames),
                              'bytes': (OUT / f'{kind}.webp').stat().st_size, 'clippedPixels': int(clipped)}
        print(kind, M['atlases'][kind], flush=True)
    M['revision'] = h.hexdigest()[:12]
    (ROOT / 'src/machinery-manifest.json').write_text(json.dumps(M, indent=1))

if __name__ == '__main__':
    main()
