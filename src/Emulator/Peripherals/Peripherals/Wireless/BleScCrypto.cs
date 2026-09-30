//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.IO;
using System.Numerics;

namespace Antmicro.Renode.Peripherals.Wireless
{
    // Bluetooth LE Secure Connections cryptographic toolkit primitives: AES-CMAC (RFC 4493),
    // the f4 / f5 key-derivation functions and g1 (P-256 ECDH shared secret). The f4/f5 byte layout
    // mirrors cordio/ExActLE so a simulated peer can derive identical confirm values, MAC keys and LTKs.
    public static class BleScCrypto
    {
        private const int BlockSize = 16;

        /// <summary>AES-128 CMAC (RFC 4493) over an arbitrary-length message.</summary>
        public static byte[] AesCmac(byte[] key, byte[] message)
        {
            if (key == null || key.Length != BlockSize)
                throw new ArgumentException("key must be 128 bits", nameof(key));

            var rk = ExpandKey(key);

            byte[] L = Enc(rk, new byte[BlockSize]);
            byte[] k1 = Dbl(L);
            byte[] k2 = Dbl(k1);

            int len = message?.Length ?? 0;
            bool complete = len > 0 && (len % BlockSize == 0);
            int n = (len + BlockSize - 1) / BlockSize;
            if (n == 0)
                n = 1;

            byte[] x = new byte[BlockSize];
            for (int i = 0; i < n - 1; i++)
                x = Enc(rk, Xor(x, SubBlock(message, i)));

            byte[] last = new byte[BlockSize];
            if (complete)
            {
                Array.Copy(message, len - BlockSize, last, 0, BlockSize);
                XorInPlace(last, k1);
            }
            else
            {
                int tailLen = len % BlockSize; // 0 for an empty message
                Array.Copy(message, len - tailLen, last, 0, tailLen);
                last[tailLen] = 0x80;
                XorInPlace(last, k2);
            }

            return Enc(rk, Xor(x, last));
        }

        // f4(U, V, x, Z) = AES-CMAC_Z (U || V || x). U/V are the own/peer public-key X coordinates.
        public static byte[] F4(byte[] ownPubKeyX, byte[] peerPubKeyX, byte z, byte[] nonce)
            => AesCmac(nonce, Concat(ownPubKeyX, peerPubKeyX, new[] { z }));

        private static readonly byte[] F5Salt =
        {
            0x6C, 0x88, 0x83, 0x91, 0xAA, 0xF5, 0xA5, 0x38,
            0x60, 0x37, 0x0B, 0xDB, 0x5A, 0x60, 0x83, 0xBE
        };

        private static readonly byte[] F5KeyId = { 0x62, 0x74, 0x6c, 0x65 }; // cordio smp_sc_act.c smpScF5Key ("btle")

        public sealed class F5Result
        {
            public byte[] T;
            public byte[] MacKey;
            public byte[] Ltk;
        }

        // f5(W, N1, N2, A_initiator, A_responder):
        //   T      = AES-CMAC_salt (W)
        //   macKey = AES-CMAC_T ( 0x00 || "btle" || N1 || N2 || A_i || A_r || 0x01 0x00 )
        //   ltk    = AES-CMAC_T ( 0x01 || "btle" || N1 || N2 || A_i || A_r || 0x01 0x00 )
        public static F5Result F5(byte[] w, byte[] n1, byte[] n2, byte[] aInitiator, byte[] aResponder)
        {
            var t = AesCmac(F5Salt, w);
            return new F5Result
            {
                T = t,
                MacKey = AesCmac(t, BuildF5Text(0x00, n1, n2, aInitiator, aResponder)),
                Ltk = AesCmac(t, BuildF5Text(0x01, n1, n2, aInitiator, aResponder))
            };
        }

        private static byte[] BuildF5Text(byte counter, byte[] n1, byte[] n2, byte[] aI, byte[] aR)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(counter);
            ms.Write(F5KeyId, 0, F5KeyId.Length);
            ms.Write(n1, 0, n1.Length);
            ms.Write(n2, 0, n2.Length);
            ms.Write(aI, 0, aI.Length);
            ms.Write(aR, 0, aR.Length);
            ms.WriteByte(0x01); // 256-bit length of the LTK (little-endian)
            ms.WriteByte(0x00);
            return ms.ToArray();
        }

        // g1: ECDH on P-256. Returns the x coordinate (32 bytes, big-endian) of priv * Q(pubX,pubY).
        public static byte[] G1(byte[] privKey, byte[] pubX, byte[] pubY)
        {
            var k = FromBe(privKey);
            var q = new P256.EcPoint(FromBe(pubX), FromBe(pubY));
            return ToBe32(P256.ScalarMultiply(k, q).X);
        }

        // NIST P-256 base point G (big-endian coordinate bytes) - used to derive ephemeral public keys.
        private static readonly BigInteger P256Gx = FromBe(Convert.FromHexString(
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"));
        private static readonly BigInteger P256Gy = FromBe(Convert.FromHexString(
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"));

        // Derive the ephemeral public point (X, Y) from a P-256 private scalar: Q = d * G.
        public static (byte[] X, byte[] Y) GeneratePublicKey(byte[] privKey)
        {
            if(privKey == null || privKey.Length != 32)
                throw new ArgumentException("P-256 private key must be exactly 32 bytes.", nameof(privKey));

            var k = FromBe(privKey);
            if(k.Sign <= 0 || k.CompareTo(P256.P) >= 0)
                throw new ArgumentException("Private scalar out of range for P-256.", nameof(privKey));

            var pt = P256.ScalarMultiply(k, new P256.EcPoint(P256Gx, P256Gy));
            return (ToBe32(pt.X), ToBe32(pt.Y));
        }

        // ---- Self-contained AES-128 ECB block cipher (no external crypto dependency) ----

        private static readonly byte[] SBox = {
            0x63,0x7c,0x77,0x7b,0xf2,0x6b,0x6f,0xc5,0x30,0x01,0x67,0x2b,0xfe,0xd7,0xab,0x76,
            0xca,0x82,0xc9,0x7d,0xfa,0x59,0x47,0xf0,0xad,0xd4,0xa2,0xaf,0x9c,0xa4,0x72,0xc0,
            0xb7,0xfd,0x93,0x26,0x36,0x3f,0xf7,0xcc,0x34,0xa5,0xe5,0xf1,0x71,0xd8,0x31,0x15,
            0x04,0xc7,0x23,0xc3,0x18,0x96,0x05,0x9a,0x07,0x12,0x80,0xe2,0xeb,0x27,0xb2,0x75,
            0x09,0x83,0x2c,0x1a,0x1b,0x6e,0x5a,0xa0,0x52,0x3b,0xd6,0xb3,0x29,0xe3,0x2f,0x84,
            0x53,0xd1,0x00,0xed,0x20,0xfc,0xb1,0x5b,0x6a,0xcb,0xbe,0x39,0x4a,0x4c,0x58,0xcf,
            0xd0,0xef,0xaa,0xfb,0x43,0x4d,0x33,0x85,0x45,0xf9,0x02,0x7f,0x50,0x3c,0x9f,0xa8,
            0x51,0xa3,0x40,0x8f,0x92,0x9d,0x38,0xf5,0xbc,0xb6,0xda,0x21,0x10,0xff,0xf3,0xd2,
            0xcd,0x0c,0x13,0xec,0x5f,0x97,0x44,0x17,0xc4,0xa7,0x7e,0x3d,0x64,0x5d,0x19,0x73,
            0x60,0x81,0x4f,0xdc,0x22,0x2a,0x90,0x88,0x46,0xee,0xb8,0x14,0xde,0x5e,0x0b,0xdb,
            0xe0,0x32,0x3a,0x0a,0x49,0x06,0x24,0x5c,0xc2,0xd3,0xac,0x62,0x91,0x95,0xe4,0x79,
            0xe7,0xc8,0x37,0x6d,0x8d,0xd5,0x4e,0xa9,0x6c,0x56,0xf4,0xea,0x65,0x7a,0xae,0x08,
            0xba,0x78,0x25,0x2e,0x1c,0xa6,0xb4,0xc6,0xe8,0xdd,0x74,0x1f,0x4b,0xbd,0x8b,0x8a,
            0x70,0x3e,0xb5,0x66,0x48,0x03,0xf6,0x0e,0x61,0x35,0x57,0xb9,0x86,0xc1,0x1d,0x9e,
            0xe1,0xf8,0x98,0x11,0x69,0xd9,0x8e,0x94,0x9b,0x1e,0x87,0xe9,0xce,0x55,0x28,0xdf,
            0x8c,0xa1,0x89,0x0d,0xbf,0xe6,0x42,0x68,0x41,0x99,0x2d,0x0f,0xb0,0x54,0xbb,0x16
        };

        private static readonly byte[] Rcon = { 0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80,0x1b,0x36 };

        private static int GfMul(int a, int b)
        {
            int p = 0;
            for (int i = 0; i < 8; i++)
            {
                if ((b & 1) != 0) p ^= a;
                int hi = a & 0x80;
                a = (a << 1) & 0xff;
                if (hi != 0) a ^= 0x1b;
                b >>= 1;
            }
            return p;
        }

        private static byte[] ExpandKey(byte[] key)
        {
            var rk = new byte[176];
            Array.Copy(key, rk, 16);
            for (int i = 4; i < 44; i++)
            {
                int p = (i - 1) * 4;
                byte t0 = rk[p], t1 = rk[p + 1], t2 = rk[p + 2], t3 = rk[p + 3];
                if (i % 4 == 0)
                {
                    // SubWord(RotWord(temp)) ^ Rcon[i/4]; compute all four from the originals.
                    byte o0 = t0, o1 = t1, o2 = t2, o3 = t3;
                    t0 = (byte)(SBox[o1] ^ Rcon[i / 4 - 1]);
                    t1 = SBox[o2];
                    t2 = SBox[o3];
                    t3 = SBox[o0];
                }
                int b4 = (i - 4) * 4;
                rk[i * 4 + 0] = (byte)(rk[b4 + 0] ^ t0);
                rk[i * 4 + 1] = (byte)(rk[b4 + 1] ^ t1);
                rk[i * 4 + 2] = (byte)(rk[b4 + 2] ^ t2);
                rk[i * 4 + 3] = (byte)(rk[b4 + 3] ^ t3);
            }
            return rk;
        }

        private static byte[] AesBlockEncrypt(byte[] rk, byte[] in16)
        {
            // State is column-major: index = r + 4*c (input order matches this).
            var s = new byte[16];
            for (int i = 0; i < 16; i++) s[i] = (byte)(in16[i] ^ rk[i]);

            for (int round = 1; round <= 9; round++)
            {
                for (int i = 0; i < 16; i++) s[i] = SBox[s[i]]; // SubBytes
                var t = new byte[16];                            // ShiftRows
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        t[r + 4 * c] = s[r + 4 * ((c + r) % 4)];
                Array.Copy(t, s, 16);
                MixColumnsInPlace(s);                            // MixColumns
                for (int i = 0; i < 16; i++) s[i] ^= rk[round * 16 + i]; // AddRoundKey
            }

            for (int i = 0; i < 16; i++) s[i] = SBox[s[i]];      // final round: SubBytes
            var t2 = new byte[16];                                 // ShiftRows
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    t2[r + 4 * c] = s[r + 4 * ((c + r) % 4)];
            Array.Copy(t2, s, 16);
            for (int i = 0; i < 16; i++) s[i] ^= rk[160 + i];     // AddRoundKey (no MixColumns)

            return s;
        }

        // Raw AES-128 ECB single-block encrypt. Exposed so the link layer can build a CTR keystream
        // (LL data-channel encryption) from this same NIST-verified block cipher.
        public static byte[] AesEcbBlock(byte[] key, byte[] in16) => AesBlockEncrypt(ExpandKey(key), in16);

        private static void MixColumnsInPlace(byte[] s)
        {
            for (int col = 0; col < 4; col++)
            {
                int b = col * 4;
                byte a0 = s[b], a1 = s[b + 1], a2 = s[b + 2], a3 = s[b + 3];
                s[b]     = (byte)(GfMul(a0, 2) ^ GfMul(a1, 3) ^ a2 ^ a3);
                s[b + 1] = (byte)(a0 ^ GfMul(a1, 2) ^ GfMul(a2, 3) ^ a3);
                s[b + 2] = (byte)(a0 ^ a1 ^ GfMul(a2, 2) ^ GfMul(a3, 3));
                s[b + 3] = (byte)(GfMul(a0, 3) ^ a1 ^ a2 ^ GfMul(a3, 2));
            }
        }

        // One AES-128 ECB block using pre-expanded round keys.
        private static byte[] Enc(byte[] rk, byte[] b16) => AesBlockEncrypt(rk, b16);

        private static byte[] SubBlock(byte[] msg, int i)
        {
            var b = new byte[BlockSize];
            Array.Copy(msg, i * BlockSize, b, 0, BlockSize);
            return b;
        }

        private static byte[] Xor(byte[] a, byte[] b)
        {
            var r = new byte[a.Length];
            for (int i = 0; i < a.Length; i++)
                r[i] = (byte)(a[i] ^ b[i]);
            return r;
        }

        private static void XorInPlace(byte[] a, byte[] b)
        {
            for (int i = 0; i < b.Length; i++)
                a[i] ^= b[i];
        }

        // RFC 4493 dbl(): left-shift the big-endian block by one bit, XOR R=0x87 into the LSB byte on overflow.
        private static byte[] Dbl(byte[] inBlock)
        {
            var a = (byte[])inBlock.Clone();
            int carry = 0;
            for (int i = BlockSize - 1; i >= 0; i--)
            {
                int c = a[i] >> 7;
                a[i] = (byte)((a[i] << 1) | carry);
                carry = c;
            }

            if (carry != 0)
                a[BlockSize - 1] ^= 0x87;
            return a;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (var p in parts)
                total += p.Length;
            var outb = new byte[total];
            int off = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, outb, off, p.Length);
                off += p.Length;
            }

            return outb;
        }

        private static BigInteger FromBe(byte[] b)
        {
            var r = BigInteger.Zero;
            for (int i = 0; i < b.Length; i++)
                r = (r << 8) | b[i];
            return r;
        }

        private static byte[] ToBe32(BigInteger v)
        {
            var outb = new byte[32];
            for (int i = 31; i >= 0; i--)
            {
                outb[i] = (byte)(v & 0xFF);
                v >>= 8;
            }
            return outb;
        }

        // Minimal P-256 (secp256r1) affine point arithmetic over the prime field, for g1.
        private static class P256
        {
            public static readonly BigInteger P = FromHex(
                "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
            public static readonly BigInteger A = P - 3;
            // b is unused in affine double-and-add for a valid point, but kept for completeness.

            public sealed class EcPoint
            {
                public bool AtInfinity;
                public BigInteger X;
                public BigInteger Y;

                public EcPoint() => AtInfinity = true;
                public EcPoint(BigInteger x, BigInteger y) { AtInfinity = false; X = x; Y = y; }
            }

            private static BigInteger Mod(BigInteger v)
            {
                var r = v % P;
                return r.Sign < 0 ? r + P : r;
            }

            private static BigInteger Inv(BigInteger v) => FermatInv(Mod(v));

            // Modular inverse of a mod P via Fermat's little theorem (P prime, a != 0): a^(P-2) mod P.
            private static BigInteger FermatInv(BigInteger a)
            {
                var result = BigInteger.One;
                var b = a % P;
                var e = P - 2;
                while (!e.IsZero)
                {
                    if ((e & BigInteger.One) == BigInteger.One)
                        result = (result * b) % P;
                    b = (b * b) % P;
                    e >>= 1;
                }

                return result;
            }

            public static EcPoint Add(EcPoint p1, EcPoint p2)
            {
                if (p1.AtInfinity)
                    return p2;
                if (p2.AtInfinity)
                    return p1;

                BigInteger x1 = Mod(p1.X), y1 = Mod(p1.Y);
                BigInteger x2 = Mod(p2.X), y2 = Mod(p2.Y);

                if (x1 == x2)
                {
                    if (Mod(y1 + y2).IsZero)
                        return new EcPoint(); // p + (-p) = inf
                    return Double(p1);
                }

                BigInteger lambda = Mod((y2 - y1) * Inv(x2 - x1));
                BigInteger x3 = Mod(lambda * lambda - x1 - x2);
                BigInteger y3 = Mod(lambda * (x1 - x3) - y1);
                return new EcPoint(x3, y3);
            }

            public static EcPoint Double(EcPoint p1)
            {
                if (p1.AtInfinity || Mod(p1.Y).IsZero)
                    return new EcPoint();

                BigInteger x1 = Mod(p1.X), y1 = Mod(p1.Y);
                BigInteger lambda = Mod((3 * x1 * x1 + A) * Inv(2 * y1));
                BigInteger x3 = Mod(lambda * lambda - 2 * x1);
                BigInteger y3 = Mod(lambda * (x1 - x3) - y1);
                return new EcPoint(x3, y3);
            }

            public static EcPoint ScalarMultiply(BigInteger k, EcPoint q)
            {
                EcPoint r = new EcPoint(); // inf
                EcPoint a = q;
                BigInteger kk = Mod256(k);

                for (int i = 0; i < 256; i++)
                {
                    if (((kk >> i) & 1) == 1)
                        r = Add(r, a);
                    a = Double(a);
                }

                return r;
            }

            private static BigInteger Mod256(BigInteger k)
            {
                // Keep only the low 256 bits (a valid P-256 scalar is < n < 2^256).
                return k & (((BigInteger.One << 256) - 1));
            }

            // Parse a big-endian hex string as an UNSIGNED magnitude, MSB-first.
            // (BigInteger.Parse with HexNumber mis-parses strings whose first digit is 8-F as NEGATIVE;
            //  the byte[] ctor's bool param is isBigEndian, not an unsigned flag — so shift in nibbles.)
            private static BigInteger FromHex(string h)
            {
                var r = BigInteger.Zero;
                for (int i = 0; i < h.Length; i += 2)
                    r = (r << 8) | Convert.ToByte(h.Substring(i, 2), 16);
                return r;
            }
        }
    }
}
