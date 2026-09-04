//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System.Text;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Minimal ARM ITM (Instrumentation Trace Macrocell) model for the Apollo510B. Captures printf
    // traffic written to the STIM[0] stimulus register and emits it on Renode's log so firmware
    // console output (am_bsp_itm_printf_enable -> SWO/ITM channel 0) can be observed headlessly.
    public class AmbiqApollo510_ITM : IDoubleWordPeripheral, IBytePeripheral, IKnownSize
    {
        private readonly StringBuilder line = new();

        public AmbiqApollo510_ITM(IMachine machine) { }

        public void Reset() => line.Clear();

        // Covers STIM[0..31] (0x40-0xFC) and the TCR/TER/PRES/LAR control block (< 0xFB0).
        public long Size => 0x1000;

        public uint ReadDoubleWord(long offset)
        {
            DiagRead(offset);
            // am_hal_itm uses a simplified layout: offset 0x0 acts as BOTH the "port ready" status word
            // (firmware busy-waits on it != 0 before writing each char) and the STIM[0] stimulus buffer.
            // Report it as always-ready so that busy-wait exits; also honor standard TCR/PRES offsets.
            switch (offset)
            {
                case 0x00:   // am_hal_itm "ready" status -> non-zero so the wait loop proceeds
                case 0xE00:  // TCR: TE set
                case 0xE51:  // PRES: PREn set (port ready to accept data)
                    return 1;
                default:
                    return 0;   // LSR reads as unlocked, identification regs read 0
            }
        }

        public byte ReadByte(long offset)
        {
            DiagRead(offset);
            return 0;
        }

        private int diagCount;
        private readonly System.Collections.Generic.HashSet<long> seenReadOffsets = new();

        private void DiagRead(long offset)
        {
            if (seenReadOffsets.Add(offset))
            {
                this.Log(LogLevel.Info, "[ITM-RDBG] read off=0x{0:X}", offset);
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            DiagWrite(offset, (uint)value);
            OnStimulus((byte)value, offset);
        }

        public void WriteByte(long offset, byte value)
        {
            DiagWrite(offset, (uint)value);
            OnStimulus(value, offset);
        }

        private void DiagWrite(long offset, uint value)
        {
            if (diagCount < 400)
            {
                this.Log(LogLevel.Info, "[ITM-DBG] write off=0x{0:X} val=0x{1:X}", offset, value);
            }
            diagCount++;
        }

        private void OnStimulus(byte b, long offset)
        {
            // STIM[0] lives at 0x40 per ARMv8-M; 0x00 is tolerated for minimal ITM targets.
            if (offset != 0x40 && offset != 0x00)
            {
                return;
            }

            char c = (char)b;
            if (c == '\n')
            {
                this.Log(LogLevel.Info, "[ITM] {0}", line.ToString().TrimEnd('\r'));
                line.Clear();
            }
            else if (!char.IsControl(c))
            {
                line.Append(c);
            }
        }
    }
}
