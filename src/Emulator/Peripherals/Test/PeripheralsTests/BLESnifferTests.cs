//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;

using Antmicro.Renode.Peripherals.Wireless;
using Antmicro.Renode.Plugins.WiresharkPlugin;

using NUnit.Framework;

namespace Antmicro.Renode.UnitTests
{
    // Minimal IRadio that lets a test pick an arbitrary channel (the real NRF52840_Radio.Channel setter throws).
    internal sealed class TestRadio : IRadio
    {
        public int Channel { get; set; }
        private Action<IRadio, byte[]> frameSentHandler;

        public event Action<IRadio, byte[]> FrameSent
        {
            add { frameSentHandler += value; }
            remove { frameSentHandler -= value; }
        }

        public void ReceiveFrame(byte[] frame, IRadio sender) { }

        public void Reset() { }
    }

    // Locks in the 10-byte header layout the BLESniffer prepends so our cross-platform Wireshark tap decodes
    // advertising / connection data PDUs with the right channel index and flags.
    [TestFixture]
    public class BLESnifferTests
    {
        // Advertising access address 0x8e89bed6 as it appears in packet bytes (little-endian on air).
        private static readonly byte[] AdvAA = { 0xd6, 0xbe, 0x89, 0x8e };

        [Test]
        public void ShouldFormatAdvertisementFrameWithChannelIndexAndBaseFlags()
        {
            var sender = new TestRadio { Channel = 38 };
            var sniffer = new BLESniffer();

            // Minimal ADV_IND frame: access address (4) + PDU header byte (type 0) + filler, total >= 9.
            var adv = new byte[9];
            Array.Copy(AdvAA, adv, 4);
            adv[4] = 0x0;
            for (var i = 5; i < 9; i++) { adv[i] = (byte)(i + 1); }

            var outPkt = sniffer.InsertHeaderToPacket(sender, adv);

            Assert.AreEqual(adv.Length + 10, outPkt.Length);
            Assert.AreEqual((byte)12, outPkt[0]); // channel 38 -> Wireshark index 12
            for (var i = 0; i < 4; i++) { Assert.AreEqual(AdvAA[i], outPkt[4 + i]); }

            // Base flags are 0x3C3F; an advertisement adds no PDU bit.
            Assert.AreEqual((byte)0x3f, outPkt[8]);
            Assert.AreEqual((byte)0x3c, outPkt[9]);

            for (var i = 0; i < adv.Length; i++) { Assert.AreEqual(adv[i], outPkt[10 + i]); }
        }

        [Test]
        public void ShouldMarkMasterAndSlaveDataFramesAfterConnectInd()
        {
            var master = new TestRadio { Channel = 37 };
            var slave = new TestRadio { Channel = 37 };
            var sniffer = new BLESniffer();

            // CONNECT_IND (PDU type 5) carries the connection access address at byte offset 18.
            var connAA = new byte[] { 0x11, 0x22, 0x33, 0x44 };
            var connectInd = new byte[43];
            Array.Copy(AdvAA, connectInd, 4); // uses the advertising access address on air
            connectInd[4] = 0x5;              // PDU type CONNECT_IND (low nibble)
            Array.Copy(connAA, 0, connectInd, 18, 4);

            var indOut = sniffer.InsertHeaderToPacket(master, connectInd);
            Assert.AreEqual((byte)0, indOut[0]);       // channel 37 -> Wireshark index 0
            Assert.AreEqual((byte)0x3c, indOut[9]);    // still an advertisement (base flags)

            var data = new byte[12];
            Array.Copy(connAA, data, 4);               // connection access address (not the advertising one)

            var fromMaster = sniffer.InsertHeaderToPacket(master, data);
            for (var i = 0; i < 4; i++) { Assert.AreEqual(connAA[i], fromMaster[4 + i]); }
            // base 0x3C3F | DataPacketMasterToSlave(0x100) -> low 0x3F, high 0x3D.
            Assert.AreEqual((byte)0x3f, fromMaster[8]);
            Assert.AreEqual((byte)0x3d, fromMaster[9]);

            var fromSlave = sniffer.InsertHeaderToPacket(slave, data);
            // base 0x3C3F | DataPacketSlaveToMaster(0x180) -> low 0xBF, high 0x3D.
            Assert.AreEqual((byte)0xbf, fromSlave[8]);
            Assert.AreEqual((byte)0x3d, fromSlave[9]);
        }

        [Test]
        public void ShouldRemapChannelToWiresharkIndex()
        {
            var sniffer = new BLESniffer();
            var adv = new byte[9];
            Array.Copy(AdvAA, adv, 4);

            foreach (var (channel, expected) in new[] { (0, (byte)1), (37, (byte)0), (38, (byte)12), (39, (byte)39) })
            {
                var sender = new TestRadio { Channel = channel };
                var outPkt = sniffer.InsertHeaderToPacket(sender, adv);
                Assert.AreEqual(expected, outPkt[0]);
            }
        }
    }
}
