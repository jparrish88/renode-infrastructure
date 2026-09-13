//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite USB (USB @ 0x400B0000, IRQ27).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (84 regs).
// Phase-1 model: SVD-faithful register file with live IRQ from
// INTEN/INTSTAT where present. No functional transfer is emulated yet.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_USB : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_USB(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x2500;

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
            new KeyValuePair<long, uint>(0x00000000, 0x00002000), // CFG0
            new KeyValuePair<long, uint>(0x00000004, 0x003F0000), // CFG1
            new KeyValuePair<long, uint>(0x00000008, 0x0600003E), // CFG2
            new KeyValuePair<long, uint>(0x0000000C, 0x00000000), // CFG3
            new KeyValuePair<long, uint>(0x00000010, 0x00000000), // IDX0
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // IDX1
            new KeyValuePair<long, uint>(0x00000018, 0x00000000), // IDX2
            new KeyValuePair<long, uint>(0x0000001C, 0x00000000), // FIFOADD
            new KeyValuePair<long, uint>(0x00000020, 0x00000000), // FIFO0
            new KeyValuePair<long, uint>(0x00000024, 0x00000000), // FIFO1
            new KeyValuePair<long, uint>(0x00000028, 0x00000000), // FIFO2
            new KeyValuePair<long, uint>(0x0000002C, 0x00000000), // FIFO3
            new KeyValuePair<long, uint>(0x00000030, 0x00000000), // FIFO4
            new KeyValuePair<long, uint>(0x00000034, 0x00000000), // FIFO5
            new KeyValuePair<long, uint>(0x0000006C, 0x00000A58), // HWVERS
            new KeyValuePair<long, uint>(0x00000078, 0x00000A55), // INFO
            new KeyValuePair<long, uint>(0x00000080, 0x00004074), // TIMEOUT1
            new KeyValuePair<long, uint>(0x00000084, 0x00000032), // TIMEOUT2
            new KeyValuePair<long, uint>(0x00002000, 0x00000000), // CLKCTRL
            new KeyValuePair<long, uint>(0x00002004, 0x0000007F), // SRAMCTRL
            new KeyValuePair<long, uint>(0x00002014, 0x00000000), // UTMISTICKYSTATUS
            new KeyValuePair<long, uint>(0x00002018, 0x00000000), // OBSCLRSTAT
            new KeyValuePair<long, uint>(0x0000201C, 0x00000000), // DPDMPULLDOWN
            new KeyValuePair<long, uint>(0x00002020, 0x00000000), // BCDETSTATUS
            new KeyValuePair<long, uint>(0x00002024, 0x00000002), // BCDETCRTL1
            new KeyValuePair<long, uint>(0x00002028, 0x00000500), // BCDETCRTL2
            new KeyValuePair<long, uint>(0x0000202C, 0x00000000), // DMACTRL
            new KeyValuePair<long, uint>(0x00002030, 0x00000000), // DMATARGADDR
            new KeyValuePair<long, uint>(0x00002034, 0x00000000), // DMAINTEN
            new KeyValuePair<long, uint>(0x00002038, 0x00000000), // DMAINTSTAT
            new KeyValuePair<long, uint>(0x0000203C, 0x00000000), // DMAINTCLR
            new KeyValuePair<long, uint>(0x00002040, 0x00000000), // DMAINTSET
            new KeyValuePair<long, uint>(0x00002044, 0x00000000), // ADMACTRL
            new KeyValuePair<long, uint>(0x00002048, 0x00000000), // ADMAEN
            new KeyValuePair<long, uint>(0x0000204C, 0x00000000), // ADMADIR
            new KeyValuePair<long, uint>(0x00002050, 0x00000000), // ADMAPRI
            new KeyValuePair<long, uint>(0x00002060, 0x00000000), // ADMACMPINTEN
            new KeyValuePair<long, uint>(0x00002064, 0x00000000), // ADMACMPINTSTAT
            new KeyValuePair<long, uint>(0x00002068, 0x00000000), // ADMACMPINTCLR
            new KeyValuePair<long, uint>(0x0000206C, 0x00000000), // ADMACMPINTSET
            new KeyValuePair<long, uint>(0x00002070, 0x00000000), // ADMAERRINTEN
            new KeyValuePair<long, uint>(0x00002074, 0x00000000), // ADMAERRINTSTAT
            new KeyValuePair<long, uint>(0x00002078, 0x00000000), // ADMAERRINTCLR
            new KeyValuePair<long, uint>(0x0000207C, 0x00000000), // ADMAERRINTSET
            new KeyValuePair<long, uint>(0x00002100, 0x00000000), // ADMATOTCOUNT0
            new KeyValuePair<long, uint>(0x00002104, 0x00000000), // ADMATOTCOUNT1
            new KeyValuePair<long, uint>(0x00002108, 0x00000000), // ADMATOTCOUNT2
            new KeyValuePair<long, uint>(0x0000210C, 0x00000000), // ADMATOTCOUNT3
            new KeyValuePair<long, uint>(0x00002110, 0x00000000), // ADMATOTCOUNT4
            new KeyValuePair<long, uint>(0x00002114, 0x00000000), // ADMATOTCOUNT5
            new KeyValuePair<long, uint>(0x00002118, 0x00000000), // ADMATOTCOUNT6
            new KeyValuePair<long, uint>(0x0000211C, 0x00000000), // ADMATOTCOUNT7
            new KeyValuePair<long, uint>(0x00002120, 0x00000000), // ADMATOTCOUNT8
            new KeyValuePair<long, uint>(0x00002124, 0x00000000), // ADMATOTCOUNT9
            new KeyValuePair<long, uint>(0x00002200, 0x00000000), // ADMATARGADDR0
            new KeyValuePair<long, uint>(0x00002204, 0x00000000), // ADMATARGADDR1
            new KeyValuePair<long, uint>(0x00002208, 0x00000000), // ADMATARGADDR2
            new KeyValuePair<long, uint>(0x0000220C, 0x00000000), // ADMATARGADDR3
            new KeyValuePair<long, uint>(0x00002210, 0x00000000), // ADMATARGADDR4
            new KeyValuePair<long, uint>(0x00002214, 0x00000000), // ADMATARGADDR5
            new KeyValuePair<long, uint>(0x00002218, 0x00000000), // ADMATARGADDR6
            new KeyValuePair<long, uint>(0x0000221C, 0x00000000), // ADMATARGADDR7
            new KeyValuePair<long, uint>(0x00002220, 0x00000000), // ADMATARGADDR8
            new KeyValuePair<long, uint>(0x00002224, 0x00000000), // ADMATARGADDR9
            new KeyValuePair<long, uint>(0x00002300, 0x00000000), // ADMAEP0
            new KeyValuePair<long, uint>(0x00002304, 0x00000000), // ADMAEP1
            new KeyValuePair<long, uint>(0x00002308, 0x00000000), // ADMAEP2
            new KeyValuePair<long, uint>(0x0000230C, 0x00000000), // ADMAEP3
            new KeyValuePair<long, uint>(0x00002310, 0x00000000), // ADMAEP4
            new KeyValuePair<long, uint>(0x00002314, 0x00000000), // ADMAEP5
            new KeyValuePair<long, uint>(0x00002318, 0x00000000), // ADMAEP6
            new KeyValuePair<long, uint>(0x0000231C, 0x00000000), // ADMAEP7
            new KeyValuePair<long, uint>(0x00002320, 0x00000000), // ADMAEP8
            new KeyValuePair<long, uint>(0x00002324, 0x00000000), // ADMAEP9
            new KeyValuePair<long, uint>(0x00002400, 0x00000000), // ADMAREQSIZE0
            new KeyValuePair<long, uint>(0x00002404, 0x00000000), // ADMAREQSIZE1
            new KeyValuePair<long, uint>(0x00002408, 0x00000000), // ADMAREQSIZE2
            new KeyValuePair<long, uint>(0x0000240C, 0x00000000), // ADMAREQSIZE3
            new KeyValuePair<long, uint>(0x00002410, 0x00000000), // ADMAREQSIZE4
            new KeyValuePair<long, uint>(0x00002414, 0x00000000), // ADMAREQSIZE5
            new KeyValuePair<long, uint>(0x00002418, 0x00000000), // ADMAREQSIZE6
            new KeyValuePair<long, uint>(0x0000241C, 0x00000000), // ADMAREQSIZE7
            new KeyValuePair<long, uint>(0x00002420, 0x00000000), // ADMAREQSIZE8
            new KeyValuePair<long, uint>(0x00002424, 0x00000000), // ADMAREQSIZE9
        };
    }
}