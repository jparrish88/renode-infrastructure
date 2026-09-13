//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite CoreSight-side plumbing blocks with their known ID/reset
// values (CPU complex, JEDEC ID page, DWT, FP-extension register file).
// Replaces the apollo510l.repl PythonPeripheral stubs with C# models that
// read the same constants firmware probes; all other offsets are RW-zero.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_CpuComplex : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_CpuComplex(IMachine machine) : base(machine)
        {
            registers = new Dictionary<long, uint>();
            Reset();
        }

        public long Size => 0x1000;

        public override void Reset()
        {
            registers.Clear();
            registers[0x54] = 0x4;
        }

        public override uint ReadDoubleWord(long offset)
        {
            return registers.TryGetValue(offset, out var v) ? v : 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            registers[offset] = value;
        }

        private readonly Dictionary<long, uint> registers;
    }

    public class AmbiqApollo510L_JEDEC : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_JEDEC(IMachine machine) : base(machine)
        {
            registers = new Dictionary<long, uint>();
            Reset();
        }

        public long Size => 0x100;

        public override void Reset()
        {
            registers.Clear();
            foreach(var kv in Id)
            {
                registers[kv.Key] = kv.Value;
            }
        }

        public override uint ReadDoubleWord(long offset)
        {
            return registers.TryGetValue(offset, out var v) ? v : 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            // JEDEC ID page is read-only; writes ignored.
        }

        private static readonly KeyValuePair<long, uint>[] Id = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0xE0, 0x90),
            new KeyValuePair<long, uint>(0xE4, 0xBE),
            new KeyValuePair<long, uint>(0xE8, 0x9),
            new KeyValuePair<long, uint>(0xEC, 0x20),
            new KeyValuePair<long, uint>(0xF0, 0xD),
            new KeyValuePair<long, uint>(0xF4, 0x10),
            new KeyValuePair<long, uint>(0xF8, 0x5),
            new KeyValuePair<long, uint>(0xFC, 0xB1),
        };

        private readonly Dictionary<long, uint> registers;
    }

    public class AmbiqApollo510L_DWT : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_DWT(IMachine machine) : base(machine)
        {
            registers = new Dictionary<long, uint>();
            Reset();
        }

        public long Size => 0x1000;

        public override void Reset()
        {
            registers.Clear();
            registers[0x00] = 0x40000000; // NUMCOMP = 4
        }

        public override uint ReadDoubleWord(long offset)
        {
            return registers.TryGetValue(offset, out var v) ? v : 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            registers[offset] = value;
        }

        private readonly Dictionary<long, uint> registers;
    }

    public class AmbiqApollo510L_FPUExt : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_FPUExt(IMachine machine) : base(machine)
        {
            registers = new Dictionary<long, uint>();
            Reset();
        }

        public long Size => 0x100;

        public override void Reset()
        {
            registers.Clear();
            registers[0x00] = 0xC0000000; // FPCCR-class reset
            registers[0x34] = 0xC0000000; // FPDSCR-class reset
        }

        public override uint ReadDoubleWord(long offset)
        {
            return registers.TryGetValue(offset, out var v) ? v : 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            registers[offset] = value;
        }

        private readonly Dictionary<long, uint> registers;
    }
}
