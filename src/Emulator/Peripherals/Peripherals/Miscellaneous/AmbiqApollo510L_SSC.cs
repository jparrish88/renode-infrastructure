//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite SSC (SSC @ 0x40005000).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (6 regs).
// Phase-1 model: SVD-faithful register file.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_SSC : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_SSC(IMachine machine) : base(machine)
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
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // SRLOCKS
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // SSLOCKS
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // FLASHSPROT0
            new KeyValuePair<long, uint>(0x0000000C, 0x00000000), // FLASHSPROT1
            new KeyValuePair<long, uint>(0x00000010, 0x00000000), // FLASHSPROT2
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // FLASHSPROT3
        };
    }
}