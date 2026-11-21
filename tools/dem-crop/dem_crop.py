#!/usr/bin/env python3
"""Cut a height-grid tile ("crop", file extension .dem.json) out of terrain data. Standard library only.

  dem_crop.py crop --hgt-dir DIR --lat DEG --lon DEG --cols N --rows N --step-mm N --credit TEXT [--credit TEXT ...]
                   --out FILE.dem.json [--preview FILE.png] [--scale-milli N]
  dem_crop.py convert --heights FILE.json [--key heights] --unit-mm N --step-mm N --credit TEXT [--credit TEXT ...]
                   --out FILE.dem.json [--preview FILE.png] [--scale-milli N]
  dem_crop.py fingerprint FILE.dem.json
  dem_crop.py --self-test

`crop` reads SRTM 1 arc-second .hgt / .hgt.gz files (3601 x 3601 big-endian int16 metres, first row = north edge)
and resamples them to an even metre grid. `convert` turns an existing height grid (JSON, already in metres) into
the same file format. See README.md for the file format, the exact arithmetic and the limits.
Every error is one line on stderr, exit status 1, and no output file is written.
"""
import argparse
import contextlib
import gzip
import hashlib
import io
import json
import math
import os
import re
import struct
import sys
import tempfile
import zlib
from decimal import Decimal
from fractions import Fraction

HGT_SIZE = 3601
HGT_BYTES = HGT_SIZE * HGT_SIZE * 2  # 25934402
NODATA = -32768
LIMIT_MM = 9000000
LAT_ARCSEC_MM = 30870
LON_ARCSEC_MM_AT_EQUATOR = 30922
MAX_LAT_DEG = 60
MIN_POINTS, MAX_POINTS = 2, 513
MAX_CREDITS = 8
DEM_KEYS = ("version", "cols", "rows", "stepMm", "credit", "heightsMm")


class DemError(Exception):
    pass


# ---------------------------------------------------------------- argument and text checks

def check_text(label, s):
    """1 to 255 UTF-8 bytes, no control characters (below 0x20)."""
    try:
        n = len(s.encode("utf-8"))
    except UnicodeEncodeError:
        raise DemError(label + ": not valid UTF-8")
    if not 1 <= n <= 255:
        raise DemError(label + ": must be 1 to 255 UTF-8 bytes")
    for ch in s:
        if ord(ch) < 0x20:
            raise DemError(label + ": control characters are not allowed")
    return s


def parse_uint(label, text, lo, hi):
    if not re.fullmatch(r"[0-9]{1,12}", text):
        raise DemError(label + ": expected a whole number")
    v = int(text)
    if not lo <= v <= hi:
        raise DemError("%s: must be %d to %d" % (label, lo, hi))
    return v


def parse_degrees(label, text):
    if not re.fullmatch(r"-?[0-9]{1,6}(\.[0-9]{1,12})?", text):
        raise DemError(label + ": expected decimal degrees such as -12.5")
    return Fraction(text)


def check_credits(credits):
    if not credits:
        raise DemError("--credit is required (1 to %d)" % MAX_CREDITS)
    if len(credits) > MAX_CREDITS:
        raise DemError("at most %d --credit values" % MAX_CREDITS)
    return [check_text("--credit", c) for c in credits]


def default_scale(step_mm):
    return min(max(step_mm, 100), 100000)


def tile_name_of_output(out):
    name = check_text("--out file name", os.path.basename(out))
    if "\\" in name or name in (".", ".."):
        raise DemError("--out file name: not usable as a tile name")
    return name


def inside_repo(path):
    d = os.path.dirname(os.path.realpath(path))
    while True:
        if os.path.exists(os.path.join(d, ".git")):
            return True
        parent = os.path.dirname(d)
        if parent == d:
            return False
        d = parent


def check_paths(out, preview):
    for label, p in (("--out", out), ("--preview", preview)):
        if p is not None and not os.path.isdir(os.path.dirname(os.path.abspath(p))):
            raise DemError(label + ": directory does not exist")
    if preview is not None:
        if inside_repo(preview):
            raise DemError("--preview must be outside any git repository")
        if os.path.realpath(preview) == os.path.realpath(out):
            raise DemError("--preview and --out are the same file")


# ---------------------------------------------------------------- strict JSON

def _reject_constant(name):
    raise DemError("JSON constant %s is not allowed" % name)


def _reject_duplicate_keys(pairs):
    d = {}
    for k, v in pairs:
        if k in d:
            raise DemError("duplicate key in a JSON object")
        d[k] = v
    return d


def load_json_strict(path, float_hook):
    try:
        with open(path, "rb") as f:
            text = f.read().decode("utf-8")
    except UnicodeDecodeError:
        raise DemError("%s: not UTF-8" % os.path.basename(path))
    try:
        return json.loads(text, parse_float=float_hook, parse_constant=_reject_constant,
                          object_pairs_hook=_reject_duplicate_keys)
    except DemError:
        raise
    except (ValueError, RecursionError) as e:
        raise DemError("%s: bad JSON (%s)" % (os.path.basename(path), e))


def _no_float(text):
    raise DemError("tile file: heights and sizes must be whole numbers")


# ---------------------------------------------------------------- tile file: fingerprint, writer, reader

def fingerprint(cols, rows, step_mm, heights):
    """sha256 of the grid only (credit is not part of it); every line ends with a newline."""
    parts = ["dem 1 %d %d %d\n" % (cols, rows, step_mm)]
    for row in heights:
        parts.append(" ".join(str(v) for v in row) + "\n")
    return hashlib.sha256("".join(parts).encode("utf-8")).hexdigest()


def escape_credit(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


def render_dem(cols, rows, step_mm, credits, heights):
    lines = ["{", '  "version": 1,', '  "cols": %d,' % cols, '  "rows": %d,' % rows,
             '  "stepMm": %d,' % step_mm, '  "credit": [']
    for i, c in enumerate(credits):
        lines.append('    "' + escape_credit(c) + '"' + ("," if i < len(credits) - 1 else ""))
    lines.append("  ],")
    lines.append('  "heightsMm": [')
    for i, row in enumerate(heights):
        lines.append("    [" + ", ".join(str(v) for v in row) + "]" + ("," if i < len(heights) - 1 else ""))
    lines.append("  ]")
    lines.append("}")
    return ("\n".join(lines) + "\n").encode("utf-8")


def is_int(v):
    return type(v) is int


def read_dem_file(path):
    d = load_json_strict(path, _no_float)
    if not isinstance(d, dict) or set(d) != set(DEM_KEYS):
        raise DemError("tile file: needs exactly the keys " + ", ".join(DEM_KEYS))
    if not is_int(d["version"]) or d["version"] != 1:
        raise DemError("tile file: version must be 1")
    for k in ("cols", "rows"):
        if not is_int(d[k]) or not MIN_POINTS <= d[k] <= MAX_POINTS:
            raise DemError("tile file: %s must be %d to %d" % (k, MIN_POINTS, MAX_POINTS))
    if not is_int(d["stepMm"]) or not 1 <= d["stepMm"] <= 100000:
        raise DemError("tile file: stepMm must be 1 to 100000")
    cr = d["credit"]
    if not isinstance(cr, list) or not 1 <= len(cr) <= MAX_CREDITS or not all(isinstance(c, str) for c in cr):
        raise DemError("tile file: credit must be 1 to %d strings" % MAX_CREDITS)
    for c in cr:
        check_text("tile file credit", c)
    h = d["heightsMm"]
    if not isinstance(h, list) or len(h) != d["rows"]:
        raise DemError("tile file: heightsMm must have rows entries")
    for row in h:
        if not isinstance(row, list) or len(row) != d["cols"]:
            raise DemError("tile file: every heightsMm row must have cols entries")
        for v in row:
            if not is_int(v) or abs(v) > LIMIT_MM:
                raise DemError("tile file: heights must be whole millimetres within +-%d" % LIMIT_MM)
    return d


def fingerprint_of_file(path):
    d = read_dem_file(path)
    return fingerprint(d["cols"], d["rows"], d["stepMm"], d["heightsMm"])


# ---------------------------------------------------------------- tile name and sources (SRTM 1 arc-second)

def tile_name(lat_idx, lon_idx):
    return "%s%02d%s%03d" % ("N" if lat_idx >= 0 else "S", abs(lat_idx), "E" if lon_idx >= 0 else "W", abs(lon_idx))


class BytesTile:
    def __init__(self, data):
        self.data = data

    def get(self, row, col):
        """row 0 = north edge, col 0 = west edge."""
        return struct.unpack_from(">h", self.data, (row * HGT_SIZE + col) * 2)[0]


class DirSource:
    def __init__(self, directory):
        self.directory = directory
        self.cache = {}

    def tile(self, name):
        if name not in self.cache:
            self.cache[name] = self._load(name)
        return self.cache[name]

    def _load(self, name):
        for suffix, opener in ((".hgt", open), (".hgt.gz", gzip.open)):
            path = os.path.join(self.directory, name + suffix)
            if not os.path.isfile(path):
                continue
            try:
                with opener(path, "rb") as f:
                    data = f.read(HGT_BYTES + 1)
            except (OSError, EOFError, zlib.error) as e:
                raise DemError("%s%s: cannot read (%s)" % (name, suffix, type(e).__name__))
            if len(data) != HGT_BYTES:
                raise DemError("%s%s: unexpected size; only 1 arc-second files (3601 x 3601) are supported" % (name, suffix))
            return BytesTile(data)
        return None


def floor_div(a, b):
    return a // b


def read_point(source, A, B):
    """Height in metres at the integer arc-second point (A, B); A east, B north, both counted from 0, 0."""
    lon_idx = A // 3600
    col = A - 3600 * lon_idx
    lat_idx = B // 3600
    rs = B - 3600 * lat_idx
    lons = [(lon_idx, col)]
    if col == 0:
        lons.append((lon_idx - 1, 3600))
    lats = [(lat_idx, rs)]
    if rs == 0:
        lats.append((lat_idx - 1, 3600))
    first = None
    for li, cc in lons:
        for ti, rr in lats:
            name = tile_name(ti, li)
            if first is None:
                first = name
            tile = source.tile(name)
            if tile is not None:
                v = tile.get(3600 - rr, cc)
                if v == NODATA:
                    raise DemError("no data at arc-second point (%d, %d) in %s" % (A, B, name))
                return v
    raise DemError("missing tile file %s.hgt (or .hgt.gz)" % first)


def axis_positions(origin_deg, count, step_mm, arcsec_mm):
    base = origin_deg * 3600
    out = []
    for i in range(count):
        x = base + Fraction(i * step_mm, arcsec_mm)
        a = math.floor(x)
        f = x - a
        out.append((a, f.numerator, f.denominator))
    return out


def crop_heights(source, lat, lon, cols, rows, step_mm):
    """Rows from south to north, columns from west to east, millimetres."""
    east_mm = math.floor(LON_ARCSEC_MM_AT_EQUATOR * math.cos(math.radians(float(lat))) + 0.5)
    north = lat + Fraction((rows - 1) * step_mm, LAT_ARCSEC_MM * 3600)
    east = lon + Fraction((cols - 1) * step_mm, east_mm * 3600)
    if lat < -MAX_LAT_DEG or north > MAX_LAT_DEG:
        raise DemError("the whole crop must lie within %d degrees north and south" % MAX_LAT_DEG)
    if lon < -180 or east > 180:
        raise DemError("the whole crop must lie within longitude -180 to 180")
    xs = axis_positions(lon, cols, step_mm, east_mm)
    ys = axis_positions(lat, rows, step_mm, LAT_ARCSEC_MM)
    result = []
    for r in range(rows):
        B, pb, qb = ys[r]
        row = []
        for c in range(cols):
            A, pa, qa = xs[c]
            h00 = read_point(source, A, B)
            h10 = read_point(source, A + 1, B) if pa else 0
            h01 = read_point(source, A, B + 1) if pb else 0
            h11 = read_point(source, A + 1, B + 1) if (pa and pb) else 0
            num = (qa - pa) * (qb - pb) * h00 + pa * (qb - pb) * h10 + (qa - pa) * pb * h01 + pa * pb * h11
            mm = floor_div(num * 1000, qa * qb)
            if abs(mm) > LIMIT_MM:
                raise DemError("height beyond +-%d mm at crop row %d column %d" % (LIMIT_MM, r, c))
            row.append(mm)
        result.append(row)
    return result


# ---------------------------------------------------------------- convert

def cell_to_mm(v, unit_mm):
    if type(v) is bool or not (type(v) is int or type(v) is Decimal):
        raise DemError("heights must be numbers")
    if type(v) is Decimal:
        t = v.as_tuple()
        if len(t.digits) > 60 or t.exponent < -40 or t.exponent > 12:
            raise DemError("height number is too long")
    mm = math.floor(Fraction(v) * unit_mm)
    if abs(mm) > LIMIT_MM:
        raise DemError("height beyond +-%d mm" % LIMIT_MM)
    return mm


def convert_heights(path, key, unit_mm):
    data = load_json_strict(path, Decimal)
    if isinstance(data, dict):
        if key not in data:
            raise DemError("key %s not found in the heights file" % key)
        grid = data[key]
    else:
        grid = data
    if not isinstance(grid, list) or not MIN_POINTS <= len(grid) <= MAX_POINTS:
        raise DemError("heights must be a list of %d to %d rows" % (MIN_POINTS, MAX_POINTS))
    if not all(isinstance(row, list) for row in grid):
        raise DemError("every heights row must be a list")
    cols = len(grid[0])
    if not MIN_POINTS <= cols <= MAX_POINTS or any(len(row) != cols for row in grid):
        raise DemError("rows must all have the same length (%d to %d)" % (MIN_POINTS, MAX_POINTS))
    return [[cell_to_mm(v, unit_mm) for v in row] for row in grid]


# ---------------------------------------------------------------- report, preview, writing

def slope_counts(heights, step_mm):
    limits = ((1, 4), (1, 2), (3, 4), (1, 1), (1414, 1000))
    counts = [0] * 6

    def add(delta):
        d = abs(delta)
        for i, (n, den) in enumerate(limits):
            if d * den <= n * step_mm:
                counts[i] += 1
                return
        counts[5] += 1

    for row in heights:
        for a, b in zip(row, row[1:]):
            add(b - a)
    for r0, r1 in zip(heights, heights[1:]):
        for a, b in zip(r0, r1):
            add(b - a)
    return counts


def preview_png(heights):
    lo = min(min(r) for r in heights)
    hi = max(max(r) for r in heights)
    rows = []
    for row in reversed(heights):
        if hi == lo:
            rows.append(bytes([128]) * len(row))
        else:
            rows.append(bytes(255 * (h - lo) // (hi - lo) for h in row))
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", len(heights[0]), len(heights), 8, 0, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def write_atomic(path, data):
    fd, tmp = tempfile.mkstemp(dir=os.path.dirname(os.path.abspath(path)), prefix=".dem-tmp-")
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(data)
        os.chmod(tmp, 0o644)
        os.replace(tmp, path)
    except BaseException:
        with contextlib.suppress(OSError):
            os.unlink(tmp)
        raise


def emit(heights, step_mm, credits, out, preview, scale_milli):
    rows, cols = len(heights), len(heights[0])
    tile = tile_name_of_output(out)
    fp = fingerprint(cols, rows, step_mm, heights)
    lo = min(min(r) for r in heights)
    hi = max(max(r) for r in heights)
    scale = default_scale(step_mm) if scale_milli is None else scale_milli
    counts = slope_counts(heights, step_mm)
    total = sum(counts)
    names = ("<=1/4", "<=1/2", "<=3/4", "<=1", "<=1.414", "steeper")
    base = ('  "base": { "tile": "%s", "sha256": "%s", "x0Mm": 0, "z0Mm": 0, "scaleMilli": %d, '
            '"exaggerationMilli": 1000, "datumMm": %d },' % (escape_credit(tile), fp, scale, lo))
    report = {
        "fingerprint": fp, "min": lo, "max": hi, "slopes": counts, "scale": scale, "base": base,
        "max_cols": (cols - 1) * step_mm // scale, "max_rows": (rows - 1) * step_mm // scale,
    }
    data = render_dem(cols, rows, step_mm, credits, heights)
    png = preview_png(heights) if preview is not None else None
    if png is not None:
        write_atomic(preview, png)
    write_atomic(out, data)
    print("fingerprint  " + fp)
    print("size         %d x %d points, step %d mm" % (cols, rows, step_mm))
    print("height       min %d mm, max %d mm" % (lo, hi))
    print("slope        " + "  ".join("%s %.2f%%" % (n, 100.0 * c / total) for n, c in zip(names, counts)))
    print("largest map  %d x %d m at scaleMilli %d (east-west x north-south)" % (report["max_cols"], report["max_rows"], scale))
    print(base)
    print('A generated file with "base" must also set "baseMm".')
    return report


# ---------------------------------------------------------------- command line

class Parser(argparse.ArgumentParser):
    def error(self, message):
        raise DemError(message.replace("\n", " "))


def build_parser():
    p = Parser(prog="dem_crop.py", allow_abbrev=False, description="Cut and convert terrain height tiles.")
    p.add_argument("--self-test", action="store_true")
    sub = p.add_subparsers(dest="cmd", parser_class=Parser)

    def common(sp):
        sp.add_argument("--step-mm", required=True)
        sp.add_argument("--credit", action="append", default=[])
        sp.add_argument("--out", required=True)
        sp.add_argument("--preview")
        sp.add_argument("--scale-milli")

    c = sub.add_parser("crop", allow_abbrev=False)
    c.add_argument("--hgt-dir", required=True)
    c.add_argument("--lat", required=True)
    c.add_argument("--lon", required=True)
    c.add_argument("--cols", required=True)
    c.add_argument("--rows", required=True)
    common(c)
    v = sub.add_parser("convert", allow_abbrev=False)
    v.add_argument("--heights", required=True)
    v.add_argument("--key", default="heights")
    v.add_argument("--unit-mm", required=True)
    common(v)
    f = sub.add_parser("fingerprint", allow_abbrev=False)
    f.add_argument("file")
    return p


def run(argv, source_factory):
    args = build_parser().parse_args(argv)
    if args.self_test:
        return self_test()
    if args.cmd is None:
        raise DemError("give a command: crop, convert, fingerprint or --self-test")
    if args.cmd == "fingerprint":
        print(fingerprint_of_file(args.file))
        return 0
    step = parse_uint("--step-mm", args.step_mm, 1, 100000)
    credits = check_credits(args.credit)
    scale = None if args.scale_milli is None else parse_uint("--scale-milli", args.scale_milli, 100, 100000)
    tile_name_of_output(args.out)
    check_paths(args.out, args.preview)
    if args.cmd == "crop":
        lat = parse_degrees("--lat", args.lat)
        lon = parse_degrees("--lon", args.lon)
        cols = parse_uint("--cols", args.cols, MIN_POINTS, MAX_POINTS)
        rows = parse_uint("--rows", args.rows, MIN_POINTS, MAX_POINTS)
        if source_factory is None:
            if not os.path.isdir(args.hgt_dir):
                raise DemError("--hgt-dir is not a directory")
            source = DirSource(args.hgt_dir)
        else:
            source = source_factory(args.hgt_dir)
        heights = crop_heights(source, lat, lon, cols, rows, step)
    else:
        unit = parse_uint("--unit-mm", args.unit_mm, 1, 1000000)
        heights = convert_heights(args.heights, args.key, unit)
    emit(heights, step, credits, args.out, args.preview, scale)
    return 0


def main(argv=None, source_factory=None):
    try:
        return run(sys.argv[1:] if argv is None else argv, source_factory)
    except DemError as e:
        print("dem_crop: %s" % e, file=sys.stderr)
        return 1
    except OSError as e:
        print("dem_crop: I/O error: %s" % e, file=sys.stderr)
        return 1


# ---------------------------------------------------------------- self-test (synthetic data only, no downloads)

SYNTH_SPECIAL = {(5, 9): 1000, (2, 3): -100}


def synth_height(A, B, extra):
    """Synthetic metres at integer arc-second point (A, B)."""
    if (A, B) in extra:
        return extra[(A, B)]
    if (A, B) in SYNTH_SPECIAL:
        return SYNTH_SPECIAL[(A, B)]
    return 2 * (A % 50) + 3 * (B % 50)


class SynthTile:
    def __init__(self, lat_idx, lon_idx, extra):
        self.lat_idx, self.lon_idx, self.extra = lat_idx, lon_idx, extra

    def get(self, row, col):
        return synth_height(self.lon_idx * 3600 + col, self.lat_idx * 3600 + (3600 - row), self.extra)


class SynthSource:
    def __init__(self, names, extra=None):
        self.names = set(names)
        self.extra = dict(extra or {})

    def tile(self, name):
        if name not in self.names:
            return None
        m = re.fullmatch(r"([NS])([0-9]{2})([EW])([0-9]{3})", name)
        lat = int(m.group(2)) * (1 if m.group(1) == "N" else -1)
        lon = int(m.group(4)) * (1 if m.group(3) == "E" else -1)
        return SynthTile(lat, lon, self.extra)


def synth_hgt_bytes(lat_idx, lon_idx, extra=None):
    """A full-size .hgt file of the synthetic terrain, built from 50 repeating row patterns."""
    extra = extra or {}
    patterns = [struct.pack(">3601h", *[2 * (col % 50) + 3 * b for col in range(HGT_SIZE)]) for b in range(50)]
    data = bytearray()
    for row in range(HGT_SIZE):
        data += patterns[(3600 - row) % 50]
    for (A, B), v in list(SYNTH_SPECIAL.items()) + list(extra.items()):
        if A // 3600 == lon_idx and B // 3600 == lat_idx:
            struct.pack_into(">h", data, ((3600 - (B - 3600 * lat_idx)) * HGT_SIZE + (A - 3600 * lon_idx)) * 2, v)
    return bytes(data)


def reference_crop(source_extra, lat, lon, cols, rows, step_mm):
    """Plain exact-fraction version of the crop arithmetic, for cross-checking."""
    E = math.floor(30922 * math.cos(math.radians(float(lat))) + 0.5)
    out = []
    for r in range(rows):
        y = lat * 3600 + Fraction(r * step_mm, 30870)
        B = math.floor(y)
        fb = y - B
        row = []
        for c in range(cols):
            x = lon * 3600 + Fraction(c * step_mm, E)
            A = math.floor(x)
            fa = x - A

            def h(a, b):
                return synth_height(a, b, source_extra)
            v = ((1 - fa) * (1 - fb) * h(A, B) + fa * (1 - fb) * h(A + 1, B)
                 + (1 - fa) * fb * h(A, B + 1) + fa * fb * h(A + 1, B + 1))
            row.append(math.floor(v * 1000))
        out.append(row)
    return out


def decode_png_gray(data):
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, size = 8, b"", None
    while pos < len(data):
        n = struct.unpack(">I", data[pos:pos + 4])[0]
        kind, body = data[pos + 4:pos + 8], data[pos + 8:pos + 8 + n]
        assert struct.unpack(">I", data[pos + 8 + n:pos + 12 + n])[0] == zlib.crc32(kind + body) & 0xFFFFFFFF
        if kind == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            assert depth == 8 and ctype == 0
            size = (w, h)
        elif kind == b"IDAT":
            idat += body
        pos += 12 + n
    raw = zlib.decompress(idat)
    w, h = size
    rows = []
    for i in range(h):
        line = raw[i * (w + 1):(i + 1) * (w + 1)]
        assert line[0] == 0
        rows.append(list(line[1:]))
    return rows


class Checker:
    def __init__(self):
        self.failed = 0

    def case(self, name, fn):
        try:
            fn()
        except BaseException as e:  # a crash is a failed case, never a crash of the run
            if isinstance(e, KeyboardInterrupt):
                raise
            self.failed += 1
            print("FAIL %s (%s: %s)" % (name, type(e).__name__, str(e)[:200]))
        else:
            print("ok   " + name)


def self_test():
    ck = Checker()
    both = ["N00E000", "N00W001"]
    only_east, only_west = ["N00E000"], ["N00W001"]
    eq = lambda got, want: _assert(got == want, "got %r want %r" % (got, want))
    default_factory = lambda d: SynthSource(both)
    sy = SynthSource(both)
    D = Fraction

    def crop(src, lat, lon, cols, rows, step):
        return crop_heights(src, D(lat), D(lon), cols, rows, step)

    def refuses(argv, factory=default_factory):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            code = main(argv, factory)
        return code != 0

    with tempfile.TemporaryDirectory() as tmp:
        def p(name):
            return os.path.join(tmp, name)

        # --- T14 convert, first because it needs nothing else
        grid = '{"id": "x", "heights": [[0, 1.5, -0.001], [38.59, 2, 3]]}'
        want_convert = [[0, 1500, -1], [38590, 2000, 3000]]

        def conv(text, *extra, name="c.json"):
            with open(p(name), "w", encoding="utf-8") as f:
                f.write(text)
            return main(["convert", "--heights", p(name), "--unit-mm", "1000", "--step-mm", "1000", "--credit", "synthetic",
                         "--out", p("c.dem.json")] + list(extra))

        def t14_basic():
            with contextlib.redirect_stdout(io.StringIO()) as buf:
                eq(conv(grid), 0)
            d = read_dem_file(p("c.dem.json"))
            eq(d["heightsMm"], want_convert)
            eq(fingerprint_of_file(p("c.dem.json")), "45843f0e3b14047f2a5a5ea6f90128980c772ee838d3a565acbb713199af6641")
            out = buf.getvalue()
            _assert('"scaleMilli": 1000' in out and '"datumMm": -1 }' in out, "convert default base line")
            _assert("largest map  2 x 1 m" in out, "convert default largest map")
        ck.case("T14 convert example and default scale", t14_basic)

        def t14_forms():
            with contextlib.redirect_stdout(io.StringIO()):
                eq(conv("[[0, 1.5, -0.001], [38.59, 2, 3]]"), 0)
                eq(read_dem_file(p("c.dem.json"))["heightsMm"], want_convert)
                eq(conv('{"heights": [[0, 15e-1, -0.001], [38.59, 2, 3]]}'), 0)
                eq(read_dem_file(p("c.dem.json"))["heightsMm"], want_convert)
                eq(conv('{"heights": [[-0.0004, 0.0004], [0, 0]]}'), 0)
                eq(read_dem_file(p("c.dem.json"))["heightsMm"], [[-1, 0], [0, 0]])
                eq(conv('{"grid": [[0, 1], [2, 3]]}', "--key", "grid"), 0)
        ck.case("T14 convert array input, exponent, rounding down, --key", t14_forms)

        def t14_rejects():
            bad = {
                "wrong key": ('{"other": [[0, 1], [2, 3]]}', []),
                "uneven rows": ('{"heights": [[0, 1], [2]]}', []),
                "string cell": ('{"heights": [[0, "1"], [2, 3]]}', []),
                "NaN": ('{"heights": [[0, NaN], [2, 3]]}', []),
                "Infinity": ('{"heights": [[0, Infinity], [2, 3]]}', []),
                "-Infinity": ('{"heights": [[0, -Infinity], [2, 3]]}', []),
                "true": ('{"heights": [[0, true], [2, 3]]}', []),
                "false": ('{"heights": [[0, false], [2, 3]]}', []),
                "null": ('{"heights": [[0, null], [2, 3]]}', []),
                "duplicate key": ('{"heights": [[0, 1], [2, 3]], "heights": [[0, 1], [2, 3]]}', []),
                "one row": ('{"heights": [[0, 1]]}', []),
                "too high": ('{"heights": [[0, 9000.001], [2, 3]]}', []),
                "bad key": ('{"heights": [[0, 1], [2, 3]]}', ["--key", "nothing"]),
                "tiny exponent": ('{"heights": [[0, 1e-50], [2, 3]]}', []),
            }
            for name, (text, extra) in bad.items():
                if os.path.exists(p("c.dem.json")):
                    os.unlink(p("c.dem.json"))
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    code = conv(text, *extra)
                _assert(code != 0, "accepted: " + name)
                _assert(not os.path.exists(p("c.dem.json")), "left a file: " + name)
        ck.case("T14 convert rejects", t14_rejects)

        # --- T1 .. T8 crop arithmetic (in-memory synthetic tiles)
        def cr(name, src, args, want):
            ck.case(name, lambda: (eq(crop(src, *args), want), eq(reference_crop({}, D(args[0]), D(args[1]), *args[2:]), want)))

        cr("T1 aligned", sy, ("0", "0", 3, 2, 30922), [[0, 2000, 4000], [3005, 5005, 7005]])
        cr("T2 half steps", sy, ("0", "0", 3, 2, 15435), [[0, 998, 1996], [1500, 2498, 3496]])
        cr("T3 four weights", sy, ("0.0025", "0.00125", 2, 2, 30922), [[517500, 519500], [39005, 41005]])
        cr("T4 across the tile edge, west longitude", sy, ("0", "-0.00125", 6, 1, 30922), [[91000, 93000, 95000, 97000, 49000, 1000]])
        cr("T5 south latitude 60", SynthSource(["S60W001"]), ("-60", "-1", 3, 2, 15461), [[0, 2000, 4000], [1502, 3502, 5502]])
        ck.case("T6 latitude 45", lambda: (eq(crop(SynthSource(["N45E000"]), "45", "0", 3, 1, 21865), [[0, 2000, 4000]]),
                                           eq(reference_crop({}, D(45), D(0), 3, 1, 21865), [[0, 2000, 4000]])))
        cr("T7 negative numbers round down", sy, ("0.000625", "0.0005", 2, 2, 15461), [[-12250, -8425], [-56024, -46540]])

        def t4_missing():
            for names, cols, want in ((only_east, 6, "N00W001"), ):
                try:
                    crop(SynthSource(names), "0", "-0.00125", cols, 1, 30922)
                except DemError as e:
                    _assert(want in str(e), "message names the missing tile: " + str(e))
                else:
                    raise AssertionError("did not refuse")
            eq(crop(SynthSource(only_west), "0", "-0.00125", 5, 1, 30922), [[91000, 93000, 95000, 97000, 49000]])
        ck.case("T4 missing tile file, and the seam point read from the west tile", t4_missing)

        def seam_north():
            # the second row sits at B = 3600 exactly (one degree north), where the north tile is absent
            lat = D(1) - D(1, 3600)
            got = crop(SynthSource(["N00E000"]), str(lat), "0", 2, 2, 30870)
            eq(got, reference_crop({}, lat, D(0), 2, 2, 30870))
        ck.case("seam in latitude (north tile absent)", seam_north)
        ck.case("T6b south-east naming S01E006", lambda: eq(
            crop(SynthSource(["S01E006"]), "-0.0025", "6.001", 3, 3, 20000), reference_crop({}, D("-0.0025"), D("6.001"), 3, 3, 20000)))

        def t8():
            src = SynthSource(both, {(1, 0): NODATA})
            try:
                crop(src, "0", "0", 3, 2, 30922)
            except DemError as e:
                _assert("(1, 0)" in str(e), str(e))
            else:
                raise AssertionError("did not refuse")
            eq(crop(src, "0", "0", 2, 1, 61844), [[0, 4000]])
        ck.case("T8 no data", t8)

        # --- T10 output bytes and fingerprint
        def write_t3(credits, name="t3.dem.json"):
            args = ["crop", "--hgt-dir", tmp, "--lat", "0.0025", "--lon", "0.00125", "--cols", "2", "--rows", "2",
                    "--step-mm", "30922", "--out", p(name)]
            for c in credits:
                args += ["--credit=" + c]
            with contextlib.redirect_stdout(io.StringIO()):
                eq(main(args, default_factory), 0)
            with open(p(name), "rb") as f:
                return f.read()

        def t10():
            b = write_t3(["synthetic"])
            eq(len(b), 162)
            eq(hashlib.sha256(b).hexdigest(), "8567653b0e89a74440c6c656974f6ec28126fc1b6ff25ff7d8870465eb84b249")
            eq(b, write_t3(["synthetic"], "again.dem.json"))
            b2 = write_t3(["synthetic © Ö – one", 'quote " and backslash \\'])
            eq(len(b2), 209)
            eq(hashlib.sha256(b2).hexdigest(), "deb0193e6321e4ea15b26ef8ce52ee0def601a414d0a7b011bf6e29e2014039b")
            _assert(b'"quote \\" and backslash \\\\"' in b2, "escaped credit line")
            T3 = "26d651c3f55f1e188c395649fb7aa4087017d1ddcd2e33e8aade35dfe4d40f62"
            eq(fingerprint_of_file(p("t3.dem.json")), T3)
            eq(fingerprint_of_file(p("again.dem.json")), T3)
            with open(p("ca.dem.json"), "w", encoding="utf-8") as f:
                f.write('{\n  "version": 1,\n  "cols": 3,\n  "rows": 3,\n  "stepMm": 30000,\n  "credit": ["synthetic test tile"],\n'
                        '  "heightsMm": [[0, 30000, 45000], [9000, 24000, 60000], [30000, 45000, 54000]]\n}\n')
            eq(fingerprint_of_file(p("ca.dem.json")), "059f05dde8ad90d037bf60e648ffd8db1644a1fc53d981baf96bb376100ce113")
            with open(p("cb.dem.json"), "w", encoding="utf-8") as f:
                f.write('{"version":1,"cols":3,"rows":3,"stepMm":30000,"credit":["x"],'
                        '"heightsMm":[[0,30000,45000],[9000,24001,60000],[30000,45000,54000]]}')
            eq(fingerprint_of_file(p("cb.dem.json")), "1a98bde96909f8acf771f23d6454c95d4efa31b635086f3b73b793864fb71a29")
            with contextlib.redirect_stdout(io.StringIO()) as buf:
                eq(main(["fingerprint", p("ca.dem.json")]), 0)
            eq(buf.getvalue().strip(), "059f05dde8ad90d037bf60e648ffd8db1644a1fc53d981baf96bb376100ce113")
        ck.case("T10 output bytes, credits, fingerprint of both sides", t10)

        def t10_reader_rejects():
            good = '{"version":1,"cols":2,"rows":2,"stepMm":1,"credit":["x"],"heightsMm":[[0,1],[2,3]]}'
            variants = {
                "float height": good.replace("[0,1]", "[0,1.5]"),
                "float size": good.replace('"cols":2', '"cols":2.0'),
                "extra key": good.replace('"version":1,', '"version":1,"extra":1,'),
                "missing key": good.replace('"version":1,', ""),
                "twice": good.replace('"version":1,', '"version":1,"version":1,'),
                "version 2": good.replace('"version":1', '"version":2'),
                "true": good.replace('"cols":2', '"cols":true'),
                "too high": good.replace("[2,3]", "[2,9000001]"),
                "wrong rows": good.replace('"rows":2', '"rows":3'),
            }
            for name, text in variants.items():
                with open(p("v.dem.json"), "w", encoding="utf-8") as f:
                    f.write(text)
                try:
                    fingerprint_of_file(p("v.dem.json"))
                except DemError:
                    continue
                raise AssertionError("accepted " + name)
            with open(p("v.dem.json"), "w", encoding="utf-8") as f:
                f.write(good.replace("[0,1]", "[-0,1]"))
            eq(fingerprint_of_file(p("v.dem.json")), fingerprint(2, 2, 1, [[0, 1], [2, 3]]))
        ck.case("fingerprint reader rejects what the engine would reject", t10_reader_rejects)

        # --- T11 preview, T12 printed lines
        def t11():
            with contextlib.redirect_stdout(io.StringIO()):
                eq(main(["crop", "--hgt-dir", tmp, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                         "--credit", "synthetic", "--out", p("t1.dem.json"), "--preview", p("t1.png"), "--scale-milli", "10000"],
                        default_factory), 0)
            with open(p("t1.png"), "rb") as f:
                eq(decode_png_gray(f.read()), [[109, 182, 255], [0, 72, 145]])
            eq(decode_png_gray(preview_png([[5, 5], [5, 5]])), [[128, 128], [128, 128]])
        ck.case("T11 preview pixels, north on top, flat is 128", t11)

        def t12():
            base = ('  "base": { "tile": "t1.dem.json", "sha256": "71728263892ffbfaa7eb1bfa79737397e7be081907f938f4aeb556e5fab5a6fe", '
                    '"x0Mm": 0, "z0Mm": 0, "scaleMilli": 10000, "exaggerationMilli": 1000, "datumMm": 0 },')

            def go(extra):
                with contextlib.redirect_stdout(io.StringIO()) as buf:
                    eq(main(["crop", "--hgt-dir", tmp, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                             "--credit", "synthetic", "--out", p("t1.dem.json")] + extra, default_factory), 0)
                return buf.getvalue().splitlines()
            lines = go(["--scale-milli", "10000"])
            _assert(base in lines, "base line")
            _assert("largest map  6 x 3 m at scaleMilli 10000 (east-west x north-south)" in lines, "largest map: " + repr(lines))
            _assert("height       min 0 mm, max 7005 mm" in lines, "min max")
            lines = go([])
            _assert(base.replace('"scaleMilli": 10000', '"scaleMilli": 30922') in lines, "default base line")
            _assert("largest map  2 x 1 m at scaleMilli 30922 (east-west x north-south)" in lines, "default largest map")
            _assert(lines[-1] == 'A generated file with "base" must also set "baseMm".', "reminder line")
        ck.case("T12 printed fingerprint, extremes, largest map and base line", t12)

        def slopes():
            # step 100: a difference of 25, 50, 75, 100, 141 and 142 mm falls in the first to the sixth bucket
            eq(slope_counts([[0, 25, 75, 150]], 100), [1, 1, 1, 0, 0, 0])
            eq(slope_counts([[0, 100, 241, 383]], 100), [0, 0, 0, 1, 1, 1])
            eq(slope_counts([[0, 0], [30, 0]], 100), [2, 2, 0, 0, 0, 0])
        ck.case("slope buckets at their boundaries", slopes)

        # --- T9 refusals
        def t9():
            base_args = ["crop", "--hgt-dir", tmp, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                         "--credit", "s", "--out", p("r.dem.json")]

            def with_(**kw):
                a = list(base_args)
                for k, v in kw.items():
                    flag = "--" + k.replace("_", "-")
                    if v is None:
                        i = a.index(flag)
                        del a[i:i + 2]
                    else:
                        a[a.index(flag) + 1] = v
                return a
            cases = {
                "cols 1": with_(cols="1"), "cols 514": with_(cols="514"), "rows 1": with_(rows="1"), "rows 514": with_(rows="514"),
                "step 0": with_(step_mm="0"), "step 100001": with_(step_mm="100001"),
                "no credit": with_(credit=None),
                "nine credits": base_args + ["--credit", "t"] * 8,
                "lat 60.5": with_(lat="60.5"), "lat -60.5": with_(lat="-60.5"), "lat 1/3": with_(lat="1/3"),
                "lat 1e-3": with_(lat="1e-3"), "lat space": with_(lat=" 1"), "lon space": with_(lon="1 "),
                "lat plus": with_(lat="+1"),
                "credit newline": with_(credit="a\nb"), "credit empty": with_(credit=""), "credit 256 bytes": with_(credit="x" * 256),
                "scale 99": base_args + ["--scale-milli", "99"], "scale 100001": base_args + ["--scale-milli", "100001"],
                "cols text": with_(cols="3.0"), "cols underscore": with_(cols="1_0"),
                "top above 60": with_(lat="59.9999", rows="400"),
                "out name 256 bytes": with_(out=p("x" * 256)),
                "unknown flag": base_args + ["--bogus", "1"],
            }
            for name, argv in cases.items():
                _assert(refuses(argv), "accepted: " + name)
                _assert(not os.path.exists(p("r.dem.json")), "left a file: " + name)
            # the 60 degree limit with the needed tiles present, so that only the limit can refuse; 1e1 would be latitude 10
            far = lambda d: SynthSource(["N60E000", "S61E000", "N59E000", "N10E000", "N00E000"])
            _assert(refuses(with_(lat="60.5"), far), "lat 60.5 with tile")
            _assert(refuses(with_(lat="-60.5"), far), "lat -60.5 with tile")
            _assert(refuses(with_(lat="59.9999", rows="400"), far), "top above 60 with tile")
            _assert(refuses(with_(lat="1e1"), far), "lat 1e1")
            _assert(refuses(with_(lat="1e-3"), far), "lat 1e-3")
            # a step below 100 gives the lowest default scale
            with contextlib.redirect_stdout(io.StringIO()) as buf:
                eq(main(with_(step_mm="50"), default_factory), 0)
            _assert('"scaleMilli": 100,' in buf.getvalue(), "default scale clamped up to 100")
            os.unlink(p("r.dem.json"))
            # 60.0 exactly is fine, and 9001 m is not
            eq(crop(SynthSource(["N60E000"]), "60", "0", 2, 1, 30922), reference_crop({}, D(60), D(0), 2, 1, 30922))
            eq(refuses(with_(lat="0"), lambda d: SynthSource(both, {(1, 0): 9001})), True)
            eq(refuses(with_(lat="0"), lambda d: SynthSource(both, {(1, 0): -9001})), True)
            _assert(not os.path.exists(p("r.dem.json")), "left a file after 9001 m")
            # preview inside a repository, and the same file as --out
            os.makedirs(p("fakerepo/sub"))
            os.makedirs(p("fakerepo/.git"))
            for prev in (p("fakerepo/sub/x.png"), p("fakerepo/x.png")):
                _assert(refuses(base_args + ["--preview", prev]), "preview in repo")
                _assert(not os.path.exists(prev) and not os.path.exists(p("r.dem.json")), "left a file: preview in repo")
            os.symlink(p("fakerepo/sub"), p("link"))
            _assert(refuses(base_args + ["--preview", p("link/y.png")]), "preview in repo through a symlink")
            _assert(refuses(base_args + ["--preview", p("r.dem.json")]), "preview same as out")
            _assert(refuses(base_args + ["--preview", p("nodir/y.png")]), "preview directory missing")
            _assert(refuses(["crop", "--hgt-dir", p("none")] + base_args[3:], None), "missing hgt dir")
            # existing output must survive a refused run
            with open(p("r.dem.json"), "w") as f:
                f.write("keep")
            _assert(refuses(with_(cols="1")), "refused")
            with open(p("r.dem.json")) as f:
                eq(f.read(), "keep")
        ck.case("T9 refusals leave no file", t9)

        # --- real files: plain, gzip, wrong size
        def real_files():
            d = p("hgt")
            os.mkdir(d)
            with open(os.path.join(d, "N00E000.hgt"), "wb") as f:
                f.write(synth_hgt_bytes(0, 0))
            with gzip.open(os.path.join(d, "N00W001.hgt.gz"), "wb", compresslevel=1) as f:
                f.write(synth_hgt_bytes(0, -1))
            with contextlib.redirect_stdout(io.StringIO()):
                eq(main(["crop", "--hgt-dir", d, "--lat", "0", "--lon", "-0.00125", "--cols", "6", "--rows", "2", "--step-mm", "30922",
                         "--credit", "synthetic", "--out", p("real4.dem.json")]), 0)
                eq(main(["crop", "--hgt-dir", d, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                         "--credit", "synthetic", "--out", p("real1.dem.json")]), 0)
            eq(read_dem_file(p("real4.dem.json"))["heightsMm"][0], [91000, 93000, 95000, 97000, 49000, 1000])
            eq(read_dem_file(p("real1.dem.json"))["heightsMm"], [[0, 2000, 4000], [3005, 5005, 7005]])
            os.remove(os.path.join(d, "N00E000.hgt"))
            with open(os.path.join(d, "N00E000.hgt"), "wb") as f:
                f.write(bytes(1201 * 1201 * 2))
            _assert(refuses(["crop", "--hgt-dir", d, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                             "--credit", "s", "--out", p("r3.dem.json")], None), "3 arc-second file")
            _assert(not os.path.exists(p("r3.dem.json")), "left a file: 3 arc-second")
            os.remove(os.path.join(d, "N00E000.hgt"))
            with open(os.path.join(d, "N00E000.hgt.gz"), "wb") as f:
                f.write(b"not gzip")
            _assert(refuses(["crop", "--hgt-dir", d, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                             "--credit", "s", "--out", p("r4.dem.json")], None), "broken gzip")
            os.remove(os.path.join(d, "N00E000.hgt.gz"))
            _assert(refuses(["crop", "--hgt-dir", d, "--lat", "0", "--lon", "0", "--cols", "3", "--rows", "2", "--step-mm", "30922",
                             "--credit", "s", "--out", p("r5.dem.json")], None), "missing file")
        ck.case("real .hgt and .hgt.gz files, wrong size, broken file, missing file", real_files)

    if ck.failed:
        print("self-test FAILED (%d)" % ck.failed)
        return 1
    print("self-test passed")
    return 0


def _assert(cond, message="assertion failed"):
    if not cond:
        raise AssertionError(message)


if __name__ == "__main__":
    sys.exit(main())
