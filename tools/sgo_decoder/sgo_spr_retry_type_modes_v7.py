#!/usr/bin/env python3
"""Convert Stargate Online / SGO SPR images to transparent PNG files.

Compatible with the recovered SPR layout used by the 2009 clients:
  [images][uint32 image_offsets...][uint32 image_count]
Per image: <uint32 width, uint32 height, int32 x, int32 y>,
           <uint32 row_start_offset * height>, then RLE scanlines.
Each RLE command is an LE uint16: 0x8000 = transparent skip,
0x4000 = per-run alpha (following uint16), 0x0000 = opaque pixels.
The lower 14 bits give the pixel count. Pixel colors are RGB565.

Requirements:
  python -m pip install Pillow

Examples:
  python sgo_spr_retry_type_modes_v7.py output -o Data1_PNG -r --spr-type plain --metadata
  python sgo_spr_retry_type_modes_v7.py output -o Data1_PNG -r --spr-type cde --list-only
  python sgo_spr_retry_type_modes_v7.py output -o Data1_PNG -r --spr-type auto
  python sgo_spr_retry_type_modes_v7.py output -o Data1_PNG -r --failed-log last_run.log

C.D.E. decryption is reconstructed from the supplied 2009 Client.exe.lst and
a verified real CDE file (Normal_Effect24.spr). Raw DEFLATE is used, NOT
zlib/GZIP. Existing PNGs are never modified without --overwrite.

Single-frame files become <stem>.png. Multi-frame files become
<stem>.0000.png, <stem>.0001.png, etc. Relative subfolders are preserved.
Use --metadata to save image origin offsets in a neighboring JSON file.
"""
from __future__ import annotations

import argparse
import csv
import re
import json
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image
from sgo_cde_decrypt_v7 import decode_cde, CDEFormatError

MAX_IMAGES = 100_000
MAX_SIDE = 16_384
MAX_PIXELS = 120_000_000
CDE_MAGIC = b'C.D.E.'
CDE_HEADER_SIZE = 0x52


def classify_spr(source: Path) -> str:
    """Return 'cde' for Client.exe's C.D.E. container, else 'plain'.

    Classifying by magic rather than the final uint32 avoids misinterpreting
    ciphertext as an image-count footer. Do not infer decryption from this.
    """
    with source.open('rb') as handle:
        return 'cde' if handle.read(len(CDE_MAGIC)) == CDE_MAGIC else 'plain'




class SPRFormatError(ValueError):
    """Raised when an SPR file has an invalid or unsupported structure."""


@dataclass(frozen=True)
class Sprite:
    image: Image.Image
    x: int
    y: int


def read_u16(data: bytes, at: int, limit: int) -> int:
    if at < 0 or at + 2 > limit:
        raise SPRFormatError(f"Truncated uint16 at offset 0x{at:X}")
    return struct.unpack_from('<H', data, at)[0]


def read_image(data: bytes, image_start: int, image_end: int) -> Sprite:
    if image_start < 0 or image_start + 16 > image_end:
        raise SPRFormatError("Image header is out of bounds")
    width, height, x_origin, y_origin = struct.unpack_from('<IIii', data, image_start)
    if (not 0 < width <= MAX_SIDE or not 0 < height <= MAX_SIDE
            or width * height > MAX_PIXELS):
        raise SPRFormatError(f"Invalid sprite dimensions: {width}x{height}")
    row_table_end = image_start + 16 + height * 4
    if row_table_end > image_end:
        raise SPRFormatError("Scanline table extends beyond image data")

    starts = struct.unpack_from(f'<{height}I', data, image_start + 16)
    # Most known sprites store absolute file offsets; preserve them as such.
    if (starts[0] < row_table_end or starts[-1] >= image_end
            or any(s >= e for s, e in zip(starts, starts[1:]))):
        raise SPRFormatError("Invalid or unordered scanline offsets")

    rgba = bytearray(width * height * 4)
    for row, start in enumerate(starts):
        end = starts[row + 1] if row + 1 < height else image_end
        cursor = start
        pixel_x = 0
        has_end = False
        while cursor < end:
            command = read_u16(data, cursor, end)
            cursor += 2
            run = command & 0x3FFF
            is_skip = bool(command & 0x8000)
            has_alpha = bool(command & 0x4000)
            if is_skip:
                if has_alpha:
                    raise SPRFormatError(f"Row {row}: invalid combined RLE flags")
                if run == 0:
                    has_end = True
                    break
                pixel_x += run
                if pixel_x > width:
                    raise SPRFormatError(f"Row {row}: transparent skip outside width")
                continue

            if run == 0:
                raise SPRFormatError(f"Row {row}: zero-length color run")
            if pixel_x + run > width:
                raise SPRFormatError(f"Row {row}: pixel run exceeds width")
            alpha = 255
            if has_alpha:
                alpha = read_u16(data, cursor, end)
                cursor += 2
                if alpha > 255:
                    raise SPRFormatError(f"Row {row}: alpha {alpha} exceeds 255")
            if cursor + run * 2 > end:
                raise SPRFormatError(f"Row {row}: truncated RGB565 pixel run")

            colors = struct.unpack_from(f'<{run}H', data, cursor)
            cursor += run * 2
            for color in colors:
                out = (row * width + pixel_x) * 4
                # Replicating low bits expands RGB565 to full 8-bit channels.
                r = (color >> 11) & 31
                g = (color >> 5) & 63
                b = color & 31
                rgba[out] = (r << 3) | (r >> 2)
                rgba[out + 1] = (g << 2) | (g >> 4)
                rgba[out + 2] = (b << 3) | (b >> 2)
                rgba[out + 3] = alpha
                pixel_x += 1
        if not has_end:
            raise SPRFormatError(f"Row {row}: missing end marker")
        if cursor != end:
            raise SPRFormatError(f"Row {row}: unexpected bytes after end marker")
    return Sprite(Image.frombytes('RGBA', (width, height), bytes(rgba)), x_origin, y_origin)


def decode_spr(data: bytes) -> list[Sprite]:
    if len(data) < 24:
        raise SPRFormatError("File too small to contain an SPR image")
    count = struct.unpack_from('<I', data, len(data) - 4)[0]
    if not 0 < count <= MAX_IMAGES or (4 + count * 4) > len(data):
        raise SPRFormatError(f"Invalid image count: {count}")
    index_begin = len(data) - 4 - count * 4
    offsets = struct.unpack_from(f'<{count}I', data, index_begin)
    if offsets[0] != 0 or any(x >= y for x, y in zip(offsets, offsets[1:])):
        raise SPRFormatError("Invalid image offset table")
    if offsets[-1] >= index_begin:
        raise SPRFormatError("Last image offset exceeds index boundary")
    frames = []
    for i, offset in enumerate(offsets):
        next_offset = offsets[i + 1] if i + 1 < count else index_begin
        try:
            frames.append(read_image(data, offset, next_offset))
        except SPRFormatError as exc:
            raise SPRFormatError(f"Image {i}: {exc}") from exc
    return frames


def completed_marker(source: Path, target_dir: Path) -> Path:
    return target_dir / f'{source.stem}.spr_complete.json'


def expected_png_names(stem: str, count: int) -> list[str]:
    return ([f'{stem}.png'] if count == 1 else
            [f'{stem}.{i:04d}.png' for i in range(count)])


def write_completed_marker(source: Path, target_dir: Path, count: int) -> None:
    """A completion marker is committed only after ALL frames have been saved."""
    stat = source.stat()
    marker = completed_marker(source, target_dir)
    content = {
        'source': source.name,
        'source_size': stat.st_size,
        'source_mtime_ns': stat.st_mtime_ns,
        'frame_count': count,
        'output_names': expected_png_names(source.stem, count),
    }
    tmp = marker.with_name(marker.name + '.tmp')
    tmp.write_text(json.dumps(content, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    tmp.replace(marker)


def completed_marker_is_valid(source: Path, target_dir: Path) -> bool:
    """Verify all output frame names still exist before declaring a CDE done."""
    try:
        marker = completed_marker(source, target_dir)
        data = json.loads(marker.read_text(encoding='utf-8'))
        stat = source.stat()
        count = data['frame_count']
        if (data.get('source') != source.name or
            data.get('source_size') != stat.st_size or
            data.get('source_mtime_ns') != stat.st_mtime_ns or
            not isinstance(count, int) or not 0 < count <= MAX_IMAGES or
            data.get('output_names') != expected_png_names(source.stem, count)):
            return False
        return all(png_is_present(target_dir / name) for name in data['output_names'])
    except (OSError, ValueError, TypeError, KeyError):
        return False


def convert_file(source: Path, target_dir: Path, overwrite: bool, metadata: bool) -> list[Path]:
    content = source.read_bytes()
    if content.startswith(CDE_MAGIC):
        try:
            content = decode_cde(content)
        except CDEFormatError as exc:
            raise SPRFormatError(f'CDE decrypt failed: {exc}') from exc
    sprites = decode_spr(content)
    target_dir.mkdir(parents=True, exist_ok=True)
    outputs: list[Path] = []
    stem = source.stem
    for i, sprite in enumerate(sprites):
        name = f'{stem}.png' if len(sprites) == 1 else f'{stem}.{i:04d}.png'
        png = target_dir / name
        if png.exists() and not overwrite:
            if png_is_present(png):
                continue  # Preserve a valid PNG from an interrupted conversion.
            raise FileExistsError(f"Existing non-PNG or truncated output: {png}; move it aside before retrying")
        sprite.image.save(png, format='PNG', optimize=True)
        outputs.append(png)
        if metadata:
            sidecar = png.with_suffix('.json')
            sidecar.write_text(json.dumps({
                'source': source.name, 'frame': i, 'frames': len(sprites),
                'width': sprite.image.width, 'height': sprite.image.height,
                'origin_x': sprite.x, 'origin_y': sprite.y
            }, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    write_completed_marker(source, target_dir, len(sprites))
    return outputs


def png_is_present(file: Path) -> bool:
    """Only count a real, non-empty PNG header as an existing success."""
    try:
        if file.stat().st_size < 33:
            return False
        with file.open('rb') as f:
            return f.read(8) == b'\x89PNG\r\n\x1a\n'
    except OSError:
        return False


def already_converted(source: Path, out_dir: Path) -> bool:
    """Fast resumption for previous versions of this same decoder.

    The previous script decodes ALL frames before writing ANY images, so
    existing PNGs normally signify a previous successful conversion. To avoid
    skipping incomplete multi-frame saves, also check their final PNG using
    the count stored in a known-valid footer (when available).
    """
    if completed_marker_is_valid(source, out_dir):
        return True
    stem = source.stem
    single = out_dir / f'{stem}.png'
    if png_is_present(single):
        return True
    first = out_dir / f'{stem}.0000.png'
    if not png_is_present(first):
        return False
    try:
        with source.open('rb') as f:
            f.seek(-4, 2)
            count = struct.unpack('<I', f.read(4))[0]
        if not 1 < count <= MAX_IMAGES:
            return False
        # A completed batch always has the final numbered image.
        last = out_dir / f'{stem}.{count - 1:04d}.png'
        return png_is_present(last)
    except (OSError, struct.error):
        return False


def parse_failed_log(log_path: Path) -> set[str]:
    """Read v3/v4 spr_retry_report.csv OR console [FAIL] output.

    Accept statuses FAIL, UNSUPPORTED_CDE, CDE_DECRYPT_FAILED and avoid
    reprocessing successes from a report. Relative source path separators
    are normalized across Windows/Linux.
    """
    if log_path.suffix.lower() == '.csv':
        with log_path.open('r', encoding='utf-8-sig', newline='') as fh:
            reader = csv.DictReader(fh)
            if not reader.fieldnames or 'relative_spr' not in reader.fieldnames:
                raise ValueError('CSV lacks relative_spr column')
            failed = {
                row['relative_spr'].replace('\\', '/').lower()
                for row in reader
                if row.get('relative_spr', '').lower().endswith('.spr')
                and row.get('status', '').upper() in (
                    'FAIL', 'UNSUPPORTED_CDE', 'CDE_DECRYPT_FAILED')
            }
    else:
        failed = set()
        with log_path.open('r', encoding='utf-8-sig', errors='replace') as handle:
            for line in handle:
                match = re.search(r'\[FAIL\]\s+(.+?\.spr)(?::\s|$)', line, re.I)
                if match:
                    failed.add(match.group(1).strip().replace('\\', '/').lower())
    if not failed:
        raise ValueError(f'No failed SPRs found in {log_path}')
    return failed


def file_diagnostics(source: Path) -> str:
    """Bounded, non-executing inspection for unsupported SPR variants."""
    try:
        with source.open('rb') as handle:
            size = handle.seek(0, 2)
            handle.seek(0)
            head = handle.read(24)
            handle.seek(max(0, size - 24))
            tail = handle.read(24)
        count = struct.unpack('<I', tail[-4:])[0] if len(tail) >= 4 else None
        known = 'C.D.E. encrypted SPR container' if head.startswith(CDE_MAGIC) else 'unknown'
        if head.startswith(b'\x89PNG'):
            known = 'PNG payload renamed .spr'
        elif head.startswith(b'DDS '):
            known = 'DDS payload renamed .spr'
        elif head.startswith(b'BM'):
            known = 'BMP payload renamed .spr'
        elif head.startswith(b'\x1f\x8b'):
            known = 'gzip header'
        elif head[:2] in (b'\x78\x01', b'\x78\x9c', b'\x78\xda'):
            known = 'zlib header'
        return (f'size={size}; head={head.hex()}; tail={tail.hex()}; '
                f'footer_u32={count}; signature={known}')
    except OSError as exc:
        return f'cannot inspect: {exc}'


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('input', type=Path, help='One .spr or unpacked Data1 folder')
    parser.add_argument('-o', '--output', type=Path, help='Existing PNG output folder')
    parser.add_argument('-r', '--recursive', action='store_true', help='Find SPRs in all nested directories')
    parser.add_argument('--resume', action='store_true', default=True,
                        help='Skip validated existing PNGs (default; safe for retry)')
    parser.add_argument('--overwrite', action='store_true',
                        help='Intentionally overwrite previously generated PNGs')
    parser.add_argument('--metadata', action='store_true', help='Write per-frame JSON offsets for new PNGs')
    parser.add_argument('--spr-type', '--mode', choices=('auto', 'plain', 'cde'),
                        default='auto', dest='spr_type',
                        help='auto=inspect both; plain=only regular SPR; '
                             'cde=only C.D.E. encrypted SPR (AES-CBC + raw DEFLATE)')
    parser.add_argument('--list-only', action='store_true',
                        help='List matching SPR types only, without converting or modifying PNGs')
    parser.add_argument('--failed-log', type=Path,
                        help='Previous [FAIL] console log or spr_retry_report.csv; process only failed SPRs')
    parser.add_argument('--report', type=Path,
                        help='CSV report path; default inside output: spr_retry_report.csv')
    parser.add_argument('--show-skips', action='store_true', help='Print each already-successful SPR (default: quiet)')
    args = parser.parse_args(argv)

    if args.input.is_file():
        if args.input.suffix.lower() != '.spr':
            parser.error('Input must be a .spr file')
        files = [args.input]
        base_dir = args.input.parent
        out = args.output or args.input.parent / 'spr_png'
    elif args.input.is_dir():
        base_dir = args.input
        files = sorted(args.input.rglob('*') if args.recursive else args.input.iterdir())
        files = [p for p in files if p.is_file() and p.suffix.lower() == '.spr']
        out = args.output or args.input.with_name(args.input.name + '_png')
    else:
        parser.error(f'Input not found: {args.input}')

    if not files:
        print('No SPRs found. Use --recursive for nested folders.', file=sys.stderr)
        return 1
    selected: set[str] | None = None
    if args.failed_log:
        try:
            selected = parse_failed_log(args.failed_log)
        except (OSError, ValueError) as exc:
            parser.error(str(exc))
    report_path = args.report or (out / 'spr_retry_report.csv')
    if not args.list_only:
        out.mkdir(parents=True, exist_ok=True)
        report_path.parent.mkdir(parents=True, exist_ok=True)
        if any(f.resolve() == report_path.resolve() for f in files):
            parser.error('Report must not overwrite a source SPR')

    skip_ok = skip_log = skip_type = succeeded = failed = cde_succeeded = cde_failed = new_pngs = matched = 0
    count_by_type = {'plain': 0, 'cde': 0}
    # In list-only mode avoid creating or changing any files (including CSV).
    # A tiny no-op report writer lets the same inspection loop serve both modes.
    class _NoopWriter:
        def writerow(self, _fields):
            pass

    from contextlib import nullcontext
    with (nullcontext(None) if args.list_only else
          report_path.open('w', newline='', encoding='utf-8-sig')) as report_file:
        writer = csv.writer(report_file) if report_file else _NoopWriter()
        writer.writerow(['relative_spr', 'spr_type', 'status', 'new_pngs', 'error', 'diagnostics'])
        for source in files:
            relative = source.relative_to(base_dir)
            rel_key = relative.as_posix().lower()
            target_dir = out / relative.parent
            if selected is not None and rel_key not in selected:
                skip_log += 1
                continue
            try:
                spr_type = classify_spr(source)
            except OSError as exc:
                failed += 1
                writer.writerow([relative.as_posix(), 'unknown', 'FAIL', 0, str(exc), ''])
                print(f'[FAIL] {relative}: cannot classify SPR: {exc}', file=sys.stderr)
                continue
            count_by_type[spr_type] += 1
            if args.spr_type != 'auto' and args.spr_type != spr_type:
                skip_type += 1
                continue
            matched += 1
            if args.list_only:
                print(f'[{spr_type.upper()}] {relative}')
                continue
            if not args.overwrite and already_converted(source, target_dir):
                skip_ok += 1
                writer.writerow([relative.as_posix(), spr_type, 'SKIP_ALREADY_OK', 0, '', ''])
                if args.show_skips:
                    print(f'[PASS/SKIP] {relative} ({spr_type}; existing PNGs untouched)')
                continue
            try:
                converted = convert_file(source, target_dir,
                                         overwrite=args.overwrite,
                                         metadata=args.metadata)
                succeeded += 1
                if spr_type == 'cde':
                    cde_succeeded += 1
                new_pngs += len(converted)
                writer.writerow([relative.as_posix(), spr_type, 'OK', len(converted), '', ''])
                print(f'[OK] {relative}: {len(converted)} PNG(s)')
            except (SPRFormatError, OSError, ValueError, struct.error) as exc:
                failed += 1
                if spr_type == 'cde':
                    cde_failed += 1
                diagnostic = file_diagnostics(source)
                status = 'CDE_DECRYPT_FAILED' if spr_type == 'cde' else 'FAIL'
                writer.writerow([relative.as_posix(), spr_type, status, 0, str(exc), diagnostic])
                print(f'[FAIL] {relative}: {exc} | {diagnostic}', file=sys.stderr)

    print(f'Files scanned: {len(files)} | Mode: {args.spr_type} | '
          f'Matched: {matched} | Plain: {count_by_type["plain"]} | '
          f'CDE: {count_by_type["cde"]} | Mode-skipped: {skip_type}')
    print(f'Already OK (skipped): {skip_ok} | Not in failed log: {skip_log} | '
          f'Newly OK: {succeeded} | CDE OK: {cde_succeeded} | CDE failed: {cde_failed} | '
          f'Still failed: {failed} | New PNGs: {new_pngs}')
    if not args.list_only:
        print(f'Output (previous PNGs preserved): {out} | Report: {report_path}')
    if failed:
        print('Unrecognized SPR variants need an original failing .spr sample to validate '
              'AES/decompression; report includes header/tail bytes.', file=sys.stderr)
    # Nonzero status when attempted decoding remains unfinished, allowing
    # automated pipelines to distinguish a completed run from a CDE inventory.
    return 0 if args.list_only or failed == 0 else 1


if __name__ == '__main__':
    raise SystemExit(main())
