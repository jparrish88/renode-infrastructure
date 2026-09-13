//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite USBPHY (USBPHY @ 0x400B4000, IRQnone).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (34 regs).
// Phase-1 model: SVD-faithful register file with live IRQ from
// INTEN/INTSTAT where present. No functional transfer is emulated yet.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_USBPHY : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_USBPHY(IMachine machine) : base(machine)
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
            new KeyValuePair<long, uint>(0x00000000, 0x00000019), // REG00
            new KeyValuePair<long, uint>(0x00000004, 0x0000006F), // REG04
            new KeyValuePair<long, uint>(0x00000008, 0x0000000C), // REG08
            new KeyValuePair<long, uint>(0x0000000C, 0x000000D4), // REG0C
            new KeyValuePair<long, uint>(0x00000010, 0x00000057), // REG10
            new KeyValuePair<long, uint>(0x00000014, 0x00000091), // REG14
            new KeyValuePair<long, uint>(0x00000018, 0x00000001), // REG18
            new KeyValuePair<long, uint>(0x0000001C, 0x00000000), // REG1C
            new KeyValuePair<long, uint>(0x00000020, 0x00000004), // REG20
            new KeyValuePair<long, uint>(0x00000024, 0x0000000C), // REG24
            new KeyValuePair<long, uint>(0x00000028, 0x00000013), // REG28
            new KeyValuePair<long, uint>(0x0000002C, 0x00000080), // REG2C
            new KeyValuePair<long, uint>(0x00000030, 0x00000055), // REG30
            new KeyValuePair<long, uint>(0x00000034, 0x00000055), // REG34
            new KeyValuePair<long, uint>(0x00000038, 0x00000055), // REG38
            new KeyValuePair<long, uint>(0x0000003C, 0x00000081), // REG3C
            new KeyValuePair<long, uint>(0x00000040, 0x00000040), // REG40
            new KeyValuePair<long, uint>(0x00000044, 0x00000041), // REG44
            new KeyValuePair<long, uint>(0x00000048, 0x00000000), // REG48
            new KeyValuePair<long, uint>(0x0000004C, 0x00000000), // REG4C
            new KeyValuePair<long, uint>(0x00000050, 0x00000000), // REG50
            new KeyValuePair<long, uint>(0x00000054, 0x00000000), // REG54
            new KeyValuePair<long, uint>(0x00000058, 0x00000000), // REG58
            new KeyValuePair<long, uint>(0x0000005C, 0x00000000), // REG5C
            new KeyValuePair<long, uint>(0x00000060, 0x00000000), // REG60
            new KeyValuePair<long, uint>(0x00000064, 0x00000020), // REG64
            new KeyValuePair<long, uint>(0x00000068, 0x00000064), // REG68
            new KeyValuePair<long, uint>(0x0000006C, 0x00000064), // REG6C
            new KeyValuePair<long, uint>(0x00000070, 0x00000000), // REG70
            new KeyValuePair<long, uint>(0x00000074, 0x00000014), // REG74
            new KeyValuePair<long, uint>(0x00000078, 0x00000000), // REG78
            new KeyValuePair<long, uint>(0x0000007C, 0x000000A8), // REG7C
            new KeyValuePair<long, uint>(0x00000080, 0x00000012), // REG80
            new KeyValuePair<long, uint>(0x00000084, 0x00000000), // REG84
        };
    }
}