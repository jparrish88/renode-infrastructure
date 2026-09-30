//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Peripherals.Wireless;

using NUnit.Framework;

namespace Antmicro.Renode.PeripheralsTests
{
    [TestFixture]
    public class AmbiqEM9305Test
    {
        private Machine machine;
        private AmbiqEM9305 peripheral;

        [SetUp]
        public void Setup()
        {
            machine = new Machine();
            peripheral = new AmbiqEM9305(machine);
        }

        // tx_starts: host clocks [0x42][0x00]; radio answers MISO[0]=0xC0 and MISO[1]=free_space.
        [Test]
        public void TxHeaderReturnsStsReadyThenFreeSpace()
        {
            Assert.AreEqual(0xC0, peripheral.Transmit(0x42), "byte[0] of status reply must be STS ready");

            var freeSpace = peripheral.Transmit(0x00);
            Assert.Greater(freeSpace, 0, "byte[1] (free space) should be non-zero");
        }

        // A byte that is not a valid header while idle must not corrupt state.
        [Test]
        public void IdleGarbageByteLeavesStateIdle()
        {
            var r = peripheral.Transmit(0x55);   // neither 0x42 nor 0x81
            Assert.AreEqual(0x00, r);

            peripheral.FinishTransmission();     // end the stray transaction

            // A subsequent valid header must still be recognised (fresh transaction).
            Assert.AreEqual(0xC0, peripheral.Transmit(0x42));
        }

        // Full round trip: a standard HCI command is processed on the TX path and the
        // resulting Command Complete event can then be read back over the RX handshake.
        [Test]
        public void TxCommandGeneratesReadableResponse()
        {
            var opcode = 0x0045;   // LE controller OGF, not vendor specific
            byte[] cmd = { 0x01, (byte)(opcode & 0xFF), (byte)((opcode >> 8) & 0xFF), 0x00 };

            TxHandshake();
            SendCommand(cmd);

            var resp = ReadResponse();

            Assert.AreEqual(7, resp.Count, "no-data CC is 3 header + 4 params");
            Assert.AreEqual(0x04, resp[0], "event type");
            Assert.AreEqual(0x0E, resp[1], "command complete event code");
            Assert.AreEqual(0x04, resp[2], "plen = numHci+opcode+status = 4");
            Assert.AreEqual(0x01, resp[3], "num_hci_command_packets");
            Assert.AreEqual((byte)(opcode & 0xFF), resp[4], "echoed op_lo");
            Assert.AreEqual((byte)((opcode >> 8) & 0xFF), resp[5], "echoed op_hi");
            Assert.AreEqual(0x00, resp[6], "status must be success");
        }

        // Read BD Address returns the stored public address with a data payload.
        [Test]
        public void ReadBdAddressReturnsSixBytePayload()
        {
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x09, 0x10, 0x00 });   // opcode 0x1009

            var resp = ReadResponse();

            Assert.AreEqual(13, resp.Count, "CC + status + 6 addr bytes");
            Assert.AreEqual(0x0A, resp[2], "plen = 4 + 6 data");
            Assert.AreEqual(0x09, resp[4]);
            Assert.AreEqual(0x10, resp[5]);
            Assert.AreEqual(0x00, resp[6], "status success");

            byte[] expected = { 0x02, 0x1F, 0xDE, 0xAD, 0xBE, 0xEF };   // default address
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(expected[i], resp[7 + i], "addr byte " + i);
            }
        }

        // VSC SET_DEV_PUB_ADDR stores the address which Read BD Address then echoes.
        [Test]
        public void SetDevPubAddrIsEchoedByReadBdAddress()
        {
            TxHandshake();
            byte[] addr = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
            var setCmd = new List<byte> { 0x01, 0x43, 0xFC, 0x06 };   // VSC 0xFC43, param_len=6
            setCmd.AddRange(addr);
            SendCommand(setCmd.ToArray());

            ReadResponse();   // drain the SetDevPubAddr Command Complete before issuing the next command

            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x09, 0x10, 0x00 });       // Read BD Address
            var resp = ReadResponse();

            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(addr[i], resp[7 + i], "echoed addr byte " + i);
            }
        }

        // Vendor specific commands (OGF = 0x3F) take the VSC path and still echo in a Command Complete.
        [Test]
        public void VendorSpecificOpcodeIsEchoed()
        {
            ushort vscOpcode = 0xF800;   // OGF=63, OOC=0
            TxHandshake();
            SendCommand(new byte[] { 0x01, (byte)(vscOpcode & 0xFF), (byte)((vscOpcode >> 8) & 0xFF), 0x00 });

            var resp = ReadResponse();
            Assert.AreEqual((byte)((vscOpcode >> 8) & 0xFF), resp[5], "VSC echo op_hi");
        }

        // Firmware update: WRITE_AT_ADDRESS (0xFD03) stores bytes into NVM, and READ_AT_ADDRESS
        // (0xFD01) returns exactly those stored bytes.
        [Test]
        public void WriteAtAddressThenReadBackReturnsStoredBytes()
        {
            const uint addr = 0x0031_0000;
            byte[] data = { 0xDE, 0xAD, 0xBE, 0xEF };

            // WRITE: [type][op_lo=03][op_hi=FD][plen=4+len][addr u32 LE][data...]
            var writeCmd = new List<byte> { 0x01, 0x03, 0xFD, (byte)(4 + data.Length) };
            AddU32Le(writeCmd, addr);
            writeCmd.AddRange(data);

            TxHandshake();
            SendCommand(writeCmd.ToArray());
            ReadResponse();   // drain the write Command Complete

            // READ: [type][op_lo=01][op_hi=FD][plen=5][addr u32 LE][len]
            var readCmd = new List<byte> { 0x01, 0x01, 0xFD, 0x05 };
            AddU32Le(readCmd, addr);
            readCmd.Add((byte)data.Length);

            TxHandshake();
            SendCommand(readCmd.ToArray());
            var resp = ReadResponse();

            Assert.AreEqual(7 + data.Length, resp.Count, "CC (7) + stored payload");
            Assert.AreEqual(0x00, resp[6], "status success");
            for (int i = 0; i < data.Length; i++)
            {
                Assert.AreEqual(data[i], resp[7 + i], "stored byte " + i);
            }
        }

        // Firmware update: CRC_CALCULATE (0xFC4E) returns the IEEE CRC-32 of a [start,end) NVM range.
        // Verified against the standard check value CRC-32("123456789") = 0xCBF43926.
        [Test]
        public void CrcCalculateMatchesStandardVector()
        {
            const uint start = 0x0030_0000;
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");

            var writeCmd = new List<byte> { 0x01, 0x03, 0xFD, (byte)(4 + data.Length) };
            AddU32Le(writeCmd, start);
            writeCmd.AddRange(data);
            TxHandshake();
            SendCommand(writeCmd.ToArray());
            ReadResponse();   // drain

            uint end = start + (uint)data.Length;
            var crcCmd = new List<byte> { 0x01, 0x4E, 0xFC, 0x08 };   // VSC 0xFC4E, plen=8
            AddU32Le(crcCmd, start);
            AddU32Le(crcCmd, end);

            TxHandshake();
            SendCommand(crcCmd.ToArray());
            var resp = ReadResponse();

            Assert.AreEqual(11, resp.Count, "CC (7) + 4 CRC bytes");
            Assert.AreEqual(0x00, resp[6], "status success");
            // 0xCBF43926 in little-endian byte order.
            Assert.AreEqual(0x26, resp[7]);
            Assert.AreEqual(0x39, resp[8]);
            Assert.AreEqual(0xF4, resp[9]);
            Assert.AreEqual(0xCB, resp[10]);
        }

        // Firmware update: a command larger than one advertised burst (a 256-byte WRITE_AT_ADDRESS)
        // is split by the host into two data transactions (255 + 1), each preceded by its own [0x42][0x00]
        // handshake. The model must re-assemble it and store all bytes, including the one sent last.
        [Test]
        public void OversizedCommandSplitAcrossBurstsIsReassembled()
        {
            const uint addr = 0x0031_1000;
            const int dataLen = 248;   // total packet = 1+2+1+4+248 = 256 bytes

            var cmd = new List<byte> { 0x01, 0x03, 0xFD, (byte)(4 + dataLen) };
            AddU32Le(cmd, addr);
            for (int i = 0; i < dataLen; i++)
            {
                cmd.Add((byte)i);      // data[i] == i
            }

            cmd[8] = 0x5A;   // marker at data offset 0
            int totalBytes = cmd.Count;   // 256
            Assert.AreEqual(256, totalBytes, "max-size write command");

            const int firstBurst = 255;   // what a single-byte free-space field can advertise

            TxHandshake();                            // handshake for burst 1
            SendDataBurst(cmd, 0, firstBurst);        // first 255 bytes (one IOM transaction)

            TxHandshake();                            // continuation handshake (buffer must NOT be cleared)
            SendDataBurst(cmd, firstBurst, totalBytes - firstBurst);   // final byte

            ReadResponse();                           // drain the write's Command Complete before reading back

            // Read back data[0] (first burst) and data[dataLen-1] (the last byte, second burst).
            Assert.AreEqual((byte)0x5A, ReadNvmByte(addr), "data offset 0 (burst 1)");
            Assert.AreEqual(cmd[totalBytes - 1], ReadNvmByte(addr + (uint)(dataLen - 1)), "last data byte (burst 2)");
        }

        // Standard LE advertising: Set_Ad_Data then Set_Ad_Enable must cause an over-the-air ADV_Ind to be emitted,
        // carrying the configured advertising data (here a complete local name "A510").
        [Test]
        public void SetAdDataThenEnableEmitsOverTheAirAdvInd()
        {
            byte[] air = null;
            peripheral.AirFrameSent += f => { air = (byte[])f.Clone(); };

            // Advertising data: Flags(len=2) + Complete Local Name "A510" (type 0x09, len = 1+4 = 5).
            var adData = new byte[] { 0x02, 0x01, 0x06, 0x05, 0x09, 0x41, 0x35, 0x31, 0x30 };

            // HCI_LE_Set_Ad_Data (0x2008): [type][op_lo=08][op_hi=20][plen][adData...]
            TxHandshake();
            var setAd = new List<byte> { 0x01, 0x08, 0x20, (byte)adData.Length };
            setAd.AddRange(adData);
            SendCommand(setAd.ToArray());

            // HCI_LE_Set_Ad_Enable (0x200A): [type][op_lo=0A][op_hi=20][plen=1][enable=1] -> emits over the air.
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x0A, 0x20, 0x01, 0x01 });

            Assert.IsNotNull(air, "Set_Ad_Enable must emit an over-the-air frame");

            // BLESniffer framing: [wsIdx][sigPwr][noisePwr][aaOffenses][refAA x4][flags u16] then the raw air PDU.
            Assert.AreEqual((byte)0x3f, air[8]);   // base flags 0x3C3F (an advertisement adds no PDU bit)
            Assert.AreEqual((byte)0x3c, air[9]);

            var pdu = air.Skip(10).ToArray();      // raw over-the-air ADV_Ind bytes
            CollectionAssert.AreEqual(new byte[] { 0xD6, 0xBE, 0x89, 0x8E }, pdu.Take(4), "advertising access address");
            Assert.AreEqual((pdu[4] & 0x0F), 0, "PDU type must be ADV_IND (legacy connectable undirected)");

            // The advertising data region must carry the complete local name "A510".
            Assert.IsTrue(Contains(pdu, new byte[] { 0x41, 0x35, 0x31, 0x30 }), "adv data must contain local name A510");
        }

        // Set_Ad_Data alone (no enable) emits nothing over the air.
        [Test]
        public void SetAdDataWithoutEnableEmitsNothing()
        {
            byte[] air = null;
            peripheral.AirFrameSent += f => { air = (byte[])f.Clone(); };

            var adData = new byte[] { 0x02, 0x01, 0x06 };
            TxHandshake();
            var setAd = new List<byte> { 0x01, 0x08, 0x20, (byte)adData.Length };
            setAd.AddRange(adData);
            SendCommand(setAd.ToArray());

            Assert.IsNull(air, "no over-the-air frame should be emitted before Set_Ad_Enable");
        }

        // HCI_LE_ENCRYPT (0x2017, the cordio HCI_OCF_LE_ENCRYPT value): the model must run AES-128 over
        // [encryption_key(16)][plaintext(16)] and return the 16-byte ciphertext in a Command Complete so cordio
        // can complete pairing / re-encryption. Verified against the canonical AES-128 ECB vector (key=00..0F, pt=00 11 22 .. EE FF).
        [Test]
        public void LeEncryptReturnsAes128CiphertextKnownVector()
        {
            byte[] key   = { 0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F };
            byte[] plain = { 0x00,0x11,0x22,0x33,0x44,0x55,0x66,0x77,0x88,0x99,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF };

            var cmd = new List<byte> { 0x01, 0x17, 0x20, (byte)(key.Length + plain.Length) };   // plen = 32
            cmd.AddRange(key);
            cmd.AddRange(plain);

            TxHandshake();
            SendCommand(cmd.ToArray());
            var resp = ReadResponse();

            Assert.AreEqual(23, resp.Count, "CC header (7) + 16-byte ciphertext");
            Assert.AreEqual((byte)0x04, resp[0], "event type");
            Assert.AreEqual((byte)0x0E, resp[1], "command complete event code");
            Assert.AreEqual((byte)(4 + 16), resp[2], "plen = numHci+opcode+status+data");
            Assert.AreEqual((byte)0x01, resp[3], "num_hci_command_packets");
            Assert.AreEqual((byte)0x17, resp[4], "echoed op_lo");
            Assert.AreEqual((byte)0x20, resp[5], "echoed op_hi");
            Assert.AreEqual((byte)0x00, resp[6], "status must be success");

            byte[] expected = { 0x69,0xC4,0xE0,0xD8,0x6A,0x7B,0x04,0x30,0xD8,0xCD,0xB7,0x80,0x70,0xB4,0xC5,0x5A };
            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(expected[i], resp[7 + i], "cipher byte " + i);
            }
        }

        // Second independent AES-128 vector to guard against a coincidental match. Cross-checked against OpenSSL
        // (aes-128-ecb -nopad) for the same key/plaintext, so it also validates Renode's AesProvider against a second implementation.
        [Test]
        public void LeEncryptMatchesSecondAesVector()
        {
            byte[] key   = { 0x2b,0x7e,0x15,0x16,0x28,0xae,0xd2,0xa6,0xab,0xf7,0x15,0x88,0x09,0xcf,0x4f,0x3c };
            byte[] plain = { 0x6b,0xc1,0xbe,0xe2,0x2e,0x40,0x9f,0x96,0xe9,0x3c,0xd7,0x43,0x5e,0x39,0x2d,0x3e };
            byte[] expected = { 0xD4,0x71,0x6B,0xDF,0xA2,0x52,0x11,0xEB,0xC3,0x46,0xBA,0xD5,0xFC,0x21,0x24,0x35 };

            var cmd = new List<byte> { 0x01, 0x17, 0x20, (byte)(key.Length + plain.Length) };
            cmd.AddRange(key);
            cmd.AddRange(plain);

            TxHandshake();
            SendCommand(cmd.ToArray());
            var resp = ReadResponse();

            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(expected[i], resp[7 + i], "cipher byte " + i);
            }
        }

        // Pairing-relevant command path: a central that has completed SMP issues Read_Remote_Features and
        // LE_Random during the feature/random exchange, then drives HCI_LE_ENCRYPT with the derived key. The model
        // must answer each correctly; LE_Encrypt's ciphertext is checked against the canonical AES-128 ECB vector so
        // the pairing crypto endpoint is proven end to end without a second radio.
        [Test]
        public void PairingEncryptionCommandsAreAnswered()
        {
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x16, 0x20, 0x02, 0x00, 0x00 });   // Read_Remote_Features (opcode 0x2016): connHandle(2)
            var feat = ReadResponse();
            Assert.AreEqual(14, feat.Count, "CC header (7) + 7 feature bytes");
            Assert.AreEqual((byte)0x00, feat[6], "status success");
            Assert.AreEqual((byte)0x16, feat[4]);   // op_lo echoed
            Assert.AreEqual((byte)0x20, feat[5]);   // op_hi echoed

            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x18, 0x20, 0x00 });               // LE_Rand (correct opcode 0x2018)
            var rnd = ReadResponse();
            Assert.AreEqual(15, rnd.Count, "CC header (7) + 8 random bytes");
            Assert.AreEqual((byte)0x00, rnd[6], "status success");
            Assert.AreEqual((byte)0x18, rnd[4]);   // op_lo echoed

            byte[] key      = { 0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F };
            byte[] plain    = { 0x00,0x11,0x22,0x33,0x44,0x55,0x66,0x77,0x88,0x99,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF };
            byte[] expected = { 0x69,0xC4,0xE0,0xD8,0x6A,0x7B,0x04,0x30,0xD8,0xCD,0xB7,0x80,0x70,0xB4,0xC5,0x5A };
            var enc = new List<byte> { 0x01, 0x17, 0x20, (byte)(key.Length + plain.Length) };   // LE_Encrypt (opcode 0x2017)
            enc.AddRange(key);
            enc.AddRange(plain);

            TxHandshake();
            SendCommand(enc.ToArray());
            var resp = ReadResponse();
            Assert.AreEqual(23, resp.Count, "CC header (7) + 16-byte ciphertext");
            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(expected[i], resp[7 + i], "cipher byte " + i);
            }
        }

        // LE_Read_Local_P256_Pub_Key (opcode 0x2025): the model must return its ephemeral P-256 public key as a
        // 64-byte X||Y whose point actually lies on the NIST P-256 curve, and it must be stable per instance.
        [Test]
        public void ReadLocalP256PubKeyReturnsPointOnCurve()
        {
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x25, 0x20, 0x00 });
            var resp = ReadResponse();

            Assert.AreEqual(7 + 64, resp.Count, "CC header (7) + 64-byte public key");
            Assert.AreEqual((byte)0x0E, resp[1], "command complete event code");
            Assert.AreEqual((byte)(4 + 64), resp[2], "plen = numHci+opcode+status+data");
            Assert.AreEqual((byte)0x25, resp[4], "op_lo echoed");
            Assert.AreEqual((byte)0x20, resp[5], "op_hi echoed");
            Assert.AreEqual((byte)0x00, resp[6], "status success");

            var pub = new byte[64];
            for (int i = 0; i < 64; i++) pub[i] = resp[7 + i];
            Assert.IsTrue(IsP256PubKey(pub), "returned point must satisfy y^2 = x^3 - 3x + b mod p");

            // The ephemeral key is generated once per controller and then cached, so a second read matches.
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x25, 0x20, 0x00 });
            var resp2 = ReadResponse();
            for (int i = 0; i < 64; i++)
            {
                Assert.AreEqual(pub[i], resp2[7 + i], "ephemeral public key must be stable per instance");
            }
        }

        // LE_Generate_DHKey (opcode 0x2026): given the peer's public point R = e*G, the model must return
        // W = g1(localPriv, R). By ECDH bilinearity d*(e*G) == e*(d*G), so this equals an independent g1(e, Q)
        // computed in-test from the model's own public point Q. Proves the command plumbing AND that the model
        // uses its private scalar consistently -- no air link required.
        [Test]
        public void GenerateDhkeyAgreesWithIndependentEcdh()
        {
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x25, 0x20, 0x00 });   // fetch the model's own ephemeral pub Q = d*G
            var qResp = ReadResponse();

            var qx = new byte[32];
            var qy = new byte[32];
            for (int i = 0; i < 32; i++) { qx[i] = qResp[7 + i]; qy[i] = qResp[39 + i]; }

            // Peer: a small fixed private scalar e and its public point R = e*G, computed independently here.
            var e = new byte[32]; e[0] = 0x05;                     // e = 5 (valid non-zero P-256 scalar)
            var r = BleScCrypto.GeneratePublicKey(e);

            var cmd = new List<byte> { 0x01, 0x26, 0x20, 0x40 };   // plen = 64
            for (int i = 0; i < 32; i++) cmd.Add(r.X[i]);
            for (int i = 0; i < 32; i++) cmd.Add(r.Y[i]);

            TxHandshake();
            SendCommand(cmd.ToArray());
            var wResp = ReadResponse();

            Assert.AreEqual(7 + 32, wResp.Count, "CC header (7) + 32-byte W");
            Assert.AreEqual((byte)0x26, wResp[4], "op_lo echoed");
            Assert.AreEqual((byte)0x00, wResp[6], "status success");

            var wModel = new byte[32];
            for (int i = 0; i < 32; i++) wModel[i] = wResp[7 + i];

            CollectionAssert.AreEqual(BleScCrypto.G1(e, qx, qy), wModel, "model g1(localPriv, peerPub) must equal the independent ECDH");
        }

        // LE_Start_Encryption (opcode 0x2019): after storing the supplied LTK the model answers Command Complete
        // and emits an async Encryption Changed LE-meta event carrying [conn_handle][status=OK][enc_mode=1].
        [Test]
        public void StartEncryptionCompletesAndEmitsEncryptionChanged()
        {
            var cmd = new List<byte> { 0x01, 0x19, 0x20, (byte)28 };   // handle(2)+ltk(16)+rand(8)+ediv(2)
            cmd.Add(0x42); cmd.Add(0x00);                              // conn_handle LE = 0x0042
            for (int i = 0; i < 16; i++) cmd.Add((byte)(0xA0 + i));     // ltk
            for (int i = 0; i < 8; i++) cmd.Add((byte)i);               // rand
            cmd.Add(0x07); cmd.Add(0x00);                              // ediv

            TxHandshake();
            SendCommand(cmd.ToArray());
            var resp = ReadResponse();   // one call drains CC (7 bytes) + Encryption Changed meta (8 bytes).

            Assert.AreEqual(15, resp.Count, "CC header (7) + Encryption Changed event (8)");

            // Command Complete half:
            Assert.AreEqual((byte)0x04, resp[0], "event type");
            Assert.AreEqual((byte)0x0E, resp[1], "command complete event code");
            Assert.AreEqual((byte)0x19, resp[4], "op_lo echoed");
            Assert.AreEqual((byte)0x20, resp[5], "op_hi echoed");
            Assert.AreEqual((byte)0x00, resp[6], "status success");

            // Encryption Changed LE-meta half: [type=04][evt=3E][plen=05][sub=08][handle(2)][status][mode]
            Assert.AreEqual((byte)0x04, resp[7], "meta event type");
            Assert.AreEqual((byte)0x3E, resp[8], "LE meta event code");
            Assert.AreEqual((byte)0x05, resp[9], "plen = subevent(1)+payload(4)");
            Assert.AreEqual((byte)0x08, resp[10], "subevent = Encryption Changed");
            Assert.AreEqual((byte)0x42, resp[11], "conn_handle low byte echoed");
            Assert.AreEqual((byte)0x00, resp[12], "conn_handle high byte echoed");
            Assert.AreEqual((byte)0x00, resp[13], "encryption status success");
            Assert.AreEqual((byte)0x01, resp[14], "encryption mode enabled");
        }

        // LE_LTK_Request_Reply (opcode 0x201A): the controller is supplied the LTK and answers with a plain
        // Command Complete (no return data). Covers the pairing-complete / re-encryption LTK delivery path.
        [Test]
        public void LtkRequestReplyCompletesWithSuccess()
        {
            var cmd = new List<byte> { 0x01, 0x1A, 0x20, (byte)18 };   // handle(2)+ltk(16)
            cmd.Add(0x42); cmd.Add(0x00);
            for (int i = 0; i < 16; i++) cmd.Add((byte)i);

            TxHandshake();
            SendCommand(cmd.ToArray());
            var resp = ReadResponse();

            Assert.AreEqual(7, resp.Count, "CC header only");
            Assert.AreEqual((byte)0x1A, resp[4], "op_lo echoed");
            Assert.AreEqual((byte)0x20, resp[5], "op_hi echoed");
            Assert.AreEqual((byte)0x00, resp[6], "status success");
        }

        //***************************************************************************
        // Helpers that mirror the IOM transaction structure: every transfer ends with a
        // FinishTransmission() so the slave can classify each IOM transfer.
        //***************************************************************************

        private void TxHandshake()
        {
            Assert.AreEqual(0xC0, peripheral.Transmit(0x42));
            Assert.Greater(peripheral.Transmit(0x00), 0);   // free space advertised
            peripheral.FinishTransmission();                // handshake transaction complete -> arms command assembly
        }

        private void SendCommand(byte[] cmd)
        {
            for (int i = 0; i < cmd.Length; i++)
            {
                peripheral.Transmit(cmd[i]);
            }

            peripheral.FinishTransmission();   // data transaction complete -> process if self-delimiting
        }

        private void SendDataBurst(List<byte> cmd, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                peripheral.Transmit(cmd[start + i]);
            }

            peripheral.FinishTransmission();   // one IOM transaction holding `count` data bytes
        }

        private List<byte> ReadResponse()
        {
            Assert.AreEqual(0xC0, peripheral.Transmit(0x81));
            var available = peripheral.Transmit(0x00);
            Assert.Greater(available, 0, "a response should have been enqueued");
            peripheral.FinishTransmission();   // RX handshake transaction complete

            var resp = new List<byte>();
            for (int i = 0; i < available; i++)
            {
                resp.Add(peripheral.Transmit(0x00));   // MISO returns the queued event bytes
            }

            peripheral.FinishTransmission();           // read data transaction complete
            return resp;
        }

        private void AddU32Le(List<byte> list, uint value)
        {
            list.Add((byte)(value & 0xFF));
            list.Add((byte)((value >> 8) & 0xFF));
            list.Add((byte)((value >> 16) & 0xFF));
            list.Add((byte)((value >> 24) & 0xFF));
        }

        // Read a single byte back from NVM via READ_AT_ADDRESS and return it.
        private byte ReadNvmByte(uint addr)
        {
            var readCmd = new List<byte> { 0x01, 0x01, 0xFD, 0x05 };
            AddU32Le(readCmd, addr);
            readCmd.Add(0x01);

            TxHandshake();
            SendCommand(readCmd.ToArray());
            var resp = ReadResponse();

            return resp[7];   // first payload byte of the Command Complete
        }

        // Validate that a 64-byte X||Y pair is a valid, non-at-infinity point on NIST P-256:
        // y^2 == x^3 - 3x + b (mod p) with 0 < x,y < p. Independent of BleScCrypto so the test can catch a
        // public key that is not actually on the curve.
        private static bool IsP256PubKey(byte[] pub)
        {
            if (pub.Length != 64) return false;

            byte[] pBytes =
            {
                0xFF,0xFF,0xFF,0xFF,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                0x00,0x00,0x00,0x00,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
            };
            byte[] bBytes =
            {
                0x5A,0xC6,0x35,0xD8,0xAA,0x3A,0x93,0xE7,0xB3,0xEB,0xBD,0x55,0x76,0x98,0x86,0xBC,
                0x65,0x1D,0x06,0xB0,0xCC,0x53,0xB0,0xF6,0x3B,0xCE,0x3C,0x3E,0x27,0xD2,0x60,0x4B,
            };

            var p = new BigInteger(pBytes, isBigEndian: true, isUnsigned: true);
            var b = new BigInteger(bBytes, isBigEndian: true, isUnsigned: true);

            var xBytes = new byte[32]; Array.Copy(pub, 0, xBytes, 0, 32);
            var yBytes = new byte[32]; Array.Copy(pub, 32, yBytes, 0, 32);

            var x = new BigInteger(xBytes, isBigEndian: true, isUnsigned: true);
            var y = new BigInteger(yBytes, isBigEndian: true, isUnsigned: true);

            if (x.Sign <= 0 || y.Sign <= 0 || x.CompareTo(p) >= 0 || y.CompareTo(p) >= 0) return false;

            var lhs = (y * y) % p;
            var rhs = ((x * x % p) * (x % p)) - (3 * (x % p)) + b;
            rhs %= p;
            if (rhs.Sign < 0) rhs += p;

            return lhs == rhs;
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j])
                {
                    j++;
                }

                if (j == needle.Length)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
