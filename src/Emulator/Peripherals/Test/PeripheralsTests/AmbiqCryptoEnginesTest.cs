//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;

using Antmicro.Renode.Peripherals.Crypto;

using NUnit.Framework;

namespace Antmicro.Renode.UnitTests
{
    // Locks the exact-computation engines behind AmbiqApollo510_Crypto to
    // FIPS/NIST reference vectors (AES-ECB FIPS-197 App. B/C, SHA-256
    // FIPS-180-4). A simulated KAT that passes against these vectors
    // passes on silicon too.
    [TestFixture]
    public class AmbiqCryptoEnginesTest
    {
        private static byte[] Hex(string s)
        {
            var b = new byte[s.Length / 2];
            for(int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        private static string HexOf(byte[] b) => BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        [Test]
        public void Aes128EcbEncryptNistVector()
        {
            var ct = AmbiqCryptoEngines.AesEcb(Hex("000102030405060708090a0b0c0d0e0f"),
                Hex("00112233445566778899aabbccddeeff"), false);
            Assert.AreEqual("69c4e0d86a7b0430d8cdb78070b4c55a", HexOf(ct));
        }

        [Test]
        public void Aes192EcbEncryptNistVector()
        {
            var ct = AmbiqCryptoEngines.AesEcb(Hex("000102030405060708090a0b0c0d0e0f1011121314151617"),
                Hex("00112233445566778899aabbccddeeff"), false);
            Assert.AreEqual("dda97ca4864cdfe06eaf70a0ec0d7191", HexOf(ct));
        }

        [Test]
        public void Aes256EcbEncryptNistVector()
        {
            var ct = AmbiqCryptoEngines.AesEcb(Hex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"),
                Hex("00112233445566778899aabbccddeeff"), false);
            Assert.AreEqual("8ea2b7ca516745bfeafc49904b496089", HexOf(ct));
        }

        [Test]
        public void AesEcbDecryptRoundTripAllKeySizes()
        {
            var pt = Hex("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");
            foreach(var keyHex in new[] {
                "000102030405060708090a0b0c0d0e0f",
                "000102030405060708090a0b0c0d0e0f1011121314151617",
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f" })
            {
                var key = Hex(keyHex);
                var ct = AmbiqCryptoEngines.AesEcb(key, pt, false);
                Assert.AreNotEqual(HexOf(pt), HexOf(ct));
                var back = AmbiqCryptoEngines.AesEcb(key, ct, true);
                Assert.AreEqual(HexOf(pt), HexOf(back));
            }
        }

        [Test]
        public void Sha256EmptyVector()
        {
            Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                HexOf(AmbiqCryptoEngines.Sha256(new byte[0])));
        }

        [Test]
        public void Sha256AbcVector()
        {
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                HexOf(AmbiqCryptoEngines.Sha256(System.Text.Encoding.ASCII.GetBytes("abc"))));
        }

        [Test]
        public void Sha256MultiBlockVector()
        {
            var msg = System.Text.Encoding.ASCII.GetBytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq");
            Assert.AreEqual("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
                HexOf(AmbiqCryptoEngines.Sha256(msg)));
        }

        [Test]
        public void Sha256StreamingSplitMatchesOneShot()
        {
            var msg = System.Text.Encoding.ASCII.GetBytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq");
            var st = new AmbiqCryptoEngines.Sha256Stream();
            st.AppendData(msg, 0, 7);
            st.AppendData(msg, 7, msg.Length - 7);
            var hh = st.CurrentHash();
            var digest = new byte[32];
            for(int i = 0; i < 8; i++)
            {
                digest[i * 4 + 0] = (byte)(hh[i] >> 24);
                digest[i * 4 + 1] = (byte)(hh[i] >> 16);
                digest[i * 4 + 2] = (byte)(hh[i] >> 8);
                digest[i * 4 + 3] = (byte)hh[i];
            }
            Assert.AreEqual(HexOf(AmbiqCryptoEngines.Sha256(msg)), HexOf(digest));
        }

        [Test]
        public void Sha256CustomInitStateHonored()
        {
            // Two different staged HASH_H states hashing the same message
            // must yield different digests (proves staged state feeds the
            // engine), and identical staged streams must agree (determinism).
            uint[] stagedA = {
                0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 };
            uint[] stagedB = {
                0x6a09e666, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 };
            var msg = System.Text.Encoding.ASCII.GetBytes("abc");
            var a1 = new AmbiqCryptoEngines.Sha256Stream(stagedA);
            a1.AppendData(msg, 0, msg.Length);
            var a2 = new AmbiqCryptoEngines.Sha256Stream(stagedA);
            a2.AppendData(msg, 0, msg.Length);
            var b = new AmbiqCryptoEngines.Sha256Stream(stagedB);
            b.AppendData(msg, 0, msg.Length);
            var ha1 = a1.CurrentHash();
            var ha2 = a2.CurrentHash();
            var hb = b.CurrentHash();
            for(int i = 0; i < 8; i++)
                Assert.AreEqual(ha2[i], ha1[i]);
            bool differ = false;
            for(int i = 0; i < 8; i++)
                differ |= hb[i] != ha1[i];
            Assert.IsTrue(differ);
        }
    }
}
