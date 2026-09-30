//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 I/O slave block (IOSLAVE @ 0x40034000, IRQ4 + ACC IRQ5).
// NEW file for the Apollo510 family (no Lite equivalent; Lite kept only
// IOSLAVEFD0/1, see AmbiqApollo510L_IOSlaveFD).
//
// Register map + reset values from SVD/apollo510.svd (23 regs).
// Phase-1 model: SVD-faithful register file with live IRQs driven by
// INTSET/INTCLR/INTEN and REGACCINTSET/REGACCINTCLR/REGACCINTEN semantics.
// No external SPI/I2C master is attached in simulation, so no bus
// transactions ever arrive: FIFO counts stay empty and no DMA runs.
// Master stimulus / loopback framing is future work (needs a test vehicle).
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510_IOSlave : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();
        public GPIO ACCIRQ { get; } = new GPIO();

        public AmbiqApollo510_IOSlave(IMachine machine) : base(machine)
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
            UpdateACCIRQ();
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
            switch(offset)
            {
            case RegINTSET:
                registers[RegINTSTAT] |= value;
                UpdateIRQ();
                break;
            case RegINTCLR:
                registers[RegINTSTAT] &= ~value;
                UpdateIRQ();
                break;
            case RegREGACCINTSET:
                registers[RegREGACCINTSTAT] |= value;
                UpdateACCIRQ();
                break;
            case RegREGACCINTCLR:
                registers[RegREGACCINTSTAT] &= ~value;
                UpdateACCIRQ();
                break;
            default:
                registers[offset] = value;
                if(offset == RegINTEN)
                {
                    UpdateIRQ();
                }
                else if(offset == RegREGACCINTEN)
                {
                    UpdateACCIRQ();
                }
                break;
            }
            this.NoisyLog("IOSLAVE WR 0x{0:X} = 0x{1:X8}", offset, value);
        }

        private void UpdateIRQ()
        {
            var pending = (registers[RegINTSTAT] & registers[RegINTEN]) != 0;
            IRQ.Set(pending);
        }

        private void UpdateACCIRQ()
        {
            var pending = (registers[RegREGACCINTSTAT] & registers[RegREGACCINTEN]) != 0;
            ACCIRQ.Set(pending);
        }

        private const long RegINTEN          = 0x200;
        private const long RegINTSTAT        = 0x204;
        private const long RegINTCLR         = 0x208;
        private const long RegINTSET         = 0x20C;
        private const long RegREGACCINTEN    = 0x210;
        private const long RegREGACCINTSTAT  = 0x214;
        private const long RegREGACCINTCLR   = 0x218;
        private const long RegREGACCINTSET   = 0x21C;

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x100, 0x00000000), // FIFOPTR
            new KeyValuePair<long, uint>(0x104, 0x20000000), // FIFOCFG
            new KeyValuePair<long, uint>(0x108, 0x00000000), // FIFOTHR
            new KeyValuePair<long, uint>(0x10C, 0x00000000), // FUPD
            new KeyValuePair<long, uint>(0x110, 0x00000000), // FIFOCTR
            new KeyValuePair<long, uint>(0x114, 0x00000000), // FIFOINC
            new KeyValuePair<long, uint>(0x118, 0x00000000), // CFG
            new KeyValuePair<long, uint>(0x11C, 0x00000000), // PRENC
            new KeyValuePair<long, uint>(0x120, 0x00000000), // IOINTCTL
            new KeyValuePair<long, uint>(0x124, 0x00000000), // GENADD
            new KeyValuePair<long, uint>(0x128, 0x00000000), // ADDPTR
            new KeyValuePair<long, uint>(0x130, 0x00000000), // DMACFG
            new KeyValuePair<long, uint>(0x134, 0x00000000), // DMATOTCOUNT
            new KeyValuePair<long, uint>(0x138, 0x00000000), // DMATARGADDR
            new KeyValuePair<long, uint>(0x13C, 0x00000000), // DMASTAT
            new KeyValuePair<long, uint>(0x200, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x204, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x208, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x20C, 0x00000000), // INTSET
            new KeyValuePair<long, uint>(0x210, 0x00000000), // REGACCINTEN
            new KeyValuePair<long, uint>(0x214, 0x00000000), // REGACCINTSTAT
            new KeyValuePair<long, uint>(0x218, 0x00000000), // REGACCINTCLR
            new KeyValuePair<long, uint>(0x21C, 0x00000000), // REGACCINTSET
        };
    }
}
