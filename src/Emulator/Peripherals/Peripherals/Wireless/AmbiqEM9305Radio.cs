//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;

namespace Antmicro.Renode.Peripherals.Wireless
{
    public class AmbiqEM9305Radio : BasicDoubleWordPeripheral, IRadio
    {
        private IValueRegisterField channel;
        private IValueRegisterField txLength;
        private IValueRegisterField txPointer;
        private IValueRegisterField rxLength;

        private byte[] lastReceived = Array.Empty<byte>();

        public AmbiqEM9305Radio(IMachine machine) : base(machine)
        {
            DefineRegisters();
            Reset();
        }

        public override void Reset()
        {
            channel.Value = 0;
            txLength.Value = 0;
            txPointer.Value = 0;
            rxLength.Value = 0;
            lastReceived = Array.Empty<byte>();
            base.Reset();
        }

        private void DefineRegisters()
        {
            Registers.Channel.Define(this, name: "CH").WithValueField(0, 7, out channel, name: "CH");
            Registers.TxLength.Define(this, name: "TXLEN").WithValueField(0, 8, out txLength, name: "LEN");
            Registers.TxPointer.Define(this, name: "TXPTR").WithValueField(0, 32, out txPointer, name: "PTR");
            Registers.TxTrigger.Define(this, name: "TXGO")
                .WithFlag(0, FieldMode.Write, writeCallback: (_, value) => { if(value) Transmit(); })
                .WithReservedBits(1, 31);
            Registers.RxLength.Define(this, name: "RXLEN").WithValueField(0, 8, out rxLength, name: "LEN");
        }

        public int Channel
        {
            get => (int)channel.Value;
            set => channel.Value = (ulong)(value & 0x7F);
        }

        public event Action<IRadio, byte[]> FrameSent;

        // Raised for every air frame delivered to this radio by the wireless medium.
        public event Action<byte[] /* frame */, IRadio /* sender */> FrameReceived;

        // Directly emit an already-formed air frame (used by the simulated link layer).
        public void TransmitFrame(byte[] frame)
        {
            if(frame != null && frame.Length > 0)
            {
                FrameSent?.Invoke(this, frame);
            }
        }

        private void Transmit()
        {
            var length = (int)txLength.Value;
            if(length <= 0 || length > 255)
            {
                return;
            }

            var address = (ulong)txPointer.Value;
            var buffer = sysbus.ReadBytes(address, length);
            FrameSent?.Invoke(this, buffer);
        }

        public void ReceiveFrame(byte[] frame, IRadio sender)
        {
            lastReceived = frame ?? Array.Empty<byte>();
            rxLength.Value = (uint)Math.Min(lastReceived.Length, 255);
            FrameReceived?.Invoke(lastReceived, sender);
        }

        public byte[] LastReceived => lastReceived;

        private enum Registers
        {
            Channel   = 0x00,
            TxLength  = 0x04,
            TxPointer = 0x08,
            TxTrigger = 0x10,
            RxLength  = 0x20,
        }
    }
}
