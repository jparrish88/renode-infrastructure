//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite SDIO0 (SDIO0 @ 0x40070000, IRQ26).
// NEW file for the Lite family.
//
// Register map + reset values from pack/SVD/apollo510L.svd (32 regs).
// Phase-2 model: eMMC functional stub for power profiling.
//  - Card always present (PRESENT.CARDINSERTED from reset; eMMC is soldered).
//  - Internal clock stabilizes when enabled (CLOCKCTRL.CLKSTABLE follows CLKEN;
//    SWRST bits are self-clearing, the HAL polls on them).
//  - Command synthesis on TRANSFER writes: CMD complete + plausible responses
//    (OCR/RCA/CID/CSDv2/R1 status), TRANSFER complete + data movement for
//    read/write commands via PIO/BUFFER, SDMA, or ADMA2-32 descriptor walk,
//    with a small SRAM-backed card image so read-back verification passes.
//  - INTSTAT is write-1-to-clear; IRQ = INTSTAT & INTENABLE.
// No timing model: transfers complete in the same virtual instant (keeps the
// CPU active, matching silicon duty; eMMC pulse timing is out of scope).
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510L_SDIO : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_SDIO(IMachine machine) : base(machine)
        {
            this.machine = machine;
            BuildExtCsd();
            Reset();
        }

        public long Size => 0x200;

        public override void Reset()
        {
            registers.Clear();
            foreach(var kv in Resets)
            {
                registers[kv.Key] = kv.Value;
            }
            cardStore.Clear();
            pioReadAddr = 0;
            pioReadRemaining = 0;
            pioWriteAddr = 0;
            UpdateIRQ();
        }

        public override uint ReadDoubleWord(long offset)
        {
            if(offset == 0x20) // BUFFER: PIO read port
            {
                return PioReadWord();
            }
            if(registers.TryGetValue(offset, out var value))
            {
                return value;
            }
            return 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            if(offset == 0x30) // INTSTAT is write-1-to-clear
            {
                registers[offset] = registers.TryGetValue(offset, out var cur) ? (uint)(cur & ~value) : 0u;
                UpdateIRQ();
                return;
            }
            registers[offset] = value;
            if(offset == 0x2C) // CLOCKCTRL fixups
            {
                uint r = registers[offset];
                if((r & 0x1u) != 0) // CLKEN -> clock stabilizes
                {
                    r |= 0x2u; // CLKSTABLE
                }
                r &= ~(0x7u << 24); // SWRST* are self-clearing (HAL polls them)
                registers[offset] = r;
            }
            else if(offset == 0x20) // BUFFER: PIO write port
            {
                PioWriteWord(value);
            }
            else if(offset == 0x0C) // TRANSFER: command issue
            {
                IssueCommand(value);
            }
            if(IsIrqRegister(offset))
            {
                UpdateIRQ();
            }
        }

        private void UpdateIRQ()
        {
            uint stat = registers.TryGetValue(0x30, out var s) ? s : 0;
            uint en = registers.TryGetValue(0x34, out var e) ? e : 0;
            if((stat & en) != 0)
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
            return offset == 0x30 || offset == 0x34 || offset == 0x38;
        }

        private void SetIntstat(uint bits)
        {
            registers[0x30] = registers.TryGetValue(0x30, out var cur) ? cur | bits : bits;
            UpdateIRQ();
        }

        private void IssueCommand(uint transfer)
        {
            uint cmdIdx = (transfer >> 24) & 0x3Fu;
            uint dataPresent = (transfer >> 21) & 0x1u;
            uint respType = (transfer >> 16) & 0x3u;
            uint readDir = (transfer >> 4) & 0x1u;
            uint arg = registers.TryGetValue(0x08, out var a) ? a : 0;
            lastCmdIdx = cmdIdx;
            lastCmdArg = arg;

            // Default R1: READY_FOR_DATA(bit8) + TRAN state (bits12:9 = 4).
            uint r1 = 0x00000900u;
            uint r0 = r1, r1w = 0, r2w = 0, r3w = 0;
            bool hasData = dataPresent != 0;
            bool isExtCsd = false;

            switch(cmdIdx)
            {
            case 0: // GO_IDLE: no response
                respType = 0;
                break;
            case 1: // SEND_OP_COND: OCR with power-up busy + sector mode
                r0 = 0xC0FF8080u;
                break;
            case 2: // ALL_SEND_CID: 136-bit CID (MID set so it looks real)
                r3w = 0x15010000u; r2w = 0; r1w = 0; r0 = 0;
                break;
            case 3: // SEND_RELATIVE_ADDR: R6 with RCA 0x1234
                r0 = (0x1234u << 16);
                break;
            case 6: // SWITCH (R1b): accept, DAT lines stay high (not busy)
                r0 = r1;
                break;
            case 7: // SELECT: R1
            case 13: // STATUS: R1 ready
            case 16: // SET_BLOCKLEN: R1
            case 23: // SET_BLOCK_COUNT: R1
                r0 = r1;
                break;
            case 8: // SEND_EXT_CSD (eMMC): R1 + 512B data out
                r0 = r1;
                isExtCsd = true;
                break;
            case 9: // SEND_CSD: CSD v2.0, C_SIZE = 0x3FFF -> 16M blocks (8GB)
                r3w = 0x40000000u; r2w = 0; r1w = 0x3FFF0000u; r0 = 0;
                break;
            case 12: // STOP_TRANSMISSION: R1 + transfer complete
                r0 = r1;
                SetIntstat(TransferComplete);
                break;
            case 17: // READ_SINGLE_BLOCK
            case 18: // READ_MULTIPLE_BLOCK
            case 24: // WRITE_BLOCK
            case 25: // WRITE_MULTIPLE_BLOCK
                r0 = r1;
                break;
            default:
                this.Log(LogLevel.Warning, "SDIO unmodeled CMD{0} arg 0x{1:X}, completing with R1", cmdIdx, arg);
                r0 = r1;
                break;
            }

            if(respType == 3 || (respType == 0 && (cmdIdx == 2 || cmdIdx == 9)))
            {
                // 136-bit response
                registers[0x10] = r0; registers[0x14] = r1w;
                registers[0x18] = r2w; registers[0x1C] = r3w;
            }
            else if(respType != 0)
            {
                registers[0x10] = r0;
            }

            if(hasData && cmdIdx != 12)
            {
                DoDataTransfer(readDir != 0, arg, transfer, isExtCsd);
            }
            SetIntstat(CommandComplete);
            this.Log(LogLevel.Info, "SDIO CMD{0} arg 0x{1:X} -> complete", cmdIdx, arg);
        }

        private void DoDataTransfer(bool isRead, uint blockArg, uint transfer, bool isExtCsd)
        {
            uint blockReg = registers.TryGetValue(0x04, out var b) ? b : 0x200u;
            uint blkSize = blockReg & 0xFFFu;
            if(blkSize == 0) blkSize = 512;
            uint blkCnt = (blockReg >> 16) & 0xFFFFu;
            if(blkCnt == 0) blkCnt = 1;
            bool dmaEn = (transfer & 0x1u) != 0;
            uint hcReg = registers.TryGetValue(0x28, out var hc) ? hc : 0u;
            uint dmaSel = (hcReg >> 3) & 0x3u;

            if(isRead && isExtCsd)
            {
                MoveExtCsdOut(blkSize, dmaEn, dmaSel);
            }
            else if(dmaEn && (dmaSel == 2 || dmaSel == 3))
            {
                AdmaTransfer(isRead, blockArg, blkSize, blkCnt);
            }
            else if(dmaEn)
            {
                SdmaTransfer(isRead, blockArg, blkSize, blkCnt);
            }
            else
            {
                // PIO: stage the window; words flow through BUFFER reads/writes.
                if(isRead)
                {
                    pioReadAddr = (ulong)blockArg * 512u;
                    pioReadRemaining = blkSize * blkCnt;
                    registers[0x24] = registers.TryGetValue(0x24, out var pr) ? pr | (1u << 11) : (1u << 11); // BUFRDEN
                    SetIntstat(BufferReadReady);
                }
                else
                {
                    pioWriteAddr = (ulong)blockArg * 512u;
                    registers[0x24] = registers.TryGetValue(0x24, out var pw) ? pw | (1u << 10) : (1u << 10); // BUFWREN
                    SetIntstat(BufferWriteReady);
                }
            }
            SetIntstat(TransferComplete);
        }

        private void MoveExtCsdOut(uint blkSize, bool dmaEn, uint dmaSel)
        {
            uint n = Math.Min(blkSize, (uint)extCsd.Length);
            if(dmaEn && (dmaSel == 2 || dmaSel == 3))
            {
                foreach(var dest in AdmaDestinations(n))
                {
                    uint chunk = Math.Min(n, (uint)(extCsd.Length));
                    var buf = new byte[chunk];
                    Array.Copy(extCsd, 0, buf, 0, chunk);
                    Sysbus.WriteBytes(buf, dest);
                    n -= chunk;
                    if(n == 0) break;
                }
            }
            else if(dmaEn)
            {
                ulong dest = registers.TryGetValue(0x00, out var s) ? s : 0u;
                var buf = new byte[n];
                Array.Copy(extCsd, 0, buf, 0, n);
                Sysbus.WriteBytes(buf, dest);
            }
            else
            {
                // PIO: serve EXT_CSD through BUFFER reads.
                pioReadAddr = ExtCsdSentinel;
                pioReadRemaining = n;
                pioExtCsdOff = 0;
                registers[0x24] = registers.TryGetValue(0x24, out var pr) ? pr | (1u << 11) : (1u << 11);
                SetIntstat(BufferReadReady);
            }
        }

        private IEnumerable<ulong> AdmaDestinations(uint total)
        {
            ulong table = registers.TryGetValue(0x58, out var lo) && lo != 0 ? lo
                : (registers.TryGetValue(0x54, out var a) ? a : 0u);
            for(int i = 0; i < 512; i++)
            {
                ulong e = (ulong)table + (ulong)i * 8u;
                uint w0 = ReadU32(e);
                uint addr = ReadU32(e + 4);
                uint attr = w0 & 0xFFu;
                uint len = (w0 >> 16) & 0xFFFFu;
                if((attr & 0x1u) == 0) yield break; // invalid
                uint act = (attr >> 4) & 0x3u;
                if(act == 2 && len > 0) // TRAN
                {
                    yield return addr;
                }
                else if(act == 3) // LINK: descriptor address, not data
                {
                    table = addr;
                    i = -1; // restart walk at new table (for-loop i++ -> 0)
                    continue;
                }
                if((attr & 0x2u) != 0) yield break; // END
            }
        }

        private void AdmaTransfer(bool isRead, uint blockArg, uint blkSize, uint blkCnt)
        {
            uint total = blkSize * blkCnt;
            uint done = 0;
            foreach(var dest in AdmaDestinations(total))
            {
                if(done >= total) break;
                // Length per descriptor is not tracked here; move in blkSize
                // chunks: the HAL builds one descriptor per iov segment.
                uint chunk = Math.Min(blkSize, total - done);
                if(isRead)
                {
                    var buf = CardRead(blockArg * 512u + done, chunk);
                    Sysbus.WriteBytes(buf, dest);
                }
                else
                {
                    var buf = Sysbus.ReadBytes(dest, (int)chunk);
                    CardWrite(blockArg * 512u + done, buf);
                }
                done += chunk;
            }
            SetIntstat(DmaInterrupt);
        }

        private void SdmaTransfer(bool isRead, uint blockArg, uint blkSize, uint blkCnt)
        {
            ulong addr = registers.TryGetValue(0x00, out var s) ? s : 0u;
            uint total = blkSize * blkCnt;
            if(isRead)
            {
                Sysbus.WriteBytes(CardRead(blockArg * 512u, total), addr);
            }
            else
            {
                CardWrite(blockArg * 512u, Sysbus.ReadBytes(addr, (int)total));
            }
            SetIntstat(DmaInterrupt);
        }

        private uint PioReadWord()
        {
            if(pioReadRemaining < 4)
            {
                return 0;
            }
            uint w;
            if(pioReadAddr == ExtCsdSentinel)
            {
                w = BitConverter.ToUInt32(extCsd, pioExtCsdOff);
                pioExtCsdOff += 4;
            }
            else
            {
                var buf = CardRead((uint)pioReadAddr, 4);
                w = BitConverter.ToUInt32(buf, 0);
                pioReadAddr += 4;
            }
            pioReadRemaining -= 4;
            return w;
        }

        private void PioWriteWord(uint value)
        {
            CardWrite((uint)pioWriteAddr, BitConverter.GetBytes(value));
            pioWriteAddr += 4;
        }

        private byte[] CardRead(uint byteAddr, uint len)
        {
            var outBuf = new byte[len];
            uint blk = byteAddr / 512u;
            uint off = byteAddr % 512u;
            uint copied = 0;
            while(copied < len)
            {
                if(!cardStore.TryGetValue(blk, out var data))
                {
                    // Never written: deterministic incrementing pattern.
                    for(uint i = 0; i < len - copied && off + i < 512; i++)
                    {
                        outBuf[copied + i] = (byte)((blk + off + i) & 0xFFu);
                    }
                    uint step = Math.Min(len - copied, 512u - off);
                    copied += step;
                    blk++;
                    off = 0;
                    continue;
                }
                uint step2 = Math.Min(len - copied, 512u - off);
                Array.Copy(data, off, outBuf, copied, step2);
                copied += step2;
                blk++;
                off = 0;
            }
            return outBuf;
        }

        private void CardWrite(uint byteAddr, byte[] data)
        {
            uint blk = byteAddr / 512u;
            uint off = byteAddr % 512u;
            int pos = 0;
            while(pos < data.Length)
            {
                if(!cardStore.TryGetValue(blk, out var buf))
                {
                    buf = new byte[512];
                    cardStore[blk] = buf;
                }
                int step = Math.Min(data.Length - pos, 512 - (int)off);
                Array.Copy(data, pos, buf, off, step);
                pos += step;
                blk++;
                off = 0;
            }
        }

        private void BuildExtCsd()
        {
            extCsd = new byte[512];
            extCsd[192] = 8; // EXT_CSD_REV v1.8
            uint secCount = 0x01000000u; // 16M sectors = 8GB
            Array.Copy(BitConverter.GetBytes(secCount), 0, extCsd, 212, 4);
        }

        private IBusController Sysbus => machine.GetSystemBus(this);

        private uint ReadU32(ulong addr)
        {
            return BitConverter.ToUInt32(Sysbus.ReadBytes(addr, 4), 0);
        }

        private const uint CommandComplete = 1u << 0;
        private const uint TransferComplete = 1u << 1;
        private const uint DmaInterrupt = 1u << 3;
        private const uint BufferWriteReady = 1u << 4;
        private const uint BufferReadReady = 1u << 5;

        private const ulong ExtCsdSentinel = 0xFFFFFFFFFFFFul;

        private readonly IMachine machine;
        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();
        private readonly Dictionary<uint, byte[]> cardStore = new Dictionary<uint, byte[]>();
        private byte[] extCsd;
        private uint lastCmdIdx;
        private uint lastCmdArg;
        private ulong pioReadAddr;
        private uint pioReadRemaining;
        private int pioExtCsdOff;
        private ulong pioWriteAddr;

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00000000, 0x00000000), // SDMA
            new KeyValuePair<long, uint>(0x00000004, 0x00000000), // BLOCK
            new KeyValuePair<long, uint>(0x00000008, 0x00000000), // ARGUMENT1
            new KeyValuePair<long, uint>(0x0000000C, 0x00000000), // TRANSFER
            new KeyValuePair<long, uint>(0x00000010, 0x00000000), // RESPONSE0
            new KeyValuePair<long, uint>(0x00000014, 0x00000000), // RESPONSE1
            new KeyValuePair<long, uint>(0x00000018, 0x00000000), // RESPONSE2
            new KeyValuePair<long, uint>(0x0000001C, 0x00000000), // RESPONSE3
            new KeyValuePair<long, uint>(0x00000020, 0x00000000), // BUFFER
            new KeyValuePair<long, uint>(0x00000024, 0x1FF70000), // PRESENT: card inserted+stable
            new KeyValuePair<long, uint>(0x00000028, 0x00800000), // HOSTCTRL1
            new KeyValuePair<long, uint>(0x0000002C, 0x00000000), // CLOCKCTRL
            new KeyValuePair<long, uint>(0x00000030, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x00000034, 0x00000000), // INTENABLE
            new KeyValuePair<long, uint>(0x00000038, 0x00000000), // INTSIG
            new KeyValuePair<long, uint>(0x0000003C, 0x00000000), // AUTO
            new KeyValuePair<long, uint>(0x00000040, 0x00000000), // CAPABILITIES0
            new KeyValuePair<long, uint>(0x00000044, 0x00000000), // CAPABILITIES1
            new KeyValuePair<long, uint>(0x00000048, 0x00000000), // MAXIMUM0
            new KeyValuePair<long, uint>(0x0000004C, 0x00000000), // MAXIMUM1
            new KeyValuePair<long, uint>(0x00000050, 0x00000000), // FORCE
            new KeyValuePair<long, uint>(0x00000054, 0x00000000), // ADMA
            new KeyValuePair<long, uint>(0x00000058, 0x00000000), // ADMALOWD
            new KeyValuePair<long, uint>(0x0000005C, 0x00000000), // ADMAHIWD
            new KeyValuePair<long, uint>(0x00000060, 0x00000000), // PRESET0
            new KeyValuePair<long, uint>(0x00000064, 0x00000000), // PRESET1
            new KeyValuePair<long, uint>(0x00000068, 0x00000000), // PRESET2
            new KeyValuePair<long, uint>(0x0000006C, 0x00000000), // PRESET3
            new KeyValuePair<long, uint>(0x00000070, 0x00000000), // BOOTTOCTRL
            new KeyValuePair<long, uint>(0x00000078, 0x00000000), // VENDOR
            new KeyValuePair<long, uint>(0x000000FC, 0x0A020000), // SLOTSTAT
            new KeyValuePair<long, uint>(0x00000100, 0x00000000), // CLKOUTCFG
        };
    }
}
