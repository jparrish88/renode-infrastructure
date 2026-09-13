//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite OTP (OTP @ 0x40009800).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (4 regs).
// Phase-1 model: SVD-faithful register file.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_OTP : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_OTP(IMachine machine) : base(machine)
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
            new KeyValuePair<long, uint>(0x000002A0, 0x00000000), // RNG
            new KeyValuePair<long, uint>(0x000002B0, 0x00000000), // INTERRUPT
            new KeyValuePair<long, uint>(0x000002C4, 0x00000000), // PTMSTAT
            new KeyValuePair<long, uint>(0x00000300, 0x00000000), // PUFUID
        };
    }
}