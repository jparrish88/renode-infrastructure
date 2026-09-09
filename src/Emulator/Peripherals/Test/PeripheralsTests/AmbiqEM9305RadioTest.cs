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
    }
}
