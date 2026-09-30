//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 AUDADC (AUDADC @ 0x40210000, IRQ42).
// NEW file for the Apollo510 family (no Lite equivalent; Lite removed AUDADC).
//
// Register map + reset values from SVD/apollo510.svd (40 regs).
// Phase-1 model: register file with live IRQ from INTEN/INTSTAT/INTSET/
// INTCLR. No audio capture is emulated yet (same posture as the PDM
// phase-1); paced DMA completion can follow the PDM pattern when an audio
// vehicle exists.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510_AUDADC : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510_AUDADC(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x300;

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
            if(offset == 0x208) // INTCLR: write-1-to-clear
            {
                registers[0x204] = registers.TryGetValue(0x204, out var st) ? (uint)(st & ~value) : 0u;
                registers[offset] = 0;
                UpdateIRQ();
                return;
            }
            if(offset == 0x20C) // INTSET: set INTSTAT bits
            {
                registers[0x204] = registers.TryGetValue(0x204, out var st2) ? (uint)(st2 | value) : value;
                registers[offset] = value;
                UpdateIRQ();
                return;
            }
            registers[offset] = value;
            if(IsIrqRegister(offset))
            {
                UpdateIRQ();
            }
        }

        private void UpdateIRQ()
        {
            uint stat = registers.TryGetValue(0x204, out var s) ? s : 0u;
            uint en = registers.TryGetValue(0x200, out var e) ? e : 0u;
            if((stat & en) != 0)
            {
                IRQ.Set();
            }
            else
            {
                IRQ.Unset();
            }
        }

        private static bool IsIrqRegister(long offset)
        {
            return offset == 0x200 || offset == 0x204 || offset == 0x208 || offset == 0x20C;
        }

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000000), // CFG
            new KeyValuePair<long, uint>(0x04, 0x00000000), // STAT
            new KeyValuePair<long, uint>(0x08, 0x00000000), // SWT
            new KeyValuePair<long, uint>(0x0C, 0x00000000), // SL0CFG
            new KeyValuePair<long, uint>(0x10, 0x00000000), // SL1CFG
            new KeyValuePair<long, uint>(0x14, 0x00000000), // SL2CFG
            new KeyValuePair<long, uint>(0x18, 0x00000000), // SL3CFG
            new KeyValuePair<long, uint>(0x1C, 0x00000000), // SL4CFG
            new KeyValuePair<long, uint>(0x20, 0x00000000), // SL5CFG
            new KeyValuePair<long, uint>(0x24, 0x00000000), // SL6CFG
            new KeyValuePair<long, uint>(0x28, 0x00000000), // SL7CFG
            new KeyValuePair<long, uint>(0x2C, 0x00000000), // WULIM
            new KeyValuePair<long, uint>(0x30, 0x00000000), // WLLIM
            new KeyValuePair<long, uint>(0x34, 0x00000000), // SCWLIM
            new KeyValuePair<long, uint>(0x38, 0x00000000), // FIFO
            new KeyValuePair<long, uint>(0x3C, 0x00000000), // FIFOPR
            new KeyValuePair<long, uint>(0x40, 0x00000000), // INTTRIGTIMER
            new KeyValuePair<long, uint>(0x44, 0x00000000), // FIFOSTAT
            new KeyValuePair<long, uint>(0x48, 0x00000000), // DATAOFFSET
            new KeyValuePair<long, uint>(0x60, 0x00000000), // ZXCFG
            new KeyValuePair<long, uint>(0x64, 0x00000000), // ZXLIM
            new KeyValuePair<long, uint>(0x68, 0x00000000), // GAINCFG
            new KeyValuePair<long, uint>(0x6C, 0x00000000), // GAIN
            new KeyValuePair<long, uint>(0xA4, 0x00000000), // SATCFG
            new KeyValuePair<long, uint>(0xA8, 0x00000000), // SATLIM
            new KeyValuePair<long, uint>(0xAC, 0x00010001), // SATMAX
            new KeyValuePair<long, uint>(0xB0, 0x00000000), // SATCLR
            new KeyValuePair<long, uint>(0x200, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x204, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x208, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x20C, 0x00000000), // INTSET
            new KeyValuePair<long, uint>(0x240, 0x00000000), // DMATRIGEN
            new KeyValuePair<long, uint>(0x244, 0x00000000), // DMATRIGSTAT
            new KeyValuePair<long, uint>(0x280, 0x00000000), // DMACFG
            new KeyValuePair<long, uint>(0x288, 0x00000000), // DMATOTCOUNT
            new KeyValuePair<long, uint>(0x28C, 0x00000000), // DMATARGADDR
            new KeyValuePair<long, uint>(0x290, 0x00000000), // DMASTAT
            new KeyValuePair<long, uint>(0x294, 0x00000000), // DMATARGADDRNEXT
            new KeyValuePair<long, uint>(0x298, 0x00000000), // DMATOTCOUNTNEXT
            new KeyValuePair<long, uint>(0x29C, 0x00000000), // DMAENNEXTCTRL
        };
    }
}
