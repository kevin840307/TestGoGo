"""v7 regression tests: original CDE asset, mock encrypted SPR, modes, resume."""
import csv
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib
from pathlib import Path
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

HERE = Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from sgo_cde_decrypt_v7 import AES_KEY, CDEFormatError, decode_cde
from sgo_spr_retry_type_modes_v7 import decode_spr, classify_spr
TOOL = HERE / 'sgo_spr_retry_type_modes_v7.py'
REAL_CDE = HERE / 'Normal_Effect24.spr'
REAL_ACT = HERE / 'Normal_Effect24.ACT'
REAL_ND1 = HERE / 'EN050C_ND1.spr'


def simple_spr():
    # 1x1 opaque red image, row RLE: 1 RGB565 pixel, then end marker.
    image = struct.pack('<IIiiIHHH',1,1,5,6,20,1,0xF800,0x8000)
    return image + struct.pack('<II',0,1)


def encrypt(data, iv):
    return Cipher(algorithms.AES(AES_KEY), modes.CBC(iv)).encryptor().update(data)


def create_encrypted(plain):
    co = zlib.compressobj(6,zlib.DEFLATED,-15)
    compressed=co.compress(plain)+co.flush()
    padded=compressed+b'\x00'*((-len(compressed))%16)
    h=bytearray(32)
    h[4:12]=b'12345678'
    h[28]=0x19;h[29]=0x1A
    struct.pack_into('<II',h,12,len(plain),len(padded))
    iv=struct.pack('<IIII',struct.unpack_from('<I',h,8)[0],struct.unpack_from('<I',h,4)[0],h[28],h[29])
    return b'C.D.E.'+struct.pack('<III',1,1,0)+encrypt(bytes(h),b'\0'*16)+b' '*32+encrypt(padded,iv)


def run_cli(*args):
    return subprocess.run([sys.executable,str(TOOL),*(str(x) for x in args)],capture_output=True,text=True)

class TestCDEV7(unittest.TestCase):
    def test_plain_sprite_format(self):
        spr=decode_spr(simple_spr())
        self.assertEqual(len(spr),1)
        self.assertEqual(spr[0].image.size,(1,1))
        self.assertEqual(spr[0].image.getpixel((0,0)),(255,0,0,255))
        self.assertEqual((spr[0].x,spr[0].y),(5,6))

    def test_raw_deflate_roundtrip_not_gzip(self):
        original=simple_spr()
        cde=create_encrypted(original)
        self.assertEqual(decode_cde(cde),original)
        self.assertEqual(len(decode_spr(decode_cde(cde))),1)

    def test_genuine_normal_effect_24(self):
        if not REAL_CDE.exists():
            self.skipTest('Original CDE sample not bundled; set next to test to verify')
        original=REAL_CDE.read_bytes()
        self.assertEqual(len(original),421186)
        raw=decode_cde(original)
        self.assertEqual(len(raw),1053718)
        sprites=decode_spr(raw)
        self.assertEqual(len(sprites),20)
        self.assertEqual(sprites[0].image.size,(166,68))
        self.assertEqual(sprites[8].image.mode,'RGBA')
        if REAL_ACT.exists():
            act=REAL_ACT.read_bytes()
            self.assertEqual(act[19],len(sprites))
            self.assertEqual([struct.unpack_from('<I',act,23+i*8)[0] for i in range(20)],list(range(20)))

    def test_genuine_enemy_nd1_with_17_byte_trailer(self):
        if not REAL_ND1.exists():
            self.skipTest('Original EN050C_ND1.spr not bundled')
        blob = REAL_ND1.read_bytes()
        self.assertEqual(len(blob), 16626)
        raw = decode_cde(blob)
        self.assertEqual(len(raw), 36962)
        sprites = decode_spr(raw)
        self.assertEqual(len(sprites), 5)
        self.assertEqual(sprites[0].image.size, (66, 101))
        self.assertEqual(sprites[-1].image.size, (80, 51))

    def test_real_enemy_cli_and_skip_on_rerun(self):
        if not REAL_ND1.exists():
            self.skipTest('Original EN050C_ND1.spr not bundled')
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / 'Data1_PNG2'
            first = run_cli(REAL_ND1, '-o', out, '--spr-type', 'cde', '--metadata')
            self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
            frames = sorted(out.glob('EN050C_ND1.[0-9][0-9][0-9][0-9].png'))
            self.assertEqual(len(frames), 5)
            original_hashes = [hashlib.sha256(p.read_bytes()).digest() for p in frames]
            again = run_cli(REAL_ND1, '-o', out, '--spr-type', 'cde', '--show-skips')
            self.assertEqual(again.returncode, 0, again.stdout + again.stderr)
            self.assertIn('PASS/SKIP', again.stdout)
            self.assertEqual([hashlib.sha256(p.read_bytes()).digest() for p in frames], original_hashes)

    def test_various_valid_flush_trailers_16_to_20_bytes(self):
        # Generate deterministic synthetic CDE blobs with all real-world long
        # marker/padding lengths, ensuring we do not just raise the length cap.
        seen = set()
        for n in range(24, 400):
            payload = bytes(range(n % 256)) + bytes([n % 256]) * (n // 256)
            co = zlib.compressobj(6, zlib.DEFLATED, -15)
            compressed = co.compress(payload) + co.flush()
            marker = b'\x00\x00\xff\xff\x00'
            suffix = marker + b'\x00' * (-(len(compressed) + len(marker)) % 16)
            if len(suffix) not in (16, 17, 18, 19, 20) or len(suffix) in seen:
                continue
            padded = compressed + suffix
            h = bytearray(32)
            h[4:12] = b'12345678'
            h[28:30] = b'\x19\x1a'
            struct.pack_into('<II', h, 12, len(payload), len(padded))
            iv = struct.pack('<IIII', struct.unpack_from('<I', h, 8)[0],
                             struct.unpack_from('<I', h, 4)[0], h[28], h[29])
            cde = (b'C.D.E.' + struct.pack('<III', 1, 1, 0) + encrypt(bytes(h), b'\0' * 16)
                   + b' ' * 32 + encrypt(padded, iv))
            self.assertEqual(decode_cde(cde), payload)
            seen.add(len(suffix))
            if seen == {16, 17, 18, 19, 20}:
                break
        self.assertEqual(seen, {16, 17, 18, 19, 20})

    def test_invalid_nonzero_trailer_rejected(self):
        payload = bytes(range(100))
        co = zlib.compressobj(6, zlib.DEFLATED, -15)
        compressed = co.compress(payload) + co.flush()
        # 21 bytes after EOF with legitimate-looking marker must fail;
        # cannot be explained by 5-byte sentinel + at most 15 zero bytes.
        trailing = b'\x00\x00\xff\xff\x00' + b'\x00' * 26
        padded = compressed + trailing
        padded += b'\x00' * (-len(padded) % 16)
        h = bytearray(32)
        h[4:12] = b'12345678'; h[28] = 25; h[29] = 26
        struct.pack_into('<II', h, 12, len(payload), len(padded))
        iv = struct.pack('<IIII', struct.unpack_from('<I', h, 8)[0],
                         struct.unpack_from('<I', h, 4)[0], h[28], h[29])
        cde = b'C.D.E.' + struct.pack('<III', 1, 1, 0) + encrypt(bytes(h), b'\x00'*16) + b' '*32 + encrypt(padded,iv)
        with self.assertRaisesRegex(CDEFormatError, 'Unexpected .* trailing bytes'):
            decode_cde(cde)

    def test_nonzero_marker_padding_rejected(self):
        # Long extra bytes must match the known marker and zero-fill only.
        for n in range(30, 450):
            payload = bytes(range(n % 256)) + bytes([n % 256]) * (n // 256)
            co = zlib.compressobj(6, zlib.DEFLATED, -15)
            compressed = co.compress(payload) + co.flush()
            if len(compressed) % 16 == 0:
                break
        else:
            self.fail('Could not construct a compressed block aligned to 16')
        # 16 zero garbage bytes are not a known trailer.
        bogus = compressed + b'\x00' * 16
        h = bytearray(32)
        h[4:12] = b'12345678'
        h[28:30] = b'\x19\x1a'
        struct.pack_into('<II', h, 12, len(payload), len(bogus))
        iv = struct.pack('<IIII', struct.unpack_from('<I', h, 8)[0],
                         struct.unpack_from('<I', h, 4)[0], h[28], h[29])
        cde = (b'C.D.E.' + struct.pack('<III', 1, 1, 0) + encrypt(bytes(h), b'\x00'*16)
               + b' '*32 + encrypt(bogus, iv))
        with self.assertRaisesRegex(CDEFormatError, 'Unexpected .* trailing bytes'):
            decode_cde(cde)

    def test_invalid_truncated_and_corrupt_cde(self):
        cde=create_encrypted(simple_spr())
        for fail in [b'garbage',cde[:-1],cde[:82]+bytes(len(cde)-82),cde[:18]+bytes(32)+cde[50:]]:
            with self.subTest(length=len(fail)):
                with self.assertRaises(CDEFormatError):
                    decode_cde(fail)

    def test_mode_filters_and_preserves_plain(self):
        with tempfile.TemporaryDirectory() as tmp:
            source=Path(tmp)/'output'; out=Path(tmp)/'Data1_PNG2'; source.mkdir()
            (source/'plain.spr').write_bytes(simple_spr())
            (source/'cde.spr').write_bytes(create_encrypted(simple_spr()))
            result=run_cli(source,'-o',out,'--recursive','--spr-type','cde','--metadata')
            self.assertEqual(result.returncode,0,result.stderr)
            self.assertFalse((out/'plain.png').exists())
            self.assertTrue((out/'cde.png').is_file())
            hash_cde=hashlib.sha256((out/'cde.png').read_bytes()).hexdigest()
            result=run_cli(source,'-o',out,'--recursive','--spr-type','auto')
            self.assertEqual(result.returncode,0,result.stderr)
            self.assertTrue((out/'plain.png').exists())
            self.assertEqual(hashlib.sha256((out/'cde.png').read_bytes()).hexdigest(),hash_cde)
            self.assertIn('SKIP_ALREADY_OK',(out/'spr_retry_report.csv').read_text('utf-8-sig'))

    def test_failed_csv_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            src=Path(tmp)/'src';out=Path(tmp)/'output';src.mkdir()
            (src/'cde.spr').write_bytes(create_encrypted(simple_spr()))
            (src/'untouched.spr').write_bytes(simple_spr())
            csvp=Path(tmp)/'previous.csv'
            with csvp.open('w',newline='',encoding='utf-8-sig') as f:
                w=csv.writer(f)
                w.writerow(['relative_spr','spr_type','status','new_pngs','error','diagnostics'])
                w.writerow(['cde.spr','cde','UNSUPPORTED_CDE',0,'',''])
            res=run_cli(src,'-o',out,'--recursive','--spr-type','cde','--failed-log',csvp)
            self.assertEqual(res.returncode,0,res.stdout+res.stderr)
            self.assertTrue((out/'cde.png').exists())
            self.assertFalse((out/'untouched.png').exists())

    def test_multiframe_partial_resume_protects_existing_png(self):
        if not REAL_CDE.exists():
            self.skipTest('Need original Normal_Effect24.spr')
        with tempfile.TemporaryDirectory() as tmp:
            inp=Path(tmp)/'input';out=Path(tmp)/'output';inp.mkdir()
            shutil.copyfile(REAL_CDE,inp/'Normal_Effect24.spr')
            first=run_cli(inp,'-o',out,'--spr-type','cde','--metadata')
            self.assertEqual(first.returncode,0,first.stdout+first.stderr)
            allframes=sorted(out.glob('Normal_Effect24.[0-9][0-9][0-9][0-9].png'))
            self.assertEqual(len(allframes),20)
            first_hash=hashlib.sha256(allframes[0].read_bytes()).digest()
            repeat=run_cli(inp,'-o',out,'--spr-type','cde','--show-skips')
            self.assertEqual(repeat.returncode,0,repeat.stdout+repeat.stderr)
            self.assertIn('PASS/SKIP',repeat.stdout)
            self.assertEqual(hashlib.sha256(allframes[0].read_bytes()).digest(),first_hash)
            allframes[-1].unlink()
            res=run_cli(inp,'-o',out,'--spr-type','cde','--metadata')
            self.assertEqual(res.returncode,0,res.stdout+res.stderr)
            self.assertTrue(allframes[-1].exists())
            self.assertEqual(hashlib.sha256(allframes[0].read_bytes()).digest(),first_hash)
            self.assertTrue((out/'Normal_Effect24.spr_complete.json').exists())

    def test_real_file_cli_generates_all_pngs(self):
        if not REAL_CDE.exists(): self.skipTest('Original encrypted SPR missing')
        with tempfile.TemporaryDirectory() as tmp:
            out=Path(tmp)/'decoded'
            res=run_cli(REAL_CDE,'-o',out,'--spr-type','cde','--metadata')
            self.assertEqual(res.returncode,0,res.stderr)
            self.assertEqual(len(list(out.glob('Normal_Effect24.[0-9][0-9][0-9][0-9].png'))),20)
            self.assertEqual(len(list(out.glob('Normal_Effect24.[0-9][0-9][0-9][0-9].json'))),20)
            self.assertTrue((out/'Normal_Effect24.spr_complete.json').exists())

if __name__=='__main__': unittest.main(verbosity=2)
