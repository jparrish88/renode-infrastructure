//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 FPIO (FPIO @ 0x40011000).
// NEW file for the Apollo510 family.
//
// Register map + reset values from SVD/apollo510.svd (49 regs: 7 GPIO
// banks; the Lite FPIO has 4 banks at different offsets and is NOT
// layout-compatible). Phase-1 model: plain register file. No IRQ.
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510_FPIO : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510_FPIO(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x100;

        public override void Reset()
        {
            registers.Clear();
            foreach(var kv in Resets)
            {
                registers[kv.Key] = kv.Value;
            }
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
        }

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000000), // RD0
            new KeyValuePair<long, uint>(0x04, 0x00000000), // RD1
            new KeyValuePair<long, uint>(0x08, 0x00000000), // RD2
            new KeyValuePair<long, uint>(0x0C, 0x00000000), // RD3
            new KeyValuePair<long, uint>(0x10, 0x00000000), // RD4
            new KeyValuePair<long, uint>(0x14, 0x00000000), // RD5
            new KeyValuePair<long, uint>(0x18, 0x00000000), // RD6
            new KeyValuePair<long, uint>(0x1C, 0x00000000), // WT0
            new KeyValuePair<long, uint>(0x20, 0x00000000), // WT1
            new KeyValuePair<long, uint>(0x24, 0x00000000), // WT2
            new KeyValuePair<long, uint>(0x28, 0x00000000), // WT3
            new KeyValuePair<long, uint>(0x2C, 0x00000000), // WT4
            new KeyValuePair<long, uint>(0x30, 0x00000000), // WT5
            new KeyValuePair<long, uint>(0x34, 0x00000000), // WT6
            new KeyValuePair<long, uint>(0x38, 0x00000000), // WTS0
            new KeyValuePair<long, uint>(0x3C, 0x00000000), // WTS1
            new KeyValuePair<long, uint>(0x40, 0x00000000), // WTS2
            new KeyValuePair<long, uint>(0x44, 0x00000000), // WTS3
            new KeyValuePair<long, uint>(0x48, 0x00000000), // WTS4
            new KeyValuePair<long, uint>(0x4C, 0x00000000), // WTS5
            new KeyValuePair<long, uint>(0x50, 0x00000000), // WTS6
            new KeyValuePair<long, uint>(0x54, 0x00000000), // WTC0
            new KeyValuePair<long, uint>(0x58, 0x00000000), // WTC1
            new KeyValuePair<long, uint>(0x5C, 0x00000000), // WTC2
            new KeyValuePair<long, uint>(0x60, 0x00000000), // WTC3
            new KeyValuePair<long, uint>(0x64, 0x00000000), // WTC4
            new KeyValuePair<long, uint>(0x68, 0x00000000), // WTC5
            new KeyValuePair<long, uint>(0x6C, 0x00000000), // WTC6
            new KeyValuePair<long, uint>(0x70, 0x00000000), // EN0
            new KeyValuePair<long, uint>(0x74, 0x00000000), // EN1
            new KeyValuePair<long, uint>(0x78, 0x00000000), // EN2
            new KeyValuePair<long, uint>(0x7C, 0x00000000), // EN3
            new KeyValuePair<long, uint>(0x80, 0x00000000), // EN4
            new KeyValuePair<long, uint>(0x84, 0x00000000), // EN5
            new KeyValuePair<long, uint>(0x88, 0x00000000), // EN6
            new KeyValuePair<long, uint>(0x8C, 0x00000000), // ENS0
            new KeyValuePair<long, uint>(0x90, 0x00000000), // ENS1
            new KeyValuePair<long, uint>(0x94, 0x00000000), // ENS2
            new KeyValuePair<long, uint>(0x98, 0x00000000), // ENS3
            new KeyValuePair<long, uint>(0x9C, 0x00000000), // ENS4
            new KeyValuePair<long, uint>(0xA0, 0x00000000), // ENS5
            new KeyValuePair<long, uint>(0xA4, 0x00000000), // ENS6
            new KeyValuePair<long, uint>(0xA8, 0x00000000), // ENC0
            new KeyValuePair<long, uint>(0xAC, 0x00000000), // ENC1
            new KeyValuePair<long, uint>(0xB0, 0x00000000), // ENC2
            new KeyValuePair<long, uint>(0xB4, 0x00000000), // ENC3
            new KeyValuePair<long, uint>(0xB8, 0x00000000), // ENC4
            new KeyValuePair<long, uint>(0xBC, 0x00000000), // ENC5
            new KeyValuePair<long, uint>(0xC0, 0x00000000), // ENC6
        };
    }
}
