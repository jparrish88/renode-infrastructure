//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Crypto
{
    public class AmbiqApollo510_Crypto : IDoubleWordPeripheral, IKnownSize, IPeripheral
    {
        private const long SramSize = 0x2000; // 8 KB internal crypto SRAM

        private readonly byte[] sram = new byte[0x2000];
        private readonly IMachine machine;
        private readonly IBusController sysbusCtrl;

        // DMA state — source/dest addresses set by firmware before trigger
        private uint dinMemAddr;      // SRCLLIWORD0 (external memory source for DIN)
        private long dinSramOffset;   // SRAMSRCADDR (destination in crypto SRAM)
        private uint doutMemAddr;     // DSTLLIWORD0 (external memory dest for DOUT)
        private long doutSramOffset;  // SRAMDESTADDR (source in crypto SRAM)

        // Busy flags: true = idle/ready, false = transfer in progress
        private bool dinMemReady = true;   // DINMEMDMABUSY @ 0xC20 (1=idle)
        private bool dinSramReady = true;  // DINSRAMDMABUSY @ 0xC38 (1=idle)
        private bool doutMemReady = true;  // DOUTMEMDMABUSY @ 0xD20 (1=idle)
        private bool doutSramReady = true; // DOUTSRAMDMABUSY @ 0xD38 (1=idle)

        // CPU SRAM access state
        private long sramCpuAddr;         // SRAMADDR @ 0xF04
        private long pkaSramAddr;         // PKASRAMADDR @ 0xD4

        public AmbiqApollo510_Crypto(IMachine machine)
        {
            this.machine = machine;
            sysbusCtrl = machine.GetSystemBus(this);
        }

        public long Size => SramSize;

        public void Reset()
        {
            Array.Clear(sram, 0, sram.Length);
            dinMemReady = true;
            dinSramReady = true;
            doutMemReady = true;
            doutSramReady = true;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch (offset)
            {
                // --- DIN DMA setup ---
                case 0xC28: dinMemAddr = value; break;              // SRCLLIWORD0
                case 0xC30: dinSramOffset = value & (SramSize - 1); break; // SRAMSRCADDR

                // --- DIN DMA TRIGGER ---
                case 0xC34: PerformDinDma(value); break;            // DINSRAMBYTESLEN

                // --- DOUT DMA setup ---
                case 0xD28: doutMemAddr = value; break;             // DSTLLIWORD0
                case 0xD30: doutSramOffset = value & (SramSize - 1); break; // SRAMDESTADDR

                // --- DOUT DMA TRIGGER ---
                case 0xD34: PerformDoutDma(value); break;           // DOUTSRAMBYTESLEN

                // --- CPU access to crypto SRAM via data port ---
                case 0xF00:
                    {
                        var off = sramCpuAddr & (SramSize - 1);
                        if (off + 4 <= SramSize)
                            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, sram, (int)off, 4);
                    }
                    break;
                case 0xF04: sramCpuAddr = value; break;              // SRAMADDR

                // --- PKA SRAM access (word-addressed large-integer ops) ---
                case 0xD4: pkaSramAddr = value & ~3u; break;         // PKASRAMADDR
                case 0xDC:
                    {
                        var off = pkaSramAddr & (SramSize - 1);
                        if (off + 4 <= SramSize)
                            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, sram, (int)off, 4);
                    }
                    break;

                default: break;
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            switch (offset)
            {
                // --- Clock status / enable (all report ready per SVD defaults) ---
                case 0x810: return 1;          // AESCLKENABLE
                case 0x818: return 1;          // HASHCLKENABLE
                case 0x81C: return 1;          // PKACLKENABLE
                case 0x820: return 1;          // DMACLKENABLE
                case 0x824: return 0x18D;      // CLKSTATUS (AES|HASH|PKA|CHACHA|DMA)

                // --- OPCODE (PKA op = ADD at reset) ---
                case 0x80: return 0x20000000;

                // --- TRNG / RNG ---
                case 0x10C: return 4;          // TRNGCONFIG
                case 0x1C0: return 0x40;       // RNGVERSION

                // --- CHACHACLKENABLE ---
                case 0x858: return 1;

                // --- HOSTCCISIDLE @ 0xA7C: CC idle status. Bit0 (HOSTCCISIDLE) must read 1 so that
                //     am_bsp_itm_printf_enable()/am_hal_dcu_swo_enable() pass their access gate and the
                //     CRYPTO_CC_IS_IDLE() busy-wait exits, which is what attaches ITM to stdio for SWO.
                case 0xA7C: return 0x3B9;   // all sub-units idle (HOSTCCISIDLE|AHBISIDLE|NVMARB|NVM|RNG|PKA|CRYPTO)

                // --- DIN DMA status (1 = idle/ready) ---
                case 0xC20: return (uint)(dinMemReady ? 1 : 0);   // DINMEMDMABUSY
                case 0xC38: return (uint)(dinSramReady ? 1 : 0);  // DINSRAMDMABUSY
                case 0xC3C: return 1;                        // DINSRAMENDIANNESS

                // --- DOUT DMA status (1 = idle/ready) ---
                case 0xD20: return (uint)(doutMemReady ? 1 : 0);  // DOUTMEMDMABUSY
                case 0xD38: return (uint)(doutSramReady ? 1 : 0); // DOUTSRAMDMABUSY
                case 0xD3C: return 1;                          // DOUTSRAMENDIANNESS

                // --- CPU SRAM data port ---
                case 0xF00:
                    {
                        var off = sramCpuAddr & (SramSize - 1);
                        if (off + 4 <= SramSize)
                            return BitConverter.ToUInt32(sram, (int)off);
                        return 0;
                    }
                case 0xF04: return unchecked((uint)sramCpuAddr);   // SRAMADDR
                case 0xF08: return 1;                             // SRAMDATAREADY

                // --- PKA SRAM data read ---
                case 0xE0:
                    {
                        var off = pkaSramAddr & (SramSize - 1);
                        if (off + 4 <= SramSize)
                            return BitConverter.ToUInt32(sram, (int)off);
                        return 0;
                    }

                // --- CRYPTOBUSY: always 0 (no real crypto ops yet) ---
                case 0x910: return 0;

                // --- CoreSight / vendor ID registers ---
                case 0xFD0: return 1;         // JEP106 PPI part
                case 0xFE4: return 0x40;      // PPI1
                case 0xFE8: return 0xB;       // PPI2
                case 0xFF0: return 0xD;       // PRMBL0
                case 0xFF4: return 0xF00;     // CLASS=0xF (PRMBL1)
                case 0xFF8: return 5;         // PRMBL2
                case 0xFFC: return 0xB100;    // PRMBL3

                default: return 0;
            }
        }

        private void PerformDinDma(uint byteCount)
        {
            if (byteCount == 0 || byteCount > SramSize - dinSramOffset)
                return;

            dinMemReady = false;
            dinSramReady = false;

            for (int i = 0; i < (int)byteCount; i++)
            {
                sram[dinSramOffset + i] = sysbusCtrl.ReadByte((ulong)(dinMemAddr + i), context: this);
            }

            dinMemReady = true;
            dinSramReady = true;
        }

        private void PerformDoutDma(uint byteCount)
        {
            if (byteCount == 0 || byteCount > SramSize - doutSramOffset)
                return;

            doutMemReady = false;
            doutSramReady = false;

            for (int i = 0; i < (int)byteCount; i++)
            {
                sysbusCtrl.WriteByte((ulong)(doutMemAddr + i), sram[doutSramOffset + i], context: this);
            }

            doutMemReady = true;
            doutSramReady = true;
        }
    }
}
