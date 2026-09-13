//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite VCOMP (VCOMP @ 0x40011C00, IRQ3).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (7 regs).
// Phase-1 model: SVD-faithful register file.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_VCOMP : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_VCOMP(IMachine machine) : base(machine)
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
            if(registers.TryGetValue(offset, out var value)) return value;
            return 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            registers[offset] = value;
            if(IsIrqRegister(offset)) UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            uint en=0, stat=0;
            if(registers.TryGetValue(0x100, out var v1)) en|=v1;
            if(registers.TryGetValue(0x104, out var v2)) stat|=v2;
            if((en & stat)!=0) IRQ.Set(); else IRQ.Unset();
        }
        private static bool IsIrqRegister(long o) => o==0x100||o==0x104||o==0x108||o==0x10C;

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // CFG
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // STAT
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // PWDKEY
            new KeyValuePair<long, uint>(0x00000200, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x00000204, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x00000208, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x0000020C, 0x00000000), // INTSET
        };
    }
}