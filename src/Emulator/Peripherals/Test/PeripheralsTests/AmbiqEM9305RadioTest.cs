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
    }
}
