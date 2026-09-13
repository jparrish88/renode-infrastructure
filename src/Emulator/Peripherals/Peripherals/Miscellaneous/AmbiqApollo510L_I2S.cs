//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite I2S0/I2S1 (I2S @ 0x40208000/0x40209000, IRQ44).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (36 regs).
// Phase-1 model: SVD-faithful register file with live IRQ44 from
// INTEN/INTSTAT. No audio streaming is emulated yet.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_I2S : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_I2S(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x400;

        public override void Reset()
        {
            registers.Clear();
            foreach(var kv in Resets)
            {
                registers[kv.Key] = kv.Value;
            }
            UpdateIRQ();
        }

        public override uint ReadDoubleWord(long offset)
        {
            if(registers.TryGetValue(offset, out var value))
            {
                return value;
            }
            return 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            registers[offset] = value;
            if(offset == 0x300 || offset == 0x304 || offset == 0x308 || offset == 0x30C)
            {
                UpdateIRQ();
            }
        }

        private void UpdateIRQ()
        {
            // Register-file model: interrupt semantics are not modelled for
            // this block yet. The IRQ output exists for platform wiring but
            // is intentionally never asserted (no fabricated levels).
            IRQ.Unset();
        }

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000000), // RXDATA
            new KeyValuePair<long, uint>(0x04, 0x00000000), // RXCHANID
            new KeyValuePair<long, uint>(0x08, 0x00000000), // RXFIFOSTATUS
            new KeyValuePair<long, uint>(0x0C, 0x00000000), // RXFIFOSIZE
            new KeyValuePair<long, uint>(0x10, 0x00000000), // RXUPPERLIMIT
            new KeyValuePair<long, uint>(0x20, 0x00000000), // TXDATA
            new KeyValuePair<long, uint>(0x24, 0x00000000), // TXCHANID
            new KeyValuePair<long, uint>(0x28, 0x00000000), // TXFIFOSTATUS
            new KeyValuePair<long, uint>(0x2C, 0x00000000), // TXFIFOSIZE
            new KeyValuePair<long, uint>(0x30, 0x00000000), // TXLOWERLIMIT
            new KeyValuePair<long, uint>(0x40, 0x01AC01A4), // I2SDATACFG
            new KeyValuePair<long, uint>(0x44, 0x01F103F0), // I2SIOCFG
            new KeyValuePair<long, uint>(0x48, 0x00000000), // I2SCTL
            new KeyValuePair<long, uint>(0x4C, 0x00000000), // IPBIRPT
            new KeyValuePair<long, uint>(0x50, 0xDA1A0000), // IPCOREID
            new KeyValuePair<long, uint>(0x54, 0x00000002), // AMQCFG
            new KeyValuePair<long, uint>(0x60, 0x00000000), // INTDIV
            new KeyValuePair<long, uint>(0x64, 0x00000000), // FRACDIV
            new KeyValuePair<long, uint>(0x100, 0x00000160), // CLKCFG
            new KeyValuePair<long, uint>(0x200, 0x00000000), // DMACFG
            new KeyValuePair<long, uint>(0x204, 0x00000000), // RXDMATOTCNT
            new KeyValuePair<long, uint>(0x208, 0x00000000), // RXDMAADDR
            new KeyValuePair<long, uint>(0x20C, 0x00000000), // RXDMASTAT
            new KeyValuePair<long, uint>(0x210, 0x00000000), // TXDMATOTCNT
            new KeyValuePair<long, uint>(0x214, 0x00000000), // TXDMAADDR
            new KeyValuePair<long, uint>(0x218, 0x00000000), // TXDMASTAT
            new KeyValuePair<long, uint>(0x21C, 0x00000000), // DMAENNEXTCTRL
            new KeyValuePair<long, uint>(0x220, 0x00000000), // RXDMATOTCNTNEXT
            new KeyValuePair<long, uint>(0x224, 0x00000000), // RXDMAADDRNEXT
            new KeyValuePair<long, uint>(0x228, 0x00000000), // TXDMATOTCNTNEXT
            new KeyValuePair<long, uint>(0x22C, 0x00000000), // TXDMAADDRNEXT
            new KeyValuePair<long, uint>(0x230, 0x00000000), // STATUS
            new KeyValuePair<long, uint>(0x300, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x304, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x308, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x30C, 0x00000000), // INTSET
        };
    }
}
