//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite FPIO (FPIO @ 0x40013800).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (28 regs).
// Phase-1 model: SVD-faithful register file.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_FPIO : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_FPIO(IMachine machine) : base(machine)
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
            if(registers.TryGetValue(offset, out var value)) return value;
            return 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            registers[offset] = value;
        }

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // RD0
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // RD1
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // RD2
            new KeyValuePair<long, uint>(0x0000000C, 0x00000000), // RD3
            new KeyValuePair<long, uint>(0x00000010, 0x00000000), // WT0
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // WT1
            new KeyValuePair<long, uint>(0x00000018, 0x00000000), // WT2
            new KeyValuePair<long, uint>(0x0000001C, 0x00000000), // WT3
            new KeyValuePair<long, uint>(0x00000020, 0x00000000), // WTS0
            new KeyValuePair<long, uint>(0x00000024, 0x00000000), // WTS1
            new KeyValuePair<long, uint>(0x00000028, 0x00000000), // WTS2
            new KeyValuePair<long, uint>(0x0000002C, 0x00000000), // WTS3
            new KeyValuePair<long, uint>(0x00000030, 0x00000000), // WTC0
            new KeyValuePair<long, uint>(0x00000034, 0x00000000), // WTC1
            new KeyValuePair<long, uint>(0x00000038, 0x00000000), // WTC2
            new KeyValuePair<long, uint>(0x0000003C, 0x00000000), // WTC3
            new KeyValuePair<long, uint>(0x00000040, 0x00000000), // EN0
            new KeyValuePair<long, uint>(0x00000044, 0x00000000), // EN1
            new KeyValuePair<long, uint>(0x00000048, 0x00000000), // EN2
            new KeyValuePair<long, uint>(0x0000004C, 0x00000000), // EN3
            new KeyValuePair<long, uint>(0x00000050, 0x00000000), // ENS0
            new KeyValuePair<long, uint>(0x00000054, 0x00000000), // ENS1
            new KeyValuePair<long, uint>(0x00000058, 0x00000000), // ENS2
            new KeyValuePair<long, uint>(0x0000005C, 0x00000000), // ENS3
            new KeyValuePair<long, uint>(0x00000060, 0x00000000), // ENC0
            new KeyValuePair<long, uint>(0x00000064, 0x00000000), // ENC1
            new KeyValuePair<long, uint>(0x00000068, 0x00000000), // ENC2
            new KeyValuePair<long, uint>(0x0000006C, 0x00000000), // ENC3
        };
    }
}