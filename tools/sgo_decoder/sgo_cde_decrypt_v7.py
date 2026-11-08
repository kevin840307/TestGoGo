#!/usr/bin/env python3
"""SGO 2009 Client.exe C.D.E. wrapper decryptor (AES-256-CBC + raw DEFLATE).

Reconstructed from Client.exe.lst:
- 0057F9F0: 0x52-byte header, 'C.D.E.' signature, 0x20 encrypted header bytes
- 0057FF70: AES decrypt-block transform; 0057F470 generates inverse round keys
- 0057FD20: AES CBC decrypt payload, feed to in-client DEFLATE inflater (005A3970).
- 005A37F0: sets decompression mode but does not imply a GZIP file wrapper.
- Real Normal_Effect24.spr (421186 bytes) verified on 2026-11-08:
  Real Enemy/EN050C_ND1.spr (16626 bytes) verified, 17-byte trailing marker+padding:
  AES-CBC -> raw DEFLATE (wbits=-15) -> 1053718 byte valid SPR -> 20 PNGs.

The wrapper has NO GZIP/zlib prefix and NO GZIP CRC. Security checks include
header lengths, 16-byte block alignment, bounded inflation, DEFLATE end marker,
verified DEFLATE trailer, including 5-byte flush sentinel + up to 15 zero bytes, and exact decompressed size.
"""
from __future__ import annotations

import struct
import zlib

CDE_MAGIC = b'C.D.E.'
CDE_HEADER_SIZE = 0x52
# 32-byte AES-256 key from the original client at .data:00651D98..00651DB7
AES_KEY = bytes.fromhex(
    '0d6f041707d3044420574d832b7fc590'
    '53290d43f197df2063d4b6b52da68824'
)
HEADER_IV = b'\0' * 16  # .data:00657768...74 are zero-initialized
MAX_DECOMPRESSED_BYTES = 512 * 1024 * 1024


class CDEFormatError(ValueError):
    """CDE header, encrypted payload or decompression verification failed."""


def _decrypt_cbc(ciphertext: bytes, iv: bytes) -> bytes:
    if len(ciphertext) % 16:
        raise CDEFormatError('AES-CBC ciphertext size must be multiple of 16')
    try:
        from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    except ImportError as exc:
        raise CDEFormatError('Missing AES backend: run py -m pip install cryptography') from exc
    cipher = Cipher(algorithms.AES(AES_KEY), modes.CBC(iv))
    decryptor = cipher.decryptor()
    return decryptor.update(ciphertext) + decryptor.finalize()


def decode_cde(blob: bytes, max_output_size: int = MAX_DECOMPRESSED_BYTES) -> bytes:
    if len(blob) < CDE_HEADER_SIZE + 16:
        raise CDEFormatError(f'CDE file too small ({len(blob)} bytes)')
    if not blob.startswith(CDE_MAGIC):
        raise CDEFormatError('Missing C.D.E. signature')
    # The EXE copies +0x6..+0x11 into three fields and decrypts 32 bytes
    # at +0x12..+0x31, using the static AES-256 key and zero IV.
    head_plain = _decrypt_cbc(blob[0x12:0x32], HEADER_IV)
    # Client header address +0x1E is size after DEFLATE inflate;
    # +0x22 is encrypted data size after the 82-byte header.
    decompressed_size, encrypted_size = struct.unpack_from('<II', head_plain, 12)
    if decompressed_size < 24 or decompressed_size > max_output_size:
        raise CDEFormatError('Unreasonable decompressed size %d' % decompressed_size)
    if encrypted_size < 16 or encrypted_size % 16:
        raise CDEFormatError('Invalid AES payload size %d' % encrypted_size)
    actual_size = len(blob) - CDE_HEADER_SIZE
    if encrypted_size != actual_size:
        raise CDEFormatError('CDE encrypted payload size mismatch: header=%d actual=%d' %
                             (encrypted_size, actual_size))

    # sub_57FD20 reconstructs 4 x uint32 CBC IV values from the stored
    # decrypted header; +0x16 is 64 bits, and +0x2E / +0x2F are bytes.
    # These are written in order high32, low32, byte2E, byte2F.
    header_iv = struct.pack('<IIII',
        struct.unpack_from('<I', head_plain, 8)[0],
        struct.unpack_from('<I', head_plain, 4)[0],
        head_plain[0x2e - 0x12],
        head_plain[0x2f - 0x12],
    )
    packed = _decrypt_cbc(blob[CDE_HEADER_SIZE:], header_iv)
    # Verified with original Normal_Effect24.spr: AES output begins 9cbd0d70,
    # not 1f8b (GZIP) and not a zlib header. The game uses RAW DEFLATE.
    # Normal_Effect24 has 5 trailing bytes; EN050C_ND1 has 17 (5-byte marker + 12 zeros).
    try:
        inflater = zlib.decompressobj(wbits=-zlib.MAX_WBITS)
        raw = inflater.decompress(packed, decompressed_size + 1)
        if len(raw) > decompressed_size:
            raise CDEFormatError('Raw DEFLATE output exceeds declared size')
        if not inflater.eof:
            raise CDEFormatError('Raw DEFLATE end marker not found')
        if inflater.unconsumed_tail:
            raise CDEFormatError('Unconsumed compressed input remains')
        raw += inflater.flush()
        trailer = inflater.unused_data
        # Real SGO C.D.E. files can finish with a five-byte DEFLATE sync-flush
        # sentinel 00 00 FF FF 00 followed by up to 15 zero padding bytes.
        # The v6 <=15-byte-only check incorrectly rejected some intact sprites
        # (EN050C_ND1: 17 bytes, decoded to 5 valid SPR images).  Keep the
        # older <=15-byte allowance for previously working CDE assets, but
        # require the observed exact marker + zero-padding when >15 bytes.
        if len(trailer) > 15:
            sentinel = b'\x00\x00\xff\xff\x00'
            if (len(trailer) > len(sentinel) + 15 or
                    not trailer.startswith(sentinel) or
                    any(trailer[len(sentinel):])):
                raise CDEFormatError(
                    'Unexpected %d trailing bytes after raw DEFLATE end; '
                    'expected 00 00 FF FF 00 + at most 15 zero AES padding '
                    'bytes (or <=15 legacy padding bytes): %s' %
                    (len(trailer), trailer[:24].hex()))
    except zlib.error as exc:
        raise CDEFormatError('AES output is not valid raw DEFLATE: %s' % exc) from exc
    if len(raw) != decompressed_size:
        raise CDEFormatError('CDE size mismatch: expected %d, inflated %d' %
                             (decompressed_size, len(raw)))
    return raw
