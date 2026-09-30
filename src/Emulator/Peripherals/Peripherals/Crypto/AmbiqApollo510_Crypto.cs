//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
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

        // DCU enable registers (64-bit, read by am_hal_dcu_get via gpDcuEnable=&CRYPTO->HOSTDCUEN2)
        private uint dcuEn0;              // HOSTDCUEN0 @ 0x1E00
        private uint dcuEn1;              // HOSTDCUEN1 @ 0x1E04
        private uint dcuEn2;              // HOSTDCUEN2 @ 0x1E08 (low word)
        private uint dcuEn3;              // HOSTDCUEN3 @ 0x1E0C (high word)

        // DCU lock registers (am_hal_dcu_get reads gpDcuLock=&CRYPTO->HOSTDCULOCK2)
        private uint dcuLock0;            // HOSTDCULOCK0 @ 0x1E10
        private uint dcuLock1;            // HOSTDCULOCK1 @ 0x1E14
        private uint dcuLock2;            // HOSTDCULOCK2 @ 0x1E18 (low word)
        private uint dcuLock3;            // HOSTDCULOCK3 @ 0x1E1C (high word)

        // LCS (Logic Configuration Set) register, read by the banner as CRYPTO->LCSREG_b.LCSREG
        private uint lcsReg;              // LCSREG @ 0x1F14

        // TRNG/RNG (CryptoCell-312 LLF_RND TRNG startup + poll loops need
        // stored readback and VALID; SVD reset values where known)
        private uint rngImr = 0x3F;       // RNGIMR @ 0x100
        private uint trngConfig = 4;      // TRNGCONFIG @ 0x10C (keep legacy readback)
        private uint rndSourceEnable;     // RNDSOURCEENABLE @ 0x12C
        private uint sampleCnt1 = 0xFFFF; // SAMPLECNT1 @ 0x130
        private uint trngDebugControl;    // TRNGDEBUGCONTROL @ 0x138
        private uint rngClkEnable;        // RNGCLKENABLE @ 0x1C4
        private uint rngDmaEnable;        // RNGDMAENABLE @ 0x1C8
        private uint rngDmaSramAddr;      // RNGDMASRAMADDR @ 0x1D0
        private uint rngWatchdogVal;      // RNGWATCHDOGVAL @ 0x1D8
        private uint hostAoLockBits = 0x80; // HOSTAOLOCKBITS @ 0x1E34 (SVD reset; R/W for readback-verify)
        private bool symDmaCompleted;     // HOST_IRR SYM_DMA_COMPLETED (bit 11), latched on DMA done
        private readonly Random trngRandom = new Random();

        // P0-1 FIX stage 1: HASH digest/state + AES key/control storage (readback-verify;
        // computation in stage 2). HASH_H0-H15 @ 0x640-0x67C hold IV in, digest out.
        private readonly uint[] hashH = new uint[16];
        private readonly uint[] aesKey0 = new uint[8]; // AES_KEY_0_0..7 @ 0x400-0x41C
        private uint aesControl;      // AES_CONTROL @ 0x4C0
        private uint aesRemaining;    // AES_REMAINING_BYTES @ 0x4BC
        private uint hashControl;     // HASH_CONTROL @ 0x7C0
        private uint hashPadEn;       // HASH_PAD_EN @ 0x7C4
        private uint hashPadCfg;      // HASH_PAD_CFG @ 0x7C8
        private uint hashCurLen0;     // HASH_CUR_LEN_0 @ 0x7CC
        private uint hashCurLen1;     // HASH_CUR_LEN_1 @ 0x7D0
        private uint hashSelAesMac;   // HASH_SEL_AES_MAC @ 0x6A4
        private uint autoHwPadding;   // AUTO_HW_PADDING @ 0x684

        // P0-1 FIX stage 1b: exact computation. Engine demux mirrors the HW:
        // the last CONTROL written (AES vs HASH) selects which engine the
        // shared DIN path feeds. HASH streams (multi-part safe); AES-ECB
        // executes when a DIN trigger arrives with both sides programmed.
        private bool aesArmed;
        private bool hashArmed;
        private AmbiqCryptoEngines.Sha256Stream shaStream;

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
            dcuEn2 = 0;
            dcuEn3 = 0;
            dcuEn0 = 0;
            dcuEn1 = 0;
            dcuLock0 = 0;
            dcuLock1 = 0;
            dcuLock2 = 0;
            dcuLock3 = 0;
            lcsReg = 0;
            rngImr = 0x3F;
            trngConfig = 4;
            rndSourceEnable = 0;
            sampleCnt1 = 0xFFFF;
            trngDebugControl = 0;
            rngClkEnable = 0;
            rngDmaEnable = 0;
            rngDmaSramAddr = 0;
            rngWatchdogVal = 0;
            hostAoLockBits = 0x80;
            symDmaCompleted = false;
            Array.Clear(hashH, 0, hashH.Length);
            Array.Clear(aesKey0, 0, aesKey0.Length);
            aesControl = 0;
            aesRemaining = 0;
            hashControl = 0;
            hashPadEn = 0;
            hashPadCfg = 0;
            hashCurLen0 = 0;
            hashCurLen1 = 0;
            hashSelAesMac = 0;
            autoHwPadding = 0;
            aesArmed = false;
            hashArmed = false;
            shaStream = null;
        }

        // TEMP-TRACE2 (P0-1 fix dev, PARKED 2026-09-22 per user: crypto saved for last).
        // Silenced via TraceEnable=false so tasks P1-4..P2-8 run on quiet logs.
        // Functional fixes below (PKA_DONE/PIPE_RDY/hashH/aes storage/engines) STAY.
        private const bool TraceEnable2 = false;
        private readonly System.Collections.Generic.HashSet<long> traceSeen2 = new System.Collections.Generic.HashSet<long>();
        private readonly System.Collections.Generic.Dictionary<long, long> traceRd2 = new System.Collections.Generic.Dictionary<long, long>();
        private readonly System.Collections.Generic.Dictionary<long, long> traceWr2 = new System.Collections.Generic.Dictionary<long, long>();
        private long traceTot2 = 0;
        private void Trace2(long offset, bool isRead, uint value)
        {
            if(!TraceEnable2) return;
            var cnt = isRead ? traceRd2 : traceWr2;
            cnt.TryGetValue(offset, out var nn);
            cnt[offset] = nn + 1;
            if(traceSeen2.Add(isRead ? offset : (offset | (1L << 40))))
            {
                this.Log(Antmicro.Renode.Logging.LogLevel.Warning, "CRYPTOTRACE2 {0} 0x{1:X} =0x{2:X}", isRead ? "RD" : "WR", offset, value);
            }
            if((++traceTot2 % 1000) == 0)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach(var kv in traceRd2)
                    if(kv.Value > 3) parts.Add(string.Format("R{0:X}:{1}", kv.Key, kv.Value));
                foreach(var kv in traceWr2)
                    if(kv.Value > 3) parts.Add(string.Format("W{0:X}:{1}", kv.Key, kv.Value));
                this.Log(Antmicro.Renode.Logging.LogLevel.Warning, "CRYPTOTRACE2 total={0} hot=[{1}]", traceTot2, string.Join(" ", parts));
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            Trace2(offset, false, value);
            switch (offset)
            {
                // --- AES key bank 0 (store for readback-verify; computation = stage 2) ---
                case 0x400: case 0x404: case 0x408: case 0x40C:
                case 0x410: case 0x414: case 0x418: case 0x41C:
                    aesKey0[(offset - 0x400) / 4] = value;
                    break;
                case 0x4BC: aesRemaining = value; break;  // AES_REMAINING_BYTES
                case 0x4C0: // AES_CONTROL: arm the AES engine, disarm HASH
                    aesControl = value;
                    aesArmed = true;
                    hashArmed = false;
                    break;

                // --- HASH engine config/state (store for readback-verify) ---
                case 0x640: case 0x644: case 0x648: case 0x64C:
                case 0x650: case 0x654: case 0x658: case 0x65C:
                case 0x660: case 0x664: case 0x668: case 0x66C:
                case 0x670: case 0x674: case 0x678: case 0x67C:
                    hashH[(offset - 0x640) / 4] = value;
                    shaStream = null; // re-staging the IV abandons any live stream
                    break;
                case 0x7C0: // HASH_CONTROL: arm the HASH engine (fresh session), disarm AES
                    hashControl = value;
                    hashArmed = true;
                    aesArmed = false;
                    shaStream = null;
                    if(value == 0x2) // SHA-256: HW auto-loads the standard IV on mode select
                        Array.Copy(AmbiqCryptoEngines.Sha256IV, hashH, 8);
                    break;
                case 0x7C4: hashPadEn = value; break;     // HASH_PAD_EN
                case 0x7C8: hashPadCfg = value; break;    // HASH_PAD_CFG
                case 0x7CC: hashCurLen0 = value; break;   // HASH_CUR_LEN_0
                case 0x7D0: hashCurLen1 = value; break;   // HASH_CUR_LEN_1
                case 0x6A4: hashSelAesMac = value; break; // HASH_SEL_AES_MAC
                case 0x684: autoHwPadding = value; break; // AUTO_HW_PADDING

                // --- DIN DMA setup ---
                case 0xC28: dinMemAddr = value; break;              // SRCLLIWORD0
                case 0xC30: dinSramOffset = value & (SramSize - 1); break; // SRAMSRCADDR

                // --- DIN DMA TRIGGER ---
                case 0xC2C: // SRCLLIWORD1 (LLI DMA trigger)
                    symDmaCompleted = true;
                    FeedDinEngines(value); // LLI WORD1 carries the transfer length
                    break;
                case 0xC34: // DINSRAMBYTESLEN (a zero-length trigger is complete by definition)
                    symDmaCompleted = true;
                    PerformDinDma(value);
                    FeedDinEngines(value);
                    break;

                // --- DOUT DMA setup ---
                case 0xD28: doutMemAddr = value; break;             // DSTLLIWORD0
                case 0xD30: doutSramOffset = value & (SramSize - 1); break; // SRAMDESTADDR

                // --- DOUT DMA TRIGGER ---
                case 0xD2C: // DSTLLIWORD1 (LLI DMA trigger)
                    symDmaCompleted = true;
                    break;
                case 0xD34: // DOUTSRAMBYTESLEN (a zero-length trigger is complete by definition)
                    symDmaCompleted = true;
                    PerformDoutDma(value);
                    break;

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

                // --- DCU enable (am_hal_dcu_get reads these as a 64-bit value) ---
                case 0x1E00: dcuEn0 = value; break;    // HOSTDCUEN0
                case 0x1E04: dcuEn1 = value; break;    // HOSTDCUEN1
                case 0x1E08: dcuEn2 = value; break;    // HOSTDCUEN2 (low word)
                case 0x1E0C: dcuEn3 = value; break;    // HOSTDCUEN3 (high word)

                // --- DCU lock ---
                case 0x1E10: dcuLock0 = value; break;  // HOSTDCULOCK0
                case 0x1E14: dcuLock1 = value; break;  // HOSTDCULOCK1
                case 0x1E18: dcuLock2 = value; break;  // HOSTDCULOCK2 (low word)
                case 0x1E1C: dcuLock3 = value; break;  // HOSTDCULOCK3 (high word)

                // --- LCS register (programmable for the programmed-chip scenario) ---
                case 0x1F14: lcsReg = value; break;    // LCSREG

                // --- TRNG/RNG (store for readback-verify poll loops) ---
                case 0x100: rngImr = value; break;          // RNGIMR
                case 0x108: break;                          // RNGICR (W1C, ISR computed)
                case 0x10C: trngConfig = value; break;      // TRNGCONFIG
                case 0x12C: rndSourceEnable = value; break; // RNDSOURCEENABLE
                case 0x130: sampleCnt1 = value; break;      // SAMPLECNT1
                case 0x138: trngDebugControl = value; break;// TRNGDEBUGCONTROL
                case 0x140:                                 // RNGSWRESET
                    if((value & 1) != 0)
                    {
                        rndSourceEnable = 0;
                        sampleCnt1 = 0xFFFF;
                    }
                    break;
                case 0x1C4: rngClkEnable = value; break;    // RNGCLKENABLE
                case 0x1C8: rngDmaEnable = value; break;    // RNGDMAENABLE
                case 0x1D0: rngDmaSramAddr = value; break;  // RNGDMASRAMADDR
                case 0x1D8: rngWatchdogVal = value; break;  // RNGWATCHDOGVAL
                case 0x1E34: hostAoLockBits = value; break; // HOSTAOLOCKBITS
                case 0xA08: // HOST_ICR (W1C)
                    if((value & 0x800u) != 0)
                        symDmaCompleted = false;
                    break;

                default: break;
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            uint _ret = ReadDoubleWordInner(offset);
            Trace2(offset, true, _ret);
            return _ret;
        }

        private uint ReadDoubleWordInner(long offset)
        {
            switch (offset)
            {
                // --- AES key bank 0 + control readback ---
                case 0x400: case 0x404: case 0x408: case 0x40C:
                case 0x410: case 0x414: case 0x418: case 0x41C:
                    return aesKey0[(offset - 0x400) / 4];
                case 0x4BC: return aesRemaining;  // AES_REMAINING_BYTES
                case 0x4C0: return aesControl;    // AES_CONTROL

                // --- HASH digest/state readback (IV in, digest out) ---
                // A read with a live stream finalizes a COPY into hashH, so
                // digest reads after the last DIN chunk return the computed
                // digest while pre-data readback-verify reads still echo the
                // staged IV. The stream stays live (idempotent reads; a later
                // HASH_CONTROL write or H re-stage starts a fresh session).
                case 0x640: case 0x644: case 0x648: case 0x64C:
                case 0x650: case 0x654: case 0x658: case 0x65C:
                case 0x660: case 0x664: case 0x668: case 0x66C:
                case 0x670: case 0x674: case 0x678: case 0x67C:
                    if(shaStream != null)
                    {
                        var dg = shaStream.CurrentHash();
                        Array.Copy(dg, hashH, 8);
                    }
                    return hashH[(offset - 0x640) / 4];
                case 0x7C0: return hashControl;   // HASH_CONTROL
                case 0x7C4: return hashPadEn;     // HASH_PAD_EN
                case 0x7C8: return hashPadCfg;    // HASH_PAD_CFG
                case 0x7CC: return hashCurLen0;   // HASH_CUR_LEN_0
                case 0x7D0: return hashCurLen1;   // HASH_CUR_LEN_1
                case 0x6A4: return hashSelAesMac; // HASH_SEL_AES_MAC
                case 0x684: return autoHwPadding; // AUTO_HW_PADDING

                // --- Clock status / enable (all report ready per SVD defaults) ---
                case 0x810: return 1;          // AESCLKENABLE
                case 0x818: return 1;          // HASHCLKENABLE
                case 0x81C: return 1;          // PKACLKENABLE
                case 0x820: return 1;          // DMACLKENABLE
                case 0x824: return 0x18D;      // CLKSTATUS (AES|HASH|PKA|CHACHA|DMA)

                // --- OPCODE (PKA op = ADD at reset) ---
                case 0x80: return 0x20000000;

                // --- TRNG / RNG ---
                case 0x100: return rngImr;     // RNGIMR
                case 0x104: return TrngReady() ? 1u : 0u; // RNGISR (EHRVALID)
                case 0x10C: return trngConfig; // TRNGCONFIG
                case 0x110: return TrngReady() ? 1u : 0u; // TRNGVALID (EHRVALID)
                case 0x114: // EHRDATA0
                case 0x118: // EHRDATA1
                case 0x11C: // EHRDATA2
                case 0x120: // EHRDATA3
                case 0x124: // EHRDATA4
                case 0x128: return (uint)trngRandom.Next(); // EHRDATA5
                case 0x12C: return rndSourceEnable; // RNDSOURCEENABLE
                case 0x130: return sampleCnt1; // SAMPLECNT1 (readback-verify)
                case 0x138: return trngDebugControl; // TRNGDEBUGCONTROL
                case 0x140: return 0;          // RNGSWRESET (self-clearing)
                case 0x1B8: return 0;          // RNGBUSY (never busy)
                case 0x1C0: return 0x40;       // RNGVERSION
                case 0x1C4: return rngClkEnable; // RNGCLKENABLE
                case 0x1C8: return rngDmaEnable; // RNGDMAENABLE
                case 0x1D0: return rngDmaSramAddr; // RNGDMASRAMADDR
                case 0x1D8: return rngWatchdogVal; // RNGWATCHDOGVAL
                case 0x1DC: return 0;          // RNGDMASTATUS (idle)
                case 0x1E34: return hostAoLockBits; // HOSTAOLOCKBITS
                case 0xA00: // HOST_IRR (RNG_INT while TRNG ready; SYM_DMA latched)
                    return (TrngReady() ? 0x400u : 0u) | (symDmaCompleted ? 0x800u : 0u);
                case 0xA04: return 0;          // HOST_IMR (mask; IRQs unwired, polled)

                // --- CHACHACLKENABLE ---
                case 0x858: return 1;

                // --- HOSTCCISIDLE @ 0xA7C: CC idle status. Bit0 (HOSTCCISIDLE) must read 1 so that
                //     am_bsp_itm_printf_enable()/am_hal_dcu_swo_enable() pass their access gate and the
                //     CRYPTO_CC_IS_IDLE() busy-wait exits, which is what attaches ITM to stdio for SWO.
                 case 0xA7C: return 0x3B9;   // all sub-units idle (HOSTCCISIDLE|AHBISIDLE|NVMARB|NVM|RNG|PKA|CRYPTO)

                  // --- NVMISIDLE @ 0x1F10: am_hal_pwrctrl_periph_enable(CRYPTO) waits for
                  //     NVMISIDLEREG (bit0)==1 after the PWRSTCRYPTO bit sets, else it returns FAIL.
                  case 0x1F10: return 0x1;

                  // --- DCU enable (return stored value so a programmed chip reports its real DCU) ---
                  case 0x1E00: return dcuEn0;   // HOSTDCUEN0
                  case 0x1E04: return dcuEn1;   // HOSTDCUEN1
                  case 0x1E08: return dcuEn2;   // HOSTDCUEN2 (low word)
                  case 0x1E0C: return dcuEn3;   // HOSTDCUEN3 (high word)

                  // --- DCU lock ---
                  case 0x1E10: return dcuLock0; // HOSTDCULOCK0
                  case 0x1E14: return dcuLock1; // HOSTDCULOCK1
                  case 0x1E18: return dcuLock2; // HOSTDCULOCK2 (low word)
                  case 0x1E1C: return dcuLock3; // HOSTDCULOCK3 (high word)

                  // --- LCS register ---
                  case 0x1F14: return lcsReg;   // LCSREG



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

                // --- PKA_DONE @ 0xB4: bit0 reads 1 when the PKA pipeline is idle/
                // a PKA op has completed. The CC driver spins on this (150M+ polls
                // observed) before/after PKA ops; returning 0 hangs every wait
                // to timeout. Simulation completes ops instantly: always done.
                case 0xB4: return 1;

                // --- PKA_PIPE_RDY @ 0xB0: bit0 reads 1 when the PKA pipeline
                // accepts a new op. Same instant-complete policy as PKA_DONE.
                case 0xB0: return 1;

                // --- CRYPTOBUSY: always 0 (no real crypto ops yet) ---
                case 0x910: return 0;

                // --- CoreSight ID registers (SVD reset values; CC_LibInit
                //     memcmps PERIPHERALID0-4 and COMPONENTID0-3, error 6 on
                //     mismatch which zeroes gCcRegBase via CC_HalTerminate) ---
                case 0xFD0: return 0x04;      // PERIPHERALID4
                case 0xFE0: return 0xC0;      // PERIPHERALID0
                case 0xFE4: return 0xB0;      // PERIPHERALID1
                case 0xFE8: return 0x2B;      // PERIPHERALID2
                case 0xFEC: return 0x00;      // PERIPHERALID3
                case 0xFF0: return 0x0D;      // COMPONENTID0
                case 0xFF4: return 0xF0;      // COMPONENTID1
                case 0xFF8: return 0x05;      // COMPONENTID2
                case 0xFFC: return 0xB1;      // COMPONENTID3

                default: return 0;
            }
        }

        private bool TrngReady()
        {
            // LLF_RND enables the RNG clock but never writes RNDSOURCEENABLE
            // in the startup/test path, so VALID follows the clock gate.
            return rngClkEnable != 0;
        }

        // Routes a DIN transfer (byteCount from dinMemAddr) to the armed engine.
        // HASH appends to the running stream (staged HASH_H is the IV);
        // AES-ECB executes immediately into doutMemAddr (engine AXI write).
        private void FeedDinEngines(uint byteCount)
        {
            if(byteCount == 0 || dinMemAddr == 0)
                return;
            if(hashArmed && !aesArmed)
            {
                if(shaStream == null)
                {
                    var iv = new uint[8];
                    Array.Copy(hashH, iv, 8);
                    shaStream = new AmbiqCryptoEngines.Sha256Stream(iv);
                }
                var chunk = new byte[byteCount];
                for(uint i = 0; i < byteCount; i++)
                    chunk[i] = sysbusCtrl.ReadByte((ulong)(dinMemAddr + i), context: this);
                shaStream.AppendData(chunk, 0, chunk.Length);
                return;
            }
            if(aesArmed && !hashArmed)
                RunAesEcb(byteCount);
        }

        // AES-ECB via LLI: key bank 0, size from NK_KEY0, direction from
        // DEC_KEY0, ECB mode only. Result goes to doutMemAddr and to the
        // DOUT sram window (so a later DOUT DMA picks it up too).
        private void RunAesEcb(uint byteCount)
        {
            if(byteCount == 0 || (byteCount % 16) != 0 || doutMemAddr == 0)
                return;
            if(((aesControl >> 2) & 0x7) != 0) // MODE_KEY0: only ECB emulated
                return;
            int nk = (int)((aesControl >> 12) & 0x3); // 0->128, 1->192, 2->256
            int keyBytes = (nk + 2) * 8;
            if(keyBytes != 16 && keyBytes != 24 && keyBytes != 32)
                return;
            bool decrypt = (aesControl & 0x1) != 0; // DEC_KEY0
            var key = new byte[keyBytes];
            for(int i = 0; i < keyBytes; i++)
                key[i] = (byte)(aesKey0[i / 4] >> (8 * (i % 4)));
            var data = new byte[byteCount];
            for(uint i = 0; i < byteCount; i++)
                data[i] = sysbusCtrl.ReadByte((ulong)(dinMemAddr + i), context: this);
            byte[] result;
            try
            {
                result = AmbiqCryptoEngines.AesEcb(key, data, decrypt);
            }
            catch(ArgumentException)
            {
                return;
            }
            for(uint i = 0; i < byteCount; i++)
                sysbusCtrl.WriteByte((ulong)(doutMemAddr + i), result[i], context: this);
            var sramOff = doutSramOffset & (SramSize - 1);
            if(sramOff + byteCount <= SramSize)
                Buffer.BlockCopy(result, 0, sram, (int)sramOff, (int)byteCount);
            symDmaCompleted = true;
        }

        private void PerformDinDma(uint byteCount)
        {            if (byteCount == 0 || byteCount > SramSize - dinSramOffset)
                return;

            dinMemReady = false;
            dinSramReady = false;

            for (int i = 0; i < (int)byteCount; i++)
            {
                sram[dinSramOffset + i] = sysbusCtrl.ReadByte((ulong)(dinMemAddr + i), context: this);
            }

            dinMemReady = true;
            dinSramReady = true;
            symDmaCompleted = true;
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
            symDmaCompleted = true;
        }
    }
}
