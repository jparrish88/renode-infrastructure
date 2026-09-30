//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Memory;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Peripherals.Wireless;
using Antmicro.Renode.Plugins.WiresharkPlugin;

using NUnit.Framework;

namespace Antmicro.Renode.PeripheralsTests
{
    [TestFixture]
    public class AmbiqEM9305RadioTest
    {
        private const ulong BufferAddress = 0x2000_1000UL;
        private const ulong PeripheralBase = 0x4100_0000UL;

        private Machine machine;
        private AmbiqEM9305Radio radio;
        private byte[] lastFrame;

        [SetUp]
        public void Setup()
        {
            machine = new Machine();

            var buffer = new MappedMemory(machine, 0x100);
            machine.SystemBus.Register(buffer, new BusRangeRegistration(BufferAddress, 0x100));

            radio = new AmbiqEM9305Radio(machine);
            machine.SystemBus.Register(radio, new BusRangeRegistration(PeripheralBase, 0x40));

            lastFrame = null;
            radio.FrameSent += (_, frame) => { lastFrame = (byte[])frame.Clone(); };
        }

        [Test]
        public void ChannelRoundTrip()
        {
            radio.Channel = 20;
            Assert.AreEqual(20, radio.Channel);
            Assert.AreEqual((uint)(20 & 0x7F), radio.ReadDoubleWord(0x00));

            radio.WriteDoubleWord(0x00, 5);
            Assert.AreEqual(5, radio.Channel);
        }

        [Test]
        public void TransmitSendsExactPayloadFromDriverBuffer()
        {
            var payload = new byte[] { 0x02, 0x01, 0x06, 0x19, 0x34, 0x8F };
            machine.SystemBus.WriteBytes(payload, BufferAddress);

            radio.WriteDoubleWord(0x04, (uint)payload.Length);
            radio.WriteDoubleWord(0x08, (uint)BufferAddress);
            radio.WriteDoubleWord(0x10, 1);

            CollectionAssert.AreEqual(payload, lastFrame);
        }

        [Test]
        public void DriverChangeChangesTransmittedFrame()
        {
            var first = new byte[] { 0xAA, 0xBB };
            machine.SystemBus.WriteBytes(first, BufferAddress);
            radio.WriteDoubleWord(0x04, (uint)first.Length);
            radio.WriteDoubleWord(0x08, (uint)BufferAddress);
            radio.WriteDoubleWord(0x10, 1);
            CollectionAssert.AreEqual(first, lastFrame);

            var second = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
            machine.SystemBus.WriteBytes(second, BufferAddress);
            radio.WriteDoubleWord(0x04, (uint)second.Length);
            radio.WriteDoubleWord(0x10, 1);

            CollectionAssert.AreEqual(second, lastFrame);
        }

        [Test]
        public void ReceiveFrameStoresLengthAndPayload()
        {
            var incoming = new byte[] { 0x80, 0x03, 0x2A };
            radio.ReceiveFrame(incoming, null);

            CollectionAssert.AreEqual(incoming, radio.LastReceived);
            Assert.AreEqual((uint)incoming.Length, radio.ReadDoubleWord(0x20));
        }

        [Test]
        public void TransmitFlowsThroughBleMediumToFrameProcessed()
        {
            // A live CurrentEmulation is required by the medium's FrameSentHandler; Clear() provides one.
            EmulationManager.Instance.Clear();

            var medium = new BLEMedium();
            byte[] seenBySink = null;
            IRadio senderSeen = null;
            medium.FrameProcessed += (external, sender, packet) => { senderSeen = sender; seenBySink = (byte[])packet.Clone(); };
            medium.AttachTo(radio);

            // Valid BLE advertising air frame: access address + PDU header + channel map.
            var payload = new byte[] { 0xD6, 0xBE, 0x89, 0x8E, 0x02, 0x19, 0x00, 0x00, 0x00 };
            machine.SystemBus.WriteBytes(payload, BufferAddress);

            radio.Channel = 37;
            radio.WriteDoubleWord(0x04, (uint)payload.Length);   // TXLEN
            radio.WriteDoubleWord(0x08, (uint)BufferAddress);    // TXPTR
            radio.WriteDoubleWord(0x10, 1);                      // TXGO -> Transmit()

            Assert.AreSame(radio, senderSeen);
            CollectionAssert.AreEqual(payload, seenBySink);
        }

        [Test]
        public void EndToEnd_TransmissionFormatsIntoWiresharkAdvertisementPacket()
        {
            var adv = new byte[]
            {
                0xD6, 0xBE, 0x89, 0x8E,   // advertising access address (little-endian on air)
                0x02,                       // PDU header: ADV_IND (type < 5)
                0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            };

            machine.SystemBus.WriteBytes(adv, BufferAddress);
            radio.Channel = 37;             // -> Wireshark channel index 0
            radio.WriteDoubleWord(0x04, (uint)adv.Length);
            radio.WriteDoubleWord(0x08, (uint)BufferAddress);
            radio.WriteDoubleWord(0x10, 1);

            Assert.IsNotNull(lastFrame);
            CollectionAssert.AreEqual(adv, lastFrame); // exact driver bytes hit the air

            var outPkt = new BLESniffer().InsertHeaderToPacket(radio, lastFrame);

            Assert.AreEqual(adv.Length + 10, outPkt.Length);
            Assert.AreEqual((byte)0, outPkt[0]);        // CH37 -> Wireshark index 0
            for (var i = 0; i < 4; i++) { Assert.AreEqual(adv[i], outPkt[4 + i]); }
            Assert.AreEqual((byte)0x3f, outPkt[8]);     // base flags 0x3C3F (advertisement adds no PDU bit)
            Assert.AreEqual((byte)0x3c, outPkt[9]);
            for (var i = 0; i < adv.Length; i++) { Assert.AreEqual(adv[i], outPkt[10 + i]); }
        }

        [Test]
        public void TwoRadiosDeliverOverMediumWhenChannelsMatch()
        {
            EmulationManager.Instance.Clear();
            var emu = EmulationManager.Instance.CurrentEmulation;
            emu.Mode = Emulation.EmulationMode.SynchronizedTimers;
            emu.AddMachine(machine);   // so GetMachine(radioB) resolves in the medium

            var radioB = new AmbiqEM9305Radio(machine);
            machine.SystemBus.Register(radioB, new BusRangeRegistration(PeripheralBase + 0x40, 0x40));

            var medium = new BLEMedium();
            medium.AttachTo(radio);    // A = sender
            medium.AttachTo(radioB);   // B = receiver

            radio.Channel = 37;
            radioB.Channel = 37;       // channels must match for delivery

            var payload = new byte[] { 0xD6, 0xBE, 0x89, 0x8E, 0x02, 0x19, 0x00, 0x00, 0x00 };
            machine.SystemBus.WriteBytes(payload, BufferAddress);

            radio.WriteDoubleWord(0x04, (uint)payload.Length);   // TXLEN
            radio.WriteDoubleWord(0x08, (uint)BufferAddress);    // TXPTR
            radio.WriteDoubleWord(0x10, 1);                      // TXGO -> Transmit()

            CollectionAssert.AreEqual(payload, radioB.LastReceived);
            Assert.AreEqual((uint)payload.Length, radioB.ReadDoubleWord(0x20)); // RXLEN
        }

        [Test]
        public void TwoRadiosNotDeliveredWhenChannelsDiffer()
        {
            EmulationManager.Instance.Clear();
            var emu = EmulationManager.Instance.CurrentEmulation;
            emu.Mode = Emulation.EmulationMode.SynchronizedTimers;
            emu.AddMachine(machine);   // so GetMachine(radioB) resolves in the medium

            var radioB = new AmbiqEM9305Radio(machine);
            machine.SystemBus.Register(radioB, new BusRangeRegistration(PeripheralBase + 0x40, 0x40));

            var medium = new BLEMedium();
            medium.AttachTo(radio);
            medium.AttachTo(radioB);

            radio.Channel = 37;
            radioB.Channel = 38;       // mismatch -> not delivered

            var payload = new byte[] { 0xD6, 0xBE, 0x89, 0x8E, 0x02 };
            machine.SystemBus.WriteBytes(payload, BufferAddress);

            radio.WriteDoubleWord(0x04, (uint)payload.Length);
            radio.WriteDoubleWord(0x08, (uint)BufferAddress);
            radio.WriteDoubleWord(0x10, 1);

            Assert.AreEqual(Array.Empty<byte>(), radioB.LastReceived);
            Assert.AreEqual(0u, radioB.ReadDoubleWord(0x20));   // RXLEN unchanged
        }

        private (AmbiqEM9305Radio radioA, AmbiqEM9305Radio radioB) MakeTwoRadios()
        {
            EmulationManager.Instance.Clear();
            var emu = EmulationManager.Instance.CurrentEmulation;
            emu.Mode = Emulation.EmulationMode.SynchronizedTimers;
            emu.AddMachine(machine);

            var radioB = new AmbiqEM9305Radio(machine);
            machine.SystemBus.Register(radioB, new BusRangeRegistration(PeripheralBase + 0x40, 0x40));

            var medium = new BLEMedium();
            medium.AttachTo(radio);    // A
            medium.AttachTo(radioB);   // B
            radio.Channel = 37;        // both monitor channel 37 for the handshake
            radioB.Channel = 37;
            return (radio, radioB);
        }

        [Test]
        public void DirectedConnectEstablishesUnencryptedLinkOnBothSides()
        {
            var (_, radioB) = MakeTwoRadios();

            byte[] addrA = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };
            byte[] addrB = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };

            var llA = new Em9305LinkLayer(radio)    { OwnAddress = addrA, PeerAddress = addrB };
            var llB = new Em9305LinkLayer(radioB)   { OwnAddress = addrB };

            llA.StartDirectedAdvertising();  // cascades synchronously in SynchronizedTimers mode

            Assert.IsTrue(llA.Connected);
            Assert.IsTrue(llB.Connected);
        }

        [Test]
        public void DirectedAdvNotAddressedToSelfDoesNotConnect()
        {
            var (_, radioB) = MakeTwoRadios();

            byte[] addrA = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };
            byte[] addrB = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };
            byte[] addrX = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

            var llA = new Em9305LinkLayer(radio)    { OwnAddress = addrA, PeerAddress = addrX }; // not B
            var llB = new Em9305LinkLayer(radioB)   { OwnAddress = addrB };

            llA.StartDirectedAdvertising();

            Assert.IsFalse(llA.Connected);
            Assert.IsFalse(llB.Connected);
        }

        [Test]
        public void StartEncHandshakeThenDataPduRoundTripsAndMatchesCtrOracle()
        {
            var (_, radioB) = MakeTwoRadios();

            byte[] addrA = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };   // advertiser -> slave
            byte[] addrB = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };   // responder -> master

            var ltk = new byte[]
            {
                0x2b, 0x7e, 0x15, 0x16, 0x28, 0xae, 0xd2, 0xa6,
                0xab, 0xf7, 0x15, 0x88, 0x09, 0xcf, 0x4f, 0x3c,
            };

            var llA = new Em9305LinkLayer(radio)   { OwnAddress = addrA, PeerAddress = addrB };
            var llB = new Em9305LinkLayer(radioB)  { OwnAddress = addrB };
            llA.ConfigureEncryption(ltk);
            llB.ConfigureEncryption(ltk);

            byte[] decryptedOnSlave = null;
            llA.DataReceived += (_, data) => { decryptedOnSlave = (byte[])data.Clone(); };

            llA.StartDirectedAdvertising();        // cascades synchronously -> both Connected
            Assert.IsTrue(llA.Connected && llB.Connected);

            byte[] lastMasterFrame = null;
            radioB.FrameSent += (_, f) => { lastMasterFrame = (byte[])f.Clone(); };

            const ulong randomE = 0x0123456789ABCDEFUL;
            llB.InitiateStartEnc(randomE);         // START_ENC REQ -> RSP, both sides now Encrypted

            Assert.IsTrue(llA.Encrypted);          // slave completed the handshake
            Assert.IsTrue(llB.Encrypted);          // master completed the handshake

            var plain = System.Text.Encoding.ASCII.GetBytes("A510-LL-DATA-PAYLOAD");  // 20 bytes -> 2 CTR blocks
            Assert.IsTrue(llB.SendEncryptedData(plain));

            // Air frame is on the connection AA (not advertising), ENC bit set, LL_DATA type.
            Assert.IsNotNull(lastMasterFrame);
            byte[] advAa = { 0xD6, 0xBE, 0x89, 0x8E };
            bool notAdvAa = lastMasterFrame[0] != advAa[0] || lastMasterFrame[1] != advAa[1]
                         || lastMasterFrame[2] != advAa[2]   || lastMasterFrame[3] != advAa[3];
            Assert.IsTrue(notAdvAa);
            Assert.AreEqual((byte)0x10, lastMasterFrame[4]);      // header byte: ENC flag (bit4) set only in the current layout
            Assert.AreEqual((byte)0x06, lastMasterFrame[5]);       // LL_DATA(6) opcode carried in its own byte

            // Slave decrypted the payload back to exactly what the master sent.
            CollectionAssert.AreEqual(plain, decryptedOnSlave);

            // On-air ciphertext matches an independent OpenSSL AES-128 CTR oracle (LTK,E=..DEF,cnt=0).
            var expectedCipher = new byte[]
            {
                0xAD, 0x6F, 0x9A, 0xB1, 0x17, 0x84, 0x80, 0x3B,
                0x09, 0xB6, 0xD8, 0x28, 0x38, 0xDD, 0x1A, 0xC3,
                0x8D, 0x52, 0x4E, 0x43,
            };
            var onAir = new byte[plain.Length];
            Array.Copy(lastMasterFrame, 6, onAir, 0, plain.Length);
            CollectionAssert.AreEqual(expectedCipher, onAir);
        }

        [Test]
        public void SecureConnectionsPairingDerivesSharedLtkThenEncryptsData()
        {
            var (_, radioB) = MakeTwoRadios();

            byte[] aAddr = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };   // advertiser -> responder
            byte[] bAddr = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };   // pairing / START_ENC initiator

            var llA = new Em9305LinkLayer(radio)   { OwnAddress = aAddr, PeerAddress = bAddr };
            var llB = new Em9305LinkLayer(radioB)  { OwnAddress = bAddr, PeerAddress = aAddr };

            // Establish the connection (directed advertising + connect-indication cascade).
            llA.StartDirectedAdvertising();
            Assert.IsTrue(llA.Connected && llB.Connected);

            byte[] H(string s)   // local hex helper: this fixture has none of its own
            {
                var b = new byte[s.Length / 2];
                for (int i = 0; i < b.Length; i++)
                    b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                return b;
            }

            // Deterministic ephemeral P-256 private keys (known-valid scalars) + 128-bit randoms.
            byte[] privA = H("8b0c360b1918acffda0a5a2b5a52a45281722745ba9c9941d2e40c175fd9a18c");
            byte[] privB = H("6b0c360b1918acffda0a5a2b5a52a45281722745ba9c9941d2e40c175fd9a18c");
            byte[] rndA  = H("a0b1c2d3e4f5061728394a5b6c7d8e9f");
            byte[] rndB  = H("102030405060708090a0b0c0d0e0f000");

            // B drives START_ENC later -> initiator=true; A is the responder (initiator=false).
            llB.StartSecureConnections(true, privB, rndB);   // generates + transmits PairPub_B -> A stores peer
            llA.StartSecureConnections(false, privA, rndA);  // generates + transmits PairPub_A -> completes both

            Assert.IsTrue(llA.PairingComplete && llB.PairingComplete);
            CollectionAssert.AreNotEqual(new byte[16], llA.PairedLtk);   // LKDH produced a real key, not zeros

            // The derived LTK (and MAC key) must be byte-identical on both peers with NO manual injection.
            CollectionAssert.AreEqual(llA.PairedLtk, llB.PairedLtk);
            CollectionAssert.AreEqual(llA.PairedMacKey, llB.PairedMacKey);

            // Run START_ENC using the auto-configured key and round-trip an encrypted Data PDU (20 bytes -> 2 CTR blocks).
            byte[] decryptedOnSlave = null;
            llA.DataReceived += (_, data) => { decryptedOnSlave = (byte[])data.Clone(); };

            const ulong randomE = 0x123456789ABCDEF0UL;
            llB.InitiateStartEnc(randomE);   // cascades synchronously -> both sides become encrypted
            Assert.IsTrue(llA.Encrypted && llB.Encrypted);

            var plain = System.Text.Encoding.ASCII.GetBytes("A510-SC-LKDH-PAYLOAD");
            Assert.IsTrue(llB.SendEncryptedData(plain));
            CollectionAssert.AreEqual(plain, decryptedOnSlave);   // decrypted on A using the derived LTK
        }

        [Test]
        public void SmpPairingProcedureCompletesOverAirWithDhKeyCheckAndBondedLtk()
        {
            var (_, radioB) = MakeTwoRadios();

            byte[] aAddr = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };   // responder / START_ENC target
            byte[] bAddr = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };   // initiator

            var llA = new Em9305LinkLayer(radio)   { OwnAddress = aAddr, PeerAddress = bAddr };
            var llB = new Em9305LinkLayer(radioB)  { OwnAddress = bAddr, PeerAddress = aAddr };

            llA.StartDirectedAdvertising();        // connect cascade -> both Connected
            Assert.IsTrue(llA.Connected && llB.Connected);

            byte[] H(string s)   // local hex helper (this fixture has none of its own)
            {
                var b = new byte[s.Length / 2];
                for (int i = 0; i < b.Length; i++)
                    b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                return b;
            }

            // Deterministic ephemeral P-256 keys + pairing randoms so the whole procedure is reproducible.
            byte[] privA = H("8b0c360b1918acffda0a5a2b5a52a45281722745ba9c9941d2e40c175fd9a18c");
            byte[] privB = H("6b0c360b1918acffda0a5a2b5a52a45281722745ba9c9941d2e40c175fd9a18c");
            byte[] rndA  = H("a0b1c2d3e4f5061728394a5b6c7d8e9f");
            byte[] rndB  = H("102030405060708090a0b0c0d0e0f000");

            // Arm both sides (the responder must be ready to answer), then let the initiator drive the full SMP procedure:
            // PairingReq -> Rsp + pub, initiator pub, f5 LKDH, f4 DHKey-check confirm value, and encrypted Encrypted-Info LTK.
            llA.PrepSecureConnections(false, privA, rndA);   // responder
            llB.PrepSecureConnections(true,  privB, rndB);   // initiator
            llB.BeginSmpPairing();

            Console.WriteLine($"DIAG A: connected={llA.Connected} pairDone={llA.PairingComplete} ltk={(llA.PairedLtk == null ? "null" : BitConverter.ToString(llA.PairedLtk))}");
            Console.WriteLine($"DIAG B: connected={llB.Connected} pairDone={llB.PairingComplete} ltk={(llB.PairedLtk == null ? "null" : BitConverter.ToString(llB.PairedLtk))}");

            // f5 LKDH produced an identical shared secret / bond LTK on both peers with no manual key injection.
            Assert.IsTrue(llA.PairingComplete && llB.PairingComplete);
            CollectionAssert.AreNotEqual(new byte[16], llA.PairedLtk);
            CollectionAssert.AreEqual(llA.PairedLtk, llB.PairedLtk);

            // The full SMP procedure completed on BOTH sides (request/response + public keys + f4 DHKey check + LTK bonding).
            Assert.IsTrue(llA.SmpComplete && llB.SmpComplete);
            Assert.IsTrue(llA.DhKeyCheckPassed);             // responder verified the initiator's f4 DHKey-check value

            // Bonding: the encrypted Encrypted-Info PDU carried the bond LTK, and it equals the locally-derived LKDH LTK.
            // (The responder/slave A is who receives & decrypts that PDU and stores the bonded key.)
            Assert.IsTrue(llA.LtkDistributed);
            Assert.IsNotNull(llA.BondedLtk);
            CollectionAssert.AreEqual(llA.PairedLtk, llA.BondedLtk);
            Assert.IsTrue(llA.BondLtkMatchesOwn);

            // The SMP-derived LTK is usable: run START_ENC and round-trip an encrypted Data PDU (16 bytes -> 1 CTR block).
            byte[] decryptedOnSlave = null;
            llA.DataReceived += (_, data) => { decryptedOnSlave = (byte[])data.Clone(); };

            const ulong randomE = 0x9F8E7D6C5B4A3928UL;
            llB.InitiateStartEnc(randomE);        // cascades synchronously -> both sides become encrypted
            Assert.IsTrue(llA.Encrypted && llB.Encrypted);

            var plain = System.Text.Encoding.ASCII.GetBytes("A510-SMP-LESEC-DATA");
            Assert.IsTrue(llB.SendEncryptedData(plain));
            CollectionAssert.AreEqual(plain, decryptedOnSlave);   // decrypted on A using the SMP-derived LTK
        }

        [Test]
        public void SpiControllerKeysDriveLinkLayerPairingOverAir()
        {
            // Two real EM9305 SPI controllers, each lazily generating its own P-256 ephemeral key pair -- exactly the
            // material a host reads back via LE_Read_Local_P256_Pub_Key (0x2025). We drive link-layer pairing with those
            // keys to prove the over-air PairPub points and DH secret match what each SPI controller reports for 0x2025/0x2026.
            var (_, radioB) = MakeTwoRadios();
            var spiA = new AmbiqEM9305(machine);
            var spiB = new AmbiqEM9305(machine);

            var (privA, pubAX, pubAY) = spiA.GetLocalP256Ephemeral();   // 0x2025 answer for controller A
            var (privB, pubBX, pubBY) = spiB.GetLocalP256Ephemeral();   // 0x2025 answer for controller B

            byte[] aAddr = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };
            byte[] bAddr = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };

            var llA = new Em9305LinkLayer(radio)   { OwnAddress = aAddr, PeerAddress = bAddr };
            var llB = new Em9305LinkLayer(radioB)  { OwnAddress = bAddr, PeerAddress = aAddr };

            llA.StartDirectedAdvertising();        // connect cascade -> both Connected
            Assert.IsTrue(llA.Connected && llB.Connected);

            byte[] rndA = { 0xA0, 0xB1, 0xC2, 0xD3, 0xE4, 0xF5, 0x06, 0x17, 0x28, 0x39, 0x4A, 0x5B, 0x6C, 0x7D, 0x8E, 0x9F };
            byte[] rndB = { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00 };

            // Feed each link layer the private scalar its own SPI controller reports for 0x2025; the on-air PairPub
            // point is then G(priv) -- the identical public key a host read from that same controller.
            llB.StartSecureConnections(true, privB, rndB);    // initiator (drives START_ENC later)
            llA.StartSecureConnections(false, privA, rndA);   // responder; completes pairing on both sides

            Assert.IsTrue(llA.PairingComplete && llB.PairingComplete);
            CollectionAssert.AreEqual(llA.PairedLtk, llB.PairedLtk);

            // The DH secret the link layer paired with equals an independent ECDH oracle built from the two SPI
            // controllers' 0x2025 public points, and matches each controller's own LE_Generate_DHKey (0x2026) answer.
            var peerOfA = new byte[64]; Array.Copy(pubBX, 0, peerOfA, 0, 32); Array.Copy(pubBY, 0, peerOfA, 32, 32);
            var peerOfB = new byte[64]; Array.Copy(pubAX, 0, peerOfB, 0, 32); Array.Copy(pubAY, 0, peerOfB, 32, 32);

            CollectionAssert.AreEqual(BleScCrypto.G1(privA, pubBX, pubBY), llA.PairedDhKey);   // independent oracle
            CollectionAssert.AreEqual(spiA.ComputeLocalDhKey(peerOfA), llA.PairedDhKey);      // == the 0x2026 reply for A's peer B
            CollectionAssert.AreEqual(spiB.ComputeLocalDhKey(peerOfB), llB.PairedDhKey);      // symmetric on B

            // End-to-end: START_ENC with the auto-derived LTK, then an encrypted Data PDU round-trips.
            byte[] decryptedOnSlave = null;
            llA.DataReceived += (_, data) => { decryptedOnSlave = (byte[])data.Clone(); };

            const ulong randomE = 0x0FEDCBA987654321UL;
            llB.InitiateStartEnc(randomE);        // cascades synchronously -> both sides become encrypted
            Assert.IsTrue(llA.Encrypted && llB.Encrypted);

            var plain = System.Text.Encoding.ASCII.GetBytes("A510-SC-SPI-COUPLING");
            Assert.IsTrue(llB.SendEncryptedData(plain));
            CollectionAssert.AreEqual(plain, decryptedOnSlave);   // decrypted on A using the derived LTK
        }

        [Test]
        public void NonAdvertisingAccessAddressIsIgnored()
        {
            var (_, radioB) = MakeTwoRadios();

            byte[] addrA = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };
            byte[] addrB = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };

            var llB = new Em9305LinkLayer(radioB)   { OwnAddress = addrB };

            // ADV_DIRECT_IND-shaped frame whose access address is not the advertising AA
            var bogus = new byte[17];
            bogus[0] = 0x00; bogus[1] = 0xBE; bogus[2] = 0x89; bogus[3] = 0x8E;
            bogus[4] = 0x12; // ADV_DIRECT_IND op + TxAdd
            Array.Copy(addrA, 0, bogus, 5, 6);
            Array.Copy(addrB, 0, bogus, 11, 6);

            radioB.ReceiveFrame(bogus, radio);   // simulate air delivery directly

            Assert.IsFalse(llB.Connected);
        }
    }
}
