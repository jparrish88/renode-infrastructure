//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite PDM0 (PDM @ 0x40201000, IRQ48).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (21 regs).
// Phase-1 model: SVD-faithful register file with live IRQ48 from the
// interrupt domain (INTEN/INTSTAT/INTSET/INTCLR). No audio capture is
// emulated yet — writes are stored and logged, reads return stored value
// (or reset). Extend with FIFO/DMA handling when an audio vehicle exists.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_PDM : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_PDM(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x200;

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
            if(offset == 0x14) // FIFOREAD is read-only in HW, but log writes
            {
                this.Log(LogLevel.Info, "PDM WR FIFOREAD <- 0x{0:X} (ignored)", value);
            }
            if(IsIrqRegister(offset))
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

        private static bool IsIrqRegister(long offset)
        {
            return offset == 0x100 || offset == 0x104 || offset == 0x108 || offset == 0x10C;
        }

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000000), // CTRL
            new KeyValuePair<long, uint>(0x04, 0x42100364), // CORECFG0
            new KeyValuePair<long, uint>(0x08, 0x00B28105), // CORECFG1
            new KeyValuePair<long, uint>(0x0C, 0x80000434), // CORECTRL
            new KeyValuePair<long, uint>(0x10, 0x00000000), // FIFOCNT
            new KeyValuePair<long, uint>(0x14, 0x00000000), // FIFOREAD
            new KeyValuePair<long, uint>(0x18, 0x00000000), // FIFOFLUSH
            new KeyValuePair<long, uint>(0x1C, 0x00000010), // FIFOTHR
            new KeyValuePair<long, uint>(0x100, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x104, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x108, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x10C, 0x00000000), // INTSET
            new KeyValuePair<long, uint>(0x140, 0x00000000), // DMATRIGEN
            new KeyValuePair<long, uint>(0x144, 0x00000000), // DMATRIGSTAT
            new KeyValuePair<long, uint>(0x148, 0x00000000), // DMACFG
            new KeyValuePair<long, uint>(0x150, 0x00000000), // DMATOTCOUNT
            new KeyValuePair<long, uint>(0x154, 0x00000000), // DMATARGADDR
            new KeyValuePair<long, uint>(0x158, 0x00000000), // DMASTAT
            new KeyValuePair<long, uint>(0x160, 0x00000000), // DMATARGADDRNEXT
            new KeyValuePair<long, uint>(0x164, 0x00000000), // DMATOTCOUNTNEXT
            new KeyValuePair<long, uint>(0x168, 0x00000000), // DMAENNEXTCTRL
        };
    }
}
