//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite SDIO0 (SDIO0 @ 0x40070000, IRQ26).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (32 regs).
// Phase-1 model: SVD-faithful register file with live IRQ from
// INTEN/INTSTAT where present. No functional transfer is emulated yet.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_SDIO : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_SDIO(IMachine machine) : base(machine)
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
            if(IsIrqRegister(offset)) UpdateIRQ();
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
            return offset == 0x100 || offset == 0x104 || offset == 0x108 || offset == 0x10C || offset == 0x20 || offset == 0x24 || offset == 0x28 || offset == 0x2C;
        }

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // SDMA
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // BLOCK
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // ARGUMENT1
            new KeyValuePair<long, uint>(0x0000000C, 0x00000000), // TRANSFER
            new KeyValuePair<long, uint>(0x00000010, 0x00000000), // RESPONSE0
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // RESPONSE1
            new KeyValuePair<long, uint>(0x00000018, 0x00000000), // RESPONSE2
            new KeyValuePair<long, uint>(0x0000001C, 0x00000000), // RESPONSE3
            new KeyValuePair<long, uint>(0x00000020, 0x00000000), // BUFFER
            new KeyValuePair<long, uint>(0x00000024, 0x1FF00000), // PRESENT
            new KeyValuePair<long, uint>(0x00000028, 0x00800000), // HOSTCTRL1
            new KeyValuePair<long, uint>(0x0000002C, 0x00000000), // CLOCKCTRL
            new KeyValuePair<long, uint>(0x00000030, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x00000034, 0x00000000), // INTENABLE
            new KeyValuePair<long, uint>(0x00000038, 0x00000000), // INTSIG
            new KeyValuePair<long, uint>(0x0000003C, 0x00000000), // AUTO
            new KeyValuePair<long, uint>(0x00000040, 0x00000000), // CAPABILITIES0
            new KeyValuePair<long, uint>(0x00000044, 0x00000000), // CAPABILITIES1
            new KeyValuePair<long, uint>(0x00000048, 0x00000000), // MAXIMUM0
            new KeyValuePair<long, uint>(0x0000004C, 0x00000000), // MAXIMUM1
            new KeyValuePair<long, uint>(0x00000050, 0x00000000), // FORCE
            new KeyValuePair<long, uint>(0x00000054, 0x00000000), // ADMA
            new KeyValuePair<long, uint>(0x00000058, 0x00000000), // ADMALOWD
            new KeyValuePair<long, uint>(0x0000005C, 0x00000000), // ADMAHIWD
            new KeyValuePair<long, uint>(0x00000060, 0x00000000), // PRESET0
            new KeyValuePair<long, uint>(0x00000064, 0x00000000), // PRESET1
            new KeyValuePair<long, uint>(0x00000068, 0x00000000), // PRESET2
            new KeyValuePair<long, uint>(0x0000006C, 0x00000000), // PRESET3
            new KeyValuePair<long, uint>(0x00000070, 0x00000000), // BOOTTOCTRL
            new KeyValuePair<long, uint>(0x00000078, 0x00000000), // VENDOR
            new KeyValuePair<long, uint>(0x000000FC, 0x0A020000), // SLOTSTAT
            new KeyValuePair<long, uint>(0x00000100, 0x00000000), // CLKOUTCFG
        };
    }
}