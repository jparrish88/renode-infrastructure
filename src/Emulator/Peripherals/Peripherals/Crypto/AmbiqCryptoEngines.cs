//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

namespace Antmicro.Renode.Peripherals.Crypto
{
    // Exact-computation engines for the Ambiq CryptoCell-312 model
    // (AmbiqApollo510_Crypto): AES-ECB (FIPS-197, 128/192/256-bit keys,
    // encrypt + decrypt) and streaming SHA-256 (FIPS-180-4, settable
    // initial state so HASH_H staging is honored).
    //
    // The AES core shares lineage with the NIST-verified AES-128 in
    // Wireless/BleScCrypto.cs (same S-box, column-major state); the key
    // schedule is generalized here to Nk = 4/6/8 words. SHA-256 is new.
    // Locked by AmbiqCryptoEnginesTest (FIPS/NIST vectors).
    public static class AmbiqCryptoEngines
    {
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

        private static readonly byte[] InvSBox = {
            0x52,0x09,0x6a,0xd5,0x30,0x36,0xa5,0x38,0xbf,0x40,0xa3,0x9e,0x81,0xf3,0xd7,0xfb,
            0x7c,0xe3,0x39,0x82,0x9b,0x2f,0xff,0x87,0x34,0x8e,0x43,0x44,0xc4,0xde,0xe9,0xcb,
            0x54,0x7b,0x94,0x32,0xa6,0xc2,0x23,0x3d,0xee,0x4c,0x95,0x0b,0x42,0xfa,0xc3,0x4e,
            0x08,0x2e,0xa1,0x66,0x28,0xd9,0x24,0xb2,0x76,0x5b,0xa2,0x49,0x6d,0x8b,0xd1,0x25,
            0x72,0xf8,0xf6,0x64,0x86,0x68,0x98,0x16,0xd4,0xa4,0x5c,0xcc,0x5d,0x65,0xb6,0x92,
            0x6c,0x70,0x48,0x50,0xfd,0xed,0xb9,0xda,0x5e,0x15,0x46,0x57,0xa7,0x8d,0x9d,0x84,
            0x90,0xd8,0xab,0x00,0x8c,0xbc,0xd3,0x0a,0xf7,0xe4,0x58,0x05,0xb8,0xb3,0x45,0x06,
            0xd0,0x2c,0x1e,0x8f,0xca,0x3f,0x0f,0x02,0xc1,0xaf,0xbd,0x03,0x01,0x13,0x8a,0x6b,
            0x3a,0x91,0x11,0x41,0x4f,0x67,0xdc,0xea,0x97,0xf2,0xcf,0xce,0xf0,0xb4,0xe6,0x73,
            0x96,0xac,0x74,0x22,0xe7,0xad,0x35,0x85,0xe2,0xf9,0x37,0xe8,0x1c,0x75,0xdf,0x6e,
            0x47,0xf1,0x1a,0x71,0x1d,0x29,0xc5,0x89,0x6f,0xb7,0x62,0x0e,0xaa,0x18,0xbe,0x1b,
            0xfc,0x56,0x3e,0x4b,0xc6,0xd2,0x79,0x20,0x9a,0xdb,0xc0,0xfe,0x78,0xcd,0x5a,0xf4,
            0x1f,0xdd,0xa8,0x33,0x88,0x07,0xc7,0x31,0xb1,0x12,0x10,0x59,0x27,0x80,0xec,0x5f,
            0x60,0x51,0x7f,0xa9,0x19,0xb5,0x4a,0x0d,0x2d,0xe5,0x7a,0x9f,0x93,0xc9,0x9c,0xef,
            0xa0,0xe0,0x3b,0x4d,0xae,0x2a,0xf5,0xb0,0xc8,0xeb,0xbb,0x3c,0x83,0x53,0x99,0x61,
            0x17,0x2b,0x04,0x7e,0xba,0x77,0xd6,0x26,0xe1,0x69,0x14,0x63,0x55,0x21,0x0c,0x7d
        };

        private static readonly byte[] Rcon = { 0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80,0x1b,0x36 };

        private static int GfMul(int a, int b)
        {
            int p = 0;
            for(int i = 0; i < 8; i++)
            {
                if((b & 1) != 0) p ^= a;
                int hi = a & 0x80;
                a = (a << 1) & 0xff;
                if(hi != 0) a ^= 0x1b;
                b >>= 1;
            }
            return p;
        }

        private static byte[] ExpandKey(byte[] key)
        {
            int nk = key.Length / 4;          // 4 / 6 / 8 words
            int nr = nk + 6;                  // 10 / 12 / 14 rounds
            var rk = new byte[(nr + 1) * 16];
            Array.Copy(key, rk, key.Length);
            int totalWords = (nr + 1) * 4;
            for(int i = nk; i < totalWords; i++)
            {
                int p = (i - 1) * 4;
                byte t0 = rk[p], t1 = rk[p + 1], t2 = rk[p + 2], t3 = rk[p + 3];
                if(i % nk == 0)
                {
                    byte o0 = t0, o1 = t1, o2 = t2, o3 = t3;
                    t0 = (byte)(SBox[o1] ^ Rcon[i / nk - 1]);
                    t1 = SBox[o2];
                    t2 = SBox[o3];
                    t3 = SBox[o0];
                }
                else if(nk > 6 && i % nk == 4)
                {
                    t0 = SBox[t0]; t1 = SBox[t1]; t2 = SBox[t2]; t3 = SBox[t3];
                }
                int b4 = (i - nk) * 4;
                rk[i * 4 + 0] = (byte)(rk[b4 + 0] ^ t0);
                rk[i * 4 + 1] = (byte)(rk[b4 + 1] ^ t1);
                rk[i * 4 + 2] = (byte)(rk[b4 + 2] ^ t2);
                rk[i * 4 + 3] = (byte)(rk[b4 + 3] ^ t3);
            }
            return rk;
        }

        private static void AddRoundKey(byte[] s, byte[] rk, int off)
        {
            for(int i = 0; i < 16; i++) s[i] ^= rk[off + i];
        }

        private static void SubBytes(byte[] s)
        {
            for(int i = 0; i < 16; i++) s[i] = SBox[s[i]];
        }

        private static void InvSubBytes(byte[] s)
        {
            for(int i = 0; i < 16; i++) s[i] = InvSBox[s[i]];
        }

        private static void ShiftRows(byte[] s)
        {
            var t = new byte[16];
            for(int r = 0; r < 4; r++)
                for(int c = 0; c < 4; c++)
                    t[r + 4 * c] = s[r + 4 * ((c + r) % 4)];
            Array.Copy(t, s, 16);
        }

        private static void InvShiftRows(byte[] s)
        {
            var t = new byte[16];
            for(int r = 0; r < 4; r++)
                for(int c = 0; c < 4; c++)
                    t[r + 4 * c] = s[r + 4 * ((c - r + 4) % 4)];
            Array.Copy(t, s, 16);
        }

        private static void MixColumns(byte[] s)
        {
            for(int col = 0; col < 4; col++)
            {
                int b = col * 4;
                byte a0 = s[b], a1 = s[b + 1], a2 = s[b + 2], a3 = s[b + 3];
                s[b]     = (byte)(GfMul(a0, 2) ^ GfMul(a1, 3) ^ a2 ^ a3);
                s[b + 1] = (byte)(a0 ^ GfMul(a1, 2) ^ GfMul(a2, 3) ^ a3);
                s[b + 2] = (byte)(a0 ^ a1 ^ GfMul(a2, 2) ^ GfMul(a3, 3));
                s[b + 3] = (byte)(GfMul(a0, 3) ^ a1 ^ a2 ^ GfMul(a3, 2));
            }
        }

        private static void InvMixColumns(byte[] s)
        {
            for(int col = 0; col < 4; col++)
            {
                int b = col * 4;
                byte a0 = s[b], a1 = s[b + 1], a2 = s[b + 2], a3 = s[b + 3];
                s[b]     = (byte)(GfMul(a0, 0x0e) ^ GfMul(a1, 0x0b) ^ GfMul(a2, 0x0d) ^ GfMul(a3, 0x09));
                s[b + 1] = (byte)(GfMul(a0, 0x09) ^ GfMul(a1, 0x0e) ^ GfMul(a2, 0x0b) ^ GfMul(a3, 0x0d));
                s[b + 2] = (byte)(GfMul(a0, 0x0d) ^ GfMul(a1, 0x09) ^ GfMul(a2, 0x0e) ^ GfMul(a3, 0x0b));
                s[b + 3] = (byte)(GfMul(a0, 0x0b) ^ GfMul(a1, 0x0d) ^ GfMul(a2, 0x09) ^ GfMul(a3, 0x0e));
            }
        }

        // AES-ECB single block. State is column-major: index = r + 4*c,
        // matching bus order for the CC312 key/data registers.
        public static byte[] AesEcbBlock(byte[] key, byte[] block16, bool decrypt)
        {
            if(key == null || (key.Length != 16 && key.Length != 24 && key.Length != 32))
                throw new ArgumentException("key must be 128/192/256 bits", nameof(key));
            if(block16 == null || block16.Length != 16)
                throw new ArgumentException("block must be 16 bytes", nameof(block16));
            int nr = key.Length / 4 + 6;
            var rk = ExpandKey(key);
            var s = new byte[16];
            if(!decrypt)
            {
                Array.Copy(block16, s, 16);
                AddRoundKey(s, rk, 0);
                for(int round = 1; round < nr; round++)
                {
                    SubBytes(s); ShiftRows(s); MixColumns(s);
                    AddRoundKey(s, rk, round * 16);
                }
                SubBytes(s); ShiftRows(s);
                AddRoundKey(s, rk, nr * 16);
            }
            else
            {
                Array.Copy(block16, s, 16);
                AddRoundKey(s, rk, nr * 16);
                for(int round = nr - 1; round >= 1; round--)
                {
                    InvShiftRows(s); InvSubBytes(s);
                    AddRoundKey(s, rk, round * 16);
                    InvMixColumns(s);
                }
                InvShiftRows(s); InvSubBytes(s);
                AddRoundKey(s, rk, 0);
            }
            return s;
        }

        // AES-ECB over a whole buffer (length must be a multiple of 16).
        public static byte[] AesEcb(byte[] key, byte[] data, bool decrypt)
        {
            if(data == null || data.Length == 0 || (data.Length % 16) != 0)
                throw new ArgumentException("data length must be a non-zero multiple of 16", nameof(data));
            var rkOut = new byte[data.Length];
            var blk = new byte[16];
            for(int off = 0; off < data.Length; off += 16)
            {
                Array.Copy(data, off, blk, 0, 16);
                var enc = AesEcbBlock(key, blk, decrypt);
                Array.Copy(enc, 0, rkOut, off, 16);
            }
            return rkOut;
        }

        private static readonly uint[] Sha256K = {
            0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
            0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
            0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
            0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
            0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
            0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
            0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
            0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
        };

        public static readonly uint[] Sha256IV = {
            0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19
        };

        private static uint RoR(uint x, int n) => (x >> n) | (x << (32 - n));

        // Streaming SHA-256 (FIPS-180-4). Construct with the staged HASH_H
        // state (default = standard IV), AppendData() per DIN chunk,
        // CurrentHash() for digest reads (finalizes a copy; stream stays live).
        public sealed class Sha256Stream
        {
            private readonly uint[] h = new uint[8];
            private readonly byte[] buf = new byte[64];
            private int bufUsed;
            private ulong bitLen;

            public Sha256Stream(uint[] initH = null)
            {
                Reset(initH);
            }

            public void Reset(uint[] initH = null)
            {
                var iv = initH ?? Sha256IV;
                Array.Copy(iv, h, 8);
                Array.Clear(buf, 0, buf.Length);
                bufUsed = 0;
                bitLen = 0;
            }

            public void AppendData(byte[] data, int offset, int count)
            {
                bitLen += (ulong)count * 8;
                while(count > 0)
                {
                    int take = Math.Min(64 - bufUsed, count);
                    Array.Copy(data, offset, buf, bufUsed, take);
                    bufUsed += take; offset += take; count -= take;
                    if(bufUsed == 64)
                    {
                        Compress(h, buf, 0);
                        bufUsed = 0;
                    }
                }
            }

            public uint[] CurrentHash()
            {
                var hh = (uint[])h.Clone();
                var tail = new byte[64];
                Array.Copy(buf, tail, bufUsed);
                ulong savedBits = bitLen;
                int used = bufUsed;
                tail[used++] = 0x80;
                if(used > 56)
                {
                    while(used < 64) tail[used++] = 0;
                    Compress(hh, tail, 0);
                    Array.Clear(tail, 0, tail.Length);
                    used = 0;
                }
                while(used < 56) tail[used++] = 0;
                ulong bl = savedBits;
                for(int i = 0; i < 8; i++)
                {
                    tail[56 + i] = (byte)(bl >> (56 - 8 * i));
                }
                Compress(hh, tail, 0);
                return hh;
            }

            internal static void Compress(uint[] hh, byte[] block, int off)
            {
                uint[] w = new uint[64];
                for(int i = 0; i < 16; i++)
                    w[i] = (uint)(block[off + i * 4] << 24 | block[off + i * 4 + 1] << 16 | block[off + i * 4 + 2] << 8 | block[off + i * 4 + 3]);
                for(int i = 16; i < 64; i++)
                {
                    uint s0 = RoR(w[i - 15], 7) ^ RoR(w[i - 15], 18) ^ (w[i - 15] >> 3);
                    uint s1 = RoR(w[i - 2], 17) ^ RoR(w[i - 2], 19) ^ (w[i - 2] >> 10);
                    w[i] = w[i - 16] + s0 + w[i - 7] + s1;
                }
                uint a = hh[0], b = hh[1], c = hh[2], d = hh[3];
                uint e = hh[4], f = hh[5], g = hh[6], hh7 = hh[7];
                for(int i = 0; i < 64; i++)
                {
                    uint S1 = RoR(e, 6) ^ RoR(e, 11) ^ RoR(e, 25);
                    uint ch = (e & f) ^ (~e & g);
                    uint t1 = hh7 + S1 + ch + Sha256K[i] + w[i];
                    uint S0 = RoR(a, 2) ^ RoR(a, 13) ^ RoR(a, 22);
                    uint maj = (a & b) ^ (a & c) ^ (b & c);
                    uint t2 = S0 + maj;
                    hh7 = g; g = f; f = e; e = d + t1;
                    d = c; c = b; b = a; a = t1 + t2;
                }
                hh[0] += a; hh[1] += b; hh[2] += c; hh[3] += d;
                hh[4] += e; hh[5] += f; hh[6] += g; hh[7] += hh7;
            }
        }

        // One-shot SHA-256 convenience.
        public static byte[] Sha256(byte[] data)
        {
            var st = new Sha256Stream();
            st.AppendData(data, 0, data.Length);
            var hh = st.CurrentHash();
            var digest = new byte[32];
            for(int i = 0; i < 8; i++)
            {
                digest[i * 4 + 0] = (byte)(hh[i] >> 24);
                digest[i * 4 + 1] = (byte)(hh[i] >> 16);
                digest[i * 4 + 2] = (byte)(hh[i] >> 8);
                digest[i * 4 + 3] = (byte)hh[i];
            }
            return digest;
        }
    }
}
