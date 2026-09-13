//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite SCM (SCM @ 0x40040000).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (12 regs).
// Phase-1 model: SVD-faithful register file.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_SCM : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_SCM(IMachine machine) : base(machine)
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
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // SCMCNTRCTRL1
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // SCMCNTRCTRL2
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // LPTHRESHVDDS
            new KeyValuePair<long, uint>(0x0000000C, 0x00000000), // LPTHRESHVDDF
            new KeyValuePair<long, uint>(0x00000010, 0x00000000), // LPTHRESHVDDC
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // LPTHRESHVDDCLV
            new KeyValuePair<long, uint>(0x00000018, 0x00000000), // LPTHRESHVDDRF
            new KeyValuePair<long, uint>(0x0000001C, 0x00000000), // LPHYSTCNT
            new KeyValuePair<long, uint>(0x00000020, 0x00000000), // ACTTHRESH1
            new KeyValuePair<long, uint>(0x00000024, 0x00000000), // ACTTHRESH2
            new KeyValuePair<long, uint>(0x00000028, 0x00000000), // ACTTHRESH3
            new KeyValuePair<long, uint>(0x0000002C, 0x00000000), // LPSTAT
        };
    }
}