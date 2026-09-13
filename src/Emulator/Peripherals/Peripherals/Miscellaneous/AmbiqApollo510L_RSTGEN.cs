//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite RSTGEN (RSTGEN @ 0x40000000).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (10 regs).
// Phase-1 model: SVD-faithful register file.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_RSTGEN : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_RSTGEN(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x1000;

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
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // CFG
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // SWPOI
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // SWPOR
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // SIMOBODM
            new KeyValuePair<long, uint>(0x00000018, 0x00000000), // SWRESET
            new KeyValuePair<long, uint>(0x00000200, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x00000204, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x00000208, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x0000020C, 0x00000000), // INTSET
            new KeyValuePair<long, uint>(0x0000885C, 0x00000000), // STAT
        };
    }
}