//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite / Apollo330P IOS full-duplex slave block (IOSLAVEFD0
// @ 0x40035000, IOSLAVEFD1 @ 0x40036000). NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo330P.svd (also byte-
// identical on apollo510L.svd). Phase-1 model: SVD-faithful register file
// with the AHB FIFO window (0x000-0x0FC) as RAM, so drivers never fault and
// read back real reset values (FIFOCFG=0x20000000, CFG.EN=1). Live IRQs:
// IOSLAVEFD n (96/98) and IOSLAVEFDnACC (97/99), driven by INTSET/INTCLR/
// INTEN semantics. No external SPI controller is attached in simulation, so
// no bus transactions ever arrive: FIFO counts stay empty and no DMA runs.
// Full framing/FIFO-overflow behavior to be added when a two-participant
// test vehicle exists.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_IOSlaveFD : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();
        public GPIO ACCIRQ { get; } = new GPIO();

        public AmbiqApollo510L_IOSlaveFD(IMachine machine) : base(machine)
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
            for(var i = 0; i < FifoWords; i++)
            {
                fifo[i] = 0;
            }
            UpdateIRQ();
            UpdateACCIRQ();
        }

        public override uint ReadDoubleWord(long offset)
        {
            if(offset < FifoWindowEnd)
            {
                return fifo[offset / 4];
            }
            if(registers.TryGetValue(offset, out var value))
            {
                return value;
            }
            return 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            if(offset < FifoWindowEnd)
            {
                fifo[offset / 4] = value;
                return;
            }
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
            this.NoisyLog("IOSLAVEFD WR 0x{0:X} = 0x{1:X8}", offset, value);
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

        private const long FifoWindowEnd = 0x100;
        private const int FifoWords = 64;
        private const long RegFIFOPTR        = 0x100;
        private const long RegFIFOCFG        = 0x104;
        private const long RegCFG            = 0x118;
        private const long RegINTEN          = 0x200;
        private const long RegINTSTAT        = 0x204;
        private const long RegINTCLR         = 0x208;
        private const long RegINTSET         = 0x20C;
        private const long RegREGACCINTEN    = 0x210;
        private const long RegREGACCINTSTAT  = 0x214;
        private const long RegREGACCINTCLR   = 0x218;
        private const long RegREGACCINTSET   = 0x21C;

        private readonly uint[] fifo = new uint[FifoWords];
        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x100, 0x00000000), // FIFOPTR
            new KeyValuePair<long, uint>(0x104, 0x20000000), // FIFOCFG
            new KeyValuePair<long, uint>(0x108, 0x00000000), // FIFOTHR
            new KeyValuePair<long, uint>(0x10C, 0x00000000), // FUPD
            new KeyValuePair<long, uint>(0x110, 0x00000000), // FIFOCTR
            new KeyValuePair<long, uint>(0x114, 0x00000000), // FIFOINC
            new KeyValuePair<long, uint>(0x118, 0x00000001), // CFG
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
