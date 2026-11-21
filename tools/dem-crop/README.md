# dem-crop

Cuts a height-grid tile (`*.dem.json`) out of terrain data. Python 3, standard library only, run with `python3 -I`.
The tile is the file the map generator's `base` layer reads (format "C-a" below). The tool writes tiles; it does not
read anything the engine writes, and it changes no engine code.

```sh
python3 -I tools/dem-crop/dem_crop.py crop --hgt-dir DIR --lat 0 --lon 0 --cols 513 --rows 513 --step-mm 30922 \
    --credit "synthetic" --out t.dem.json --preview t.png --scale-milli 10000
python3 -I tools/dem-crop/dem_crop.py convert --heights grid.json --unit-mm 1000 --step-mm 1000 \
    --credit "synthetic" --out t.dem.json
python3 -I tools/dem-crop/dem_crop.py fingerprint t.dem.json
python3 -I tools/dem-crop/dem_crop.py --self-test
```

Every error is one line on stderr with exit status 1, and no output file is written (a file already there stays as it was).
A `--credit` value that starts with `-` must be written `--credit=...`.

## crop

Input: SRTM 1 arc-second tiles, `.hgt` or `.hgt.gz`, named by their south-west corner (`N00E006`, `S01W001`; south and
west use `S` and `W`), all in `--hgt-dir`. Only that one kind of file is read: 3601 x 3601 big-endian signed 16-bit
metres, 25934402 bytes, first row = north edge, neighbouring tiles share their edge row and column, -32768 = no data.
Other sizes (3 arc-second), GeoTIFF and other data sets are refused. SRTM covers 60 degrees north to 56 degrees south;
the tool refuses any crop that is not wholly within 60 degrees north and south.

- `--lat`, `--lon`: south-west corner in decimal degrees (`-?digits[.digits]`, nothing else), parsed exactly.
- `--cols`, `--rows`: 2 to 513. `--step-mm`: 1 to 100000. `--credit`: 1 to 8, each 1 to 255 UTF-8 bytes, no control characters.
- `--scale-milli`: 100 to 100000. It only changes the `base` line that is printed. Default: `--step-mm`, limited to 100..100000.
- Row 0 of the result is the southernmost row, column 0 the westernmost.

Arithmetic (exact, no rounding until the end):

- One arc-second is 30870 mm north-south and `E = floor(30922 * cos(latitude of the corner) + 1/2)` mm east-west. This is an
  approximation (north-south error under 0.5%), good enough for game maps. `E` is the only floating-point number; different
  math libraries can in extreme cases differ by 1 mm in `E`.
- Point (r, c) lies at arc-second `x = lon * 3600 + c * step / E`, `y = lat * 3600 + r * step / 30870`.
- Height = bilinear blend of the four surrounding integer arc-second points, in exact fractions, times 1000, rounded down
  (towards minus infinity) to whole millimetres. A point with weight 0 is not read, so a crop that ends exactly on a tile edge needs no extra file.
- A point on a tile seam is read from the east (north) tile; if that file is absent, from the last column (row) of the
  west (south) tile, which holds the same values.
- A needed point with no data, a missing file, a file of the wrong size, or a result beyond +-9000000 mm is an error
  that names the point or file.

## convert

Turns a height grid that already is in metres-per-point form into a tile. `--heights` is a JSON file that is either a list
of rows or an object whose `--key` (default `heights`) holds that list; other keys are ignored. Each number is parsed
exactly (`1e-3` is accepted), multiplied by `--unit-mm` (1 to 1000000) and rounded down to whole millimetres. Row 0 of the
input is row 0 of the tile (no flipping). `NaN`, `Infinity`, `true`, `false`, `null`, strings, rows of different length and
objects with a repeated key are refused. Sizes, ranges, `--credit`, `--out`, the printout and `--preview` work as in `crop`.

## Tile file (C-a)

```
{
  "version": 1,
  "cols": 2,
  "rows": 2,
  "stepMm": 30922,
  "credit": [
    "synthetic"
  ],
  "heightsMm": [
    [517500, 519500],
    [39005, 41005]
  ]
}
```

UTF-8, no BOM, `\n` line ends, one final `\n`. Exactly these six keys; heights are whole millimetres within +-9000000;
row 0 is the row with the smallest z. In credits only `"` and `\` are escaped; every other character, including
non-ASCII, is written as it is (the engine's JSON reader rejects `\b`, `\f`, `\r` and bare control characters, so the
language's own JSON writer must not be used).

Fingerprint: sha256 of the text `dem 1 <cols> <rows> <stepMm>\n` followed by one line per row, numbers separated by a
single space, every line (the last one too) ending in `\n`. The credit is not part of it.

## Printout

Fingerprint; lowest and highest point (mm); the share of neighbouring-point pairs (east-west and north-south) whose slope
is at most 1/4, 1/2, 3/4, 1, 1.414 or steeper, without any exaggeration; the largest map this tile can cover at that scale
(`floor((cols - 1) * stepMm / scaleMilli)`, rows likewise); and one line that can be pasted into a generated file:

```
  "base": { "tile": "t.dem.json", "sha256": "<fingerprint>", "x0Mm": 0, "z0Mm": 0, "scaleMilli": 10000, "exaggerationMilli": 1000, "datumMm": <lowest point> },
```

A generated file with `base` must also set `baseMm`.

## --preview

An 8-bit grey PNG, one pixel per point, the last row at the top (north for `crop`), lowest point black and highest white
(`floor(255 * (h - lowest) / (highest - lowest))`, all 128 when flat). The path must not be inside a git repository
(the real path is resolved, then every parent is checked for `.git`); the repository does not take image files.

## Credit and licence

`--credit` is required because a tile that is shared must carry its credit with it. Only commit a tile whose source
data licence has been checked and whose credit text follows that licence.

## Self-test

`--self-test` uses synthetic terrain only (no downloads, no files left behind). Two full-size `.hgt` files are written to
a temporary directory for the file-reading cases (one plain, one gzip); everything else uses in-memory tiles.
It is not part of the repository's automatic checks.
