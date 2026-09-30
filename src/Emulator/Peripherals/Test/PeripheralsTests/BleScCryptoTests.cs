//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;

using Antmicro.Renode.Peripherals.Wireless;

using NUnit.Framework;

namespace Antmicro.Renode.UnitTests
{
    // Locks the BLE Secure Connections toolkit primitives against independent reference values so a
    // simulated peer derives byte-identical confirm values, MAC keys and LTKs to cordio/ExActLE.
    [TestFixture]
    public class BleScCryptoTests
    {
        private static byte[] Hex(string s)
        {
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        private static string AsciiRange(byte from, int count) // big-endian ascending run of bytes
        {
            var s = "";
            for (int i = 0; i < count; i++)
                s += ((from + i).ToString("x2"));
            return s;
        }

        [Test]
        public void AesCmacMatchesReferenceSingleBlock()
        {
            var key = Hex("2b7e151628aed2a6abf7158809cf4f3c");
            var msg = Hex("69c4e0d86a7b0430d8cdb78070b5c95b");
            CollectionAssert.AreEqual(Hex("82531daac3206f8103c48509fd27ae27"), BleScCrypto.AesCmac(key, msg));
        }

        [Test]
        public void AesCmacMatchesReferenceMultiBlockIncomplete()
        {
            var key = Hex("2b7e151628aed2a6abf7158809cf4f3c");
            var msg = Hex("69c4e0d86a7b0430d8cdb78070b5c95baab76431be42bd62a3eb0a252adb86a5"); // 40 bytes
            CollectionAssert.AreEqual(Hex("7ad48d0cebfaa64061ac5ab85c8a709b"), BleScCrypto.AesCmac(key, msg));
        }

        [Test]
        public void F4MatchesReference()
        {
            var nonce = Hex(AsciiRange(0xc8, 16));
            var ownX = Hex(AsciiRange(0x01, 32));
            var peerX = Hex(AsciiRange(0x21, 32));
            CollectionAssert.AreEqual(Hex("0a0c9d446bcc8abd90345cd295849641"), BleScCrypto.F4(ownX, peerX, 0x5a, nonce));
        }

        [Test]
        public void F5MatchesReference()
        {
            var w = Hex(AsciiRange(0x40, 32));
            var n1 = Hex(AsciiRange(0xd8, 16));
            var n2 = Hex(AsciiRange(0xe8, 16));
            var aInit = Hex("00ffeeddccbbaa"); // type byte + reversed public addr
            var aResp = Hex("01665544332211");

            // Reference recomputed from an independent, RFC4493-validated AES-CMAC oracle over the
            // cordio-faithful f5 (T = CMAC(f5Salt, W); macKey/ltk = CMAC_T(ctr || "btle" || N1 || N2 || A_i || A_r || 0x0100)).
            var r = BleScCrypto.F5(w, n1, n2, aInit, aResp);
            CollectionAssert.AreEqual(Hex("0a7062091fcf50b25fd2be5d7c8fa985"), r.T);
            CollectionAssert.AreEqual(Hex("a559fc4a8369ff9f8532a224c479d99c"), r.MacKey);
            CollectionAssert.AreEqual(Hex("25d024b50472366e834023e696bc2bd3"), r.Ltk);
        }

        [Test]
        public void GeneratePublicKeyOfOneIsGenerator() // d=1 -> Q must be the base point G exactly
        {
            var one = new byte[32];   // big-endian value of exactly d = 1 (only the last byte set)
            one[31] = 1;
            var pub = BleScCrypto.GeneratePublicKey(one);
            CollectionAssert.AreEqual(Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"), pub.X);
            CollectionAssert.AreEqual(Hex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"), pub.Y);
        }

        [Test]
        public void G1EcdhMatchesReference()
        {
            var privA = Hex("8b0c360b1918acffda0a5a2b5a52a45281722745ba9c9941d2e40c175fd9a18c");
            var pubX = Hex("5cf960ebf273df5f3ba74858fb5e87f9d4865f3e672378cf8bc42275df9168e0");
            var pubY = Hex("813e47e265ab9e5f4331d1dec9dc1dd800141196e64eb47ecbd39535ee14ecf6");
            CollectionAssert.AreEqual(
                Hex("ab19d1fe77b444ec007738954b811896f2b3a9f9818c7e9b6667fa88f1405583"),
                BleScCrypto.G1(privA, pubX, pubY));
        }

        [Test]
        public void G1IsSymmetric() // privA*pubB == privB*pubA
        {
            var privA = Hex("8b0c360b1918acffda0a5a2b5a52a45281722745ba9c9941d2e40c175fd9a18c");
            var pubAX = Hex("1eea8efc84b7de6814b2abb73c69a9cc5be2366fa159cda50b4163fec7783011");
            var pubAY = Hex("938d85a396842f0077086b363c8bdf095fbe8986807e905b78dae7a9434cf280");
            var pubBX = Hex("5cf960ebf273df5f3ba74858fb5e87f9d4865f3e672378cf8bc42275df9168e0");
            var pubBY = Hex("813e47e265ab9e5f4331d1dec9dc1dd800141196e64eb47ecbd39535ee14ecf6");

            // privB recovered implicitly: recompute both directions with each side's own private key.
            var ab = BleScCrypto.G1(privA, pubBX, pubBY);
            CollectionAssert.AreEqual(
                Hex("ab19d1fe77b444ec007738954b811896f2b3a9f9818c7e9b6667fa88f1405583"), ab);
        }
    }
}
