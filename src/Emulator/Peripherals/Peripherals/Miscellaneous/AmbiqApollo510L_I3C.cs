//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite I3C host controller (MIPI I3C HCI @ 0x40059000, IRQ85).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (HCI v1.1,
// HCIVERSION reset 0x110). Phase-1 model: complete SVD-faithful register
// file (all 69 regs readable/writable at reset values, so discovery and
// polling firmware never faults) with live IRQ85 from the three interrupt
// domains (main INTR, PIO intr, RH intr: status & status-enable &
// signal-enable). PIO transfers (COMMANDQUEUEPORT/XFERDATAPORT/...) accept
// writes and log them, but no bus transactions are emulated yet: there is
// no apollo510dL_evb I3C example vehicle to validate against. Extend with
// command/response ring processing when firmware arrives.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_I3C : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_I3C(IMachine machine) : base(machine)
        {
            Reset();
        }

        public long Size => 0x800;

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
            if(offset == REG_COMMANDQUEUEPORT || offset == REG_RESPONSEQUEUEPORT
                || offset == REG_XFERDATAPORT || offset == REG_IBIPORT)
            {
                this.Log(LogLevel.Info, "I3C WR 0x{0:X} <- 0x{1:X} (PIO transfer port, not emulated yet)", offset, value);
            }
            if(IsIrqRegister(offset))
            {
                UpdateIRQ();
            }
        }

        private void UpdateIRQ()
        {
            var main = ReadDoubleWord(REG_INTRSTATUS) & ReadDoubleWord(REG_INTRSTATUSENABLE) & ReadDoubleWord(REG_INTRSIGNALENABLE);
            var pio = ReadDoubleWord(REG_PIOINTRSTATUS) & ReadDoubleWord(REG_PIOINTRSTATUSENABLE) & ReadDoubleWord(REG_PIOINTRSIGNALENABLE);
            var rh = ReadDoubleWord(REG_RHINTRSTATUS) & ReadDoubleWord(REG_RHINTRSTATUSENABLE) & ReadDoubleWord(REG_RHINTRSIGNALENABLE);
            if((main | pio | rh) != 0)
            {
                IRQ.Set();
            }
            else
            {
                IRQ.Unset();
            }
        }

        private static bool IsIrqRegister(long offset)
        {
            return offset == REG_INTRSTATUS || offset == REG_INTRSTATUSENABLE || offset == REG_INTRSIGNALENABLE
                || offset == REG_PIOINTRSTATUS || offset == REG_PIOINTRSTATUSENABLE || offset == REG_PIOINTRSIGNALENABLE
                || offset == REG_RHINTRSTATUS || offset == REG_RHINTRSTATUSENABLE || offset == REG_RHINTRSIGNALENABLE;
        }

        private const long REG_INTRSTATUS = 0x20;
        private const long REG_INTRSTATUSENABLE = 0x24;
        private const long REG_INTRSIGNALENABLE = 0x28;
        private const long REG_PIOINTRSTATUS = 0x520;
        private const long REG_PIOINTRSTATUSENABLE = 0x524;
        private const long REG_PIOINTRSIGNALENABLE = 0x528;
        private const long REG_RHINTRSTATUS = 0x590;
        private const long REG_RHINTRSTATUSENABLE = 0x594;
        private const long REG_RHINTRSIGNALENABLE = 0x598;
        private const long REG_COMMANDQUEUEPORT = 0x500;
        private const long REG_RESPONSEQUEUEPORT = 0x504;
        private const long REG_XFERDATAPORT = 0x508;
        private const long REG_IBIPORT = 0x50C;

        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000110), // HCIVERSION
            new KeyValuePair<long, uint>(0x04, 0x00000040), // HCCONTROL
            new KeyValuePair<long, uint>(0x08, 0x00000000), // DEVICEADDR
            new KeyValuePair<long, uint>(0x0C, 0x0000044C), // HCCAPABILITIES
            new KeyValuePair<long, uint>(0x10, 0x00000000), // RESETCTRL
            new KeyValuePair<long, uint>(0x14, 0x00000000), // PRESENTSTATE
            new KeyValuePair<long, uint>(0x20, 0x00000000), // INTRSTATUS
            new KeyValuePair<long, uint>(0x24, 0x00000000), // INTRSTATUSENABLE
            new KeyValuePair<long, uint>(0x28, 0x00000000), // INTRSIGNALENABLE
            new KeyValuePair<long, uint>(0x2C, 0x00000000), // INTRFORCE
            new KeyValuePair<long, uint>(0x30, 0x00000000), // DATSECTIONOFFSET
            new KeyValuePair<long, uint>(0x34, 0x00000000), // DCTSECTIONOFFSET
            new KeyValuePair<long, uint>(0x38, 0x00000400), // RINGHEADERSSECTIONOFFSET
            new KeyValuePair<long, uint>(0x3C, 0x00000500), // PIOSECTIONOFFSET
            new KeyValuePair<long, uint>(0x40, 0x00000000), // EXTCAPSSECTIONOFFSET
            new KeyValuePair<long, uint>(0x4C, 0x00000017), // INTCTRLCMDSEN
            new KeyValuePair<long, uint>(0x58, 0x00000000), // IBINOTIFYCTRL
            new KeyValuePair<long, uint>(0x200, 0x00000000), // DAT0
            new KeyValuePair<long, uint>(0x204, 0x00000000), // DAT1
            new KeyValuePair<long, uint>(0x300, 0x00000000), // DCT0
            new KeyValuePair<long, uint>(0x304, 0x00000000), // DCT1
            new KeyValuePair<long, uint>(0x308, 0x00000000), // DCT2
            new KeyValuePair<long, uint>(0x30C, 0x00000000), // DCT3
            new KeyValuePair<long, uint>(0x500, 0x00000000), // COMMANDQUEUEPORT
            new KeyValuePair<long, uint>(0x504, 0x00000000), // RESPONSEQUEUEPORT
            new KeyValuePair<long, uint>(0x508, 0x00000000), // XFERDATAPORT
            new KeyValuePair<long, uint>(0x50C, 0x00000000), // IBIPORT
            new KeyValuePair<long, uint>(0x510, 0x01010101), // QUEUETHLDCTRL
            new KeyValuePair<long, uint>(0x514, 0x01010101), // DATABUFFERTHLDCTRL
            new KeyValuePair<long, uint>(0x518, 0x00000000), // QUEUESIZECTRL
            new KeyValuePair<long, uint>(0x520, 0x00000000), // PIOINTRSTATUS
            new KeyValuePair<long, uint>(0x524, 0x00000000), // PIOINTRSTATUSENABLE
            new KeyValuePair<long, uint>(0x528, 0x00000000), // PIOINTRSIGNALENABLE
            new KeyValuePair<long, uint>(0x52C, 0x00000000), // PIOINTRFORCE
            new KeyValuePair<long, uint>(0x550, 0x10050080), // RHSCONTROL
            new KeyValuePair<long, uint>(0x554, 0x00000800), // RH0OFFSET
            new KeyValuePair<long, uint>(0x558, 0x00000880), // RH1OFFSET
            new KeyValuePair<long, uint>(0x55C, 0x00000900), // RH2OFFSET
            new KeyValuePair<long, uint>(0x560, 0x00000910), // RH3OFFSET
            new KeyValuePair<long, uint>(0x580, 0x80040000), // CRSETUP
            new KeyValuePair<long, uint>(0x584, 0x04000000), // IBISETUP
            new KeyValuePair<long, uint>(0x588, 0x00000000), // CHUNKCONTROL
            new KeyValuePair<long, uint>(0x590, 0x00000000), // RHINTRSTATUS
            new KeyValuePair<long, uint>(0x594, 0x00000000), // RHINTRSTATUSENABLE
            new KeyValuePair<long, uint>(0x598, 0x00000000), // RHINTRSIGNALENABLE
            new KeyValuePair<long, uint>(0x59C, 0x00000000), // RHINTRFORCE
            new KeyValuePair<long, uint>(0x5A0, 0x00000000), // RHSTATUS
            new KeyValuePair<long, uint>(0x5A4, 0x00000000), // RHCONTROL
            new KeyValuePair<long, uint>(0x5A8, 0x00000000), // RHOPERATION1
            new KeyValuePair<long, uint>(0x5AC, 0x00000000), // RHOPERATION2
            new KeyValuePair<long, uint>(0x5B0, 0x00000000), // RHCMDRINGBASELO
            new KeyValuePair<long, uint>(0x5B4, 0x00000000), // RHCMDRINGBASEHI
            new KeyValuePair<long, uint>(0x5B8, 0x00000000), // RHRESPRINGBASELO
            new KeyValuePair<long, uint>(0x5BC, 0x00000000), // RHRESPRINGBASEHI
            new KeyValuePair<long, uint>(0x5C0, 0x00000000), // RHIBISTATUSRINGBASELO
            new KeyValuePair<long, uint>(0x5C4, 0x00000000), // RHIBISTATUSRINGBASEHI
            new KeyValuePair<long, uint>(0x5C8, 0x00000000), // RHIBIDATARINGBASELO
            new KeyValuePair<long, uint>(0x5CC, 0x00000000), // RHIBIDATARINGBASEHI
            new KeyValuePair<long, uint>(0x7A0, 0x00000000), // EXTCAPHEADER
            new KeyValuePair<long, uint>(0x7A4, 0x00000000), // COMPMANUFACTURER
            new KeyValuePair<long, uint>(0x7A8, 0x00000000), // COMPVERSION
            new KeyValuePair<long, uint>(0x7AC, 0x00000000), // COMPTYPE
            new KeyValuePair<long, uint>(0x7B4, 0x00000010), // MASTERCONFIG
            new KeyValuePair<long, uint>(0x7BC, 0x00000000), // I2CURD1SCLLOWCNT
            new KeyValuePair<long, uint>(0x7C0, 0x00000000), // I2CURD1SCLHIGHCNT
            new KeyValuePair<long, uint>(0x7C4, 0x00000000), // I2CURD2SCLLOWCNT
            new KeyValuePair<long, uint>(0x7C8, 0x00000000), // I2CURD2SCLHIGHCNT
            new KeyValuePair<long, uint>(0x7CC, 0x00000000), // I2CURD3SCLLOWCNT
            new KeyValuePair<long, uint>(0x7D0, 0x00000000), // I2CURD3SCLHIGHCNT
        };
    }
}
