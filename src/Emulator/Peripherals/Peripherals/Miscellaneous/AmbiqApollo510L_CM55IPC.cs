//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite CM55/CM4 IPC mailbox (CM55IPC @ 0x40034000,
// IRQ17 IPC_PEND_MSG, IRQ18 IPC_ERR). NEW file for the Lite family.
//
// Register map from pack/SVD/apollo510L.svd; behavior from
// mcu/apollo510L/hal/am_hal_ipc_mbox.c and am_hal_pwrctrl.c:
//
//   M2DDATA @0x00: M55 write pushes the M2D (M55->CM4) FIFO (depth 32).
//     Writing while full latches M2DFULLERROR. No CM4 core exists in the
//     simulation, so the M2D side fills and stays (backpressure is real).
//   D2MDATA @0x04: M55 read pops the D2M (CM4->M55) FIFO. Empty reads latch
//     D2MEMPTYERROR and return 0 (the HAL checks D2MEMPTY first, so this is
//     a safety net, not a normal path).
//   STATUS @0x08: computed levels - MxDPEND counts, FULL/EMPTY, threshold
//     ACTIVE = count >= threshold (default threshold 1).
//   Threshold latches (M2DIS[0]/D2MIS[0]): set on push/threshold-write,
//     cleared by driver RMW pattern (writes store; level recomputed).
//   Error latches (D2MERROR @0x1C): W1C, as the HAL writes masks to clear.
//   IRQ17 PEND_MSG: any enabled threshold latch. IRQ18 ERR: any enabled
//     error latch, plus IPCINIT unconditionally (see below).
//
// CM4 boot handshake: am_hal_pwrctrl rss_mbox_init_wait polls
// NVIC_GetPendingIRQ(IPC_ERR_IRQn) + D2MERROR.IPCINIT *before* any enable
// is set. On hardware the freshly booted CM4 raises exactly this. The model
// therefore exposes a CM4-alive GPIO input (wired from the power
// controller CM4-boot output in the repl): a rising edge latches IPCINIT
// and asserts ERR.
//
// Emulated-CM4 signal responder: RFXTAL ON/OFF/CONFIG request frames in
// M2D are answered synchronously with the matching single-word response
// in D2M (the CM55 poll waiters and mailbox ISRs consume these). Consumed
// M2D words are dequeued, so the M2D side drains like real CM4 reads.
// Unknown signals are logged and dropped (RPMsg framing comes later).
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Timers;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510L_CM55IPC : BasicDoubleWordPeripheral, IKnownSize, IGPIOReceiver
    {
        public AmbiqApollo510L_CM55IPC(IMachine machine) : base(machine)
        {
            this.machine = machine;
            nsPollTimer = new LimitTimer(machine.ClockSource, NsPollFrequencyHz, this, "ns-announce",
                limit: 1, direction: Direction.Ascending, enabled: false,
                workMode: WorkMode.Periodic, eventEnabled: true, autoUpdate: true);
            nsPollTimer.LimitReached += OnNsPoll;
            DefineRegisters();
        }

        public long Size => 0x100;

        public GPIO PendMsgIRQ { get; } = new GPIO();
        public GPIO ErrIRQ { get; } = new GPIO();

        public void OnGPIO(int number, bool value)
        {
            if(value && !cm4Alive)
            {
                cm4Alive = true;
                ipcInitLatched = true;
                this.Log(LogLevel.Info, "IPC: CM4 boot handshake (IPCINIT latched, ERR asserted)");
                UpdateIRQ();
            }
            else if(!value && cm4Alive)
            {
                cm4Alive = false;
                ipcInitLatched = false;
                UpdateIRQ();
            }
        }

        public override void Reset()
        {
            m2dFifo.Clear();
            d2mFifo.Clear();
            m2dMsg.Clear();
            nsPollTimer.Enabled = false;
            nsTotalInjections = 0;
            nsInjectedThisGeneration = false;
            m2dThreshold = 1;
            d2mThreshold = 1;
            m2dFullError = false;
            d2mEmptyError = false;
            d2mFullError = false;
            ipcInitLatched = false;
            cm4Alive = false;
            UpdateIRQ();
        }

        private void DefineRegisters()
        {
            Registers.M2DData.Define(this)
                .WithValueField(0, 32, name: "M2DDATA",
                    writeCallback: (_, v) => PushM2D((uint)v),
                    valueProviderCallback: _ => 0);
            Registers.D2MData.Define(this)
                .WithValueField(0, 32, name: "D2MDATA",
                    valueProviderCallback: _ => PopD2M());
            Registers.Status.Define(this, 0x00010001)
                .WithFlag(0, FieldMode.Read, name: "D2MEMPTY", valueProviderCallback: _ => d2mFifo.Count == 0)
                .WithFlag(1, FieldMode.Read, name: "D2MFULL", valueProviderCallback: _ => d2mFifo.Count >= FifoDepth)
                .WithFlag(2, FieldMode.Read, name: "D2MTHRESHOLDACTIVE", valueProviderCallback: _ => d2mFifo.Count >= d2mThreshold)
                .WithReservedBits(3, 1)
                .WithValueField(4, 6, name: "D2MPEND", valueProviderCallback: _ => (uint)d2mFifo.Count)
                .WithReservedBits(10, 6)
                .WithFlag(16, FieldMode.Read, name: "M2DEMPTY", valueProviderCallback: _ => m2dFifo.Count == 0)
                .WithFlag(17, FieldMode.Read, name: "M2DFULL", valueProviderCallback: _ => m2dFifo.Count >= FifoDepth)
                .WithFlag(18, FieldMode.Read, name: "M2DTHRESHOLDACTIVE", valueProviderCallback: _ => m2dFifo.Count >= m2dThreshold)
                .WithReservedBits(19, 1)
                .WithValueField(20, 6, name: "M2DPEND", valueProviderCallback: _ => (uint)m2dFifo.Count)
                .WithReservedBits(26, 6);
            Registers.M2DThreshold.Define(this, 0x00000004)
                .WithValueField(0, 5, name: "M2DTHRESHOLD",
                    writeCallback: (_, v) => { m2dThreshold = ClampThreshold((uint)v); },
                    valueProviderCallback: _ => m2dThreshold)
                .WithReservedBits(5, 27);
            Registers.M2DInterruptStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "M2DTHRESHOLDIRQ", valueProviderCallback: _ => m2dFifo.Count >= m2dThreshold && m2dFifo.Count > 0)
                .WithFlag(1, FieldMode.Read, name: "M2DERRORIRQ", valueProviderCallback: _ => m2dFullError)
                .WithReservedBits(2, 30);
            Registers.M2DInterruptEnable.Define(this)
                .WithFlag(0, out m2dThresholdIrqEnField, name: "M2DTHRESHOLDIRQEN")
                .WithFlag(1, out m2dErrorIrqEnField, name: "M2DERRORIRQEN")
                .WithReservedBits(2, 30);
            Registers.D2MError.Define(this)
                .WithFlag(0, name: "D2MEMPTYERROR", writeCallback: (_, v) => { if(v) d2mEmptyError = false; UpdateIRQ(); }, valueProviderCallback: _ => d2mEmptyError)
                .WithFlag(1, name: "D2MFULLERROR", writeCallback: (_, v) => { if(v) d2mFullError = false; UpdateIRQ(); }, valueProviderCallback: _ => d2mFullError)
                .WithFlag(2, name: "M2DFULLERROR", writeCallback: (_, v) => { if(v) m2dFullError = false; UpdateIRQ(); }, valueProviderCallback: _ => m2dFullError)
                .WithFlag(3, name: "IPCINIT", writeCallback: (_, v) => { if(v) ipcInitLatched = false; UpdateIRQ(); }, valueProviderCallback: _ => ipcInitLatched)
                .WithReservedBits(4, 28);
            Registers.D2MThreshold.Define(this, 0x00000004)
                .WithValueField(0, 5, name: "D2MTHRESHOLD",
                    writeCallback: (_, v) => { d2mThreshold = ClampThreshold((uint)v); },
                    valueProviderCallback: _ => d2mThreshold)
                .WithReservedBits(5, 27);
            Registers.D2MInterruptStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "D2MTHRESHOLDIRQ", valueProviderCallback: _ => d2mFifo.Count >= d2mThreshold && d2mFifo.Count > 0)
                .WithFlag(1, FieldMode.Read, name: "D2MERRORIRQ", valueProviderCallback: _ => d2mEmptyError || d2mFullError || ipcInitLatched)
                .WithReservedBits(2, 30);
            Registers.D2MInterruptEnable.Define(this)
                .WithFlag(0, out d2mThresholdIrqEnField, name: "D2MTHRESHOLDIRQEN")
                .WithFlag(1, out d2mErrorIrqEnField, name: "D2MERRORIRQEN")
                .WithReservedBits(2, 30);
        }

        private void PushM2D(uint value)
        {
            if(m2dFifo.Count >= FifoDepth)
            {
                m2dFullError = true;
                this.Log(LogLevel.Warning, "IPC: M2D FIFO full, message dropped (M2DFULLERROR)");
            }
            else
            {
                m2dFifo.Enqueue(value);
                m2dMsg.Add(value);
            }
            UpdateIRQ();
            TryDispatchM2D();
        }

        private uint PopD2M()
        {
            if(d2mFifo.Count == 0)
            {
                d2mEmptyError = true;
                UpdateIRQ();
                return 0;
            }
            var value = d2mFifo.Dequeue();
            UpdateIRQ();
            return value;
        }

        private void UpdateIRQ()
        {
            var m2dThresh = m2dFifo.Count >= m2dThreshold && m2dFifo.Count > 0;
            var d2mThresh = d2mFifo.Count >= d2mThreshold && d2mFifo.Count > 0;
            var pend = (m2dThresh && M2DThresholdIrqEn) || (d2mThresh && D2MThresholdIrqEn);
            var err = (m2dFullError && M2DErrorIrqEn)
                || ((d2mEmptyError || d2mFullError) && D2MErrorIrqEn)
                || ipcInitLatched;
            PendMsgIRQ.Set(pend);
            ErrIRQ.Set(err);
        }

        // Emulated-CM4 request dispatcher. RFXTAL request frames are answered
        // synchronously with the matching single-word response in D2M, which is
        // what the CM55 poll waiters (wait_XTAL_HS_response) and the mailbox
        // ISRs consume. Consumed M2D words are dequeued (the CM4 read them).
        private void TryDispatchM2D()
        {
            while(m2dMsg.Count > 0)
            {
                uint signal = m2dMsg[0];
                uint response;
                int frameLen;
                string name;
                if(signal == SigRfxtalOnReq)
                {
                    response = SigRfxtalOnRsp; frameLen = 1; name = "RFXTAL_ON";
                }
                else if(signal == SigRfxtalOffReq)
                {
                    response = SigRfxtalOffRsp; frameLen = 1; name = "RFXTAL_OFF";
                }
                else if(signal == SigRfxtalConfigReq || signal == SigIpcShmConfigReq)
                {
                    if(m2dMsg.Count < 2)
                    {
                        return; // wait for the size word
                    }
                    uint size = m2dMsg[1];
                    if(size > FifoDepth)
                    {
                        this.Log(LogLevel.Warning, "IPC: CONFIG size {0} insane, dropping signal", size);
                        m2dMsg.RemoveAt(0);
                        continue;
                    }
                    frameLen = (int)size + 2;
                    if(m2dMsg.Count < frameLen)
                    {
                        return; // wait for the payload
                    }
                    response = signal == SigRfxtalConfigReq ? SigRfxtalConfigRsp : SigIpcShmConfigRsp;
                    name = signal == SigRfxtalConfigReq ? "RFXTAL_CONFIG" : "IPC_SHM_CONFIG";
                }
                else
                {
                    this.Log(LogLevel.Info, "IPC: unknown M2D signal 0x{0:X}, dropping (RPMsg comes later)", signal);
                    m2dMsg.RemoveAt(0);
                    if(m2dFifo.Count > 0)
                    {
                        m2dFifo.Dequeue();
                    }
                    continue;
                }
                // Capture the negotiated SHM window (needed for RPMsg/vring work).
                if(signal == SigIpcShmConfigReq && frameLen >= 4)
                {
                    shmAddr = m2dMsg.Count > 2 ? m2dMsg[2] : 0;
                    shmSize = m2dMsg.Count > 3 ? m2dMsg[3] : 0;
                    shmBufSize = m2dMsg.Count > 4 ? m2dMsg[4] : 0;
                    this.Log(LogLevel.Info, "IPC: SHM window addr=0x{0:X} size=0x{1:X} bufSize=0x{2:X}", shmAddr, shmSize, shmBufSize);
                    nsPollTimer.Enabled = true;
                }
                m2dMsg.RemoveRange(0, frameLen);
                for(int i = 0; i < frameLen && m2dFifo.Count > 0; i++)
                {
                    m2dFifo.Dequeue();
                }
                d2mFifo.Enqueue(response);
                UpdateIRQ();
                this.Log(LogLevel.Info, "IPC: answered {0} with RSP 0x{1:X} (D2M depth {2})", name, response, d2mFifo.Count);
            }
        }

        private static uint ClampThreshold(uint value)
        {
            if(value < 1)
            {
                return 1;
            }
            if(value > FifoDepth - 1)
            {
                return FifoDepth - 1;
            }
            return value;
        }

        private const int FifoDepth = 32;

        // Emulated-CM4 mailbox signals (am_hal_ipc_mbox.h, base 0xA868).
        private const uint SigRfxtalOnReq = 0xA86A;
        private const uint SigRfxtalOnRsp = 0xA86B;
        private const uint SigRfxtalOffReq = 0xA86C;
        private const uint SigRfxtalOffRsp = 0xA86D;
        private const uint SigRfxtalConfigReq = 0xA86E;
        private const uint SigRfxtalConfigRsp = 0xA86F;
        private const uint SigIpcShmConfigReq = 0xA870;
        private const uint SigIpcShmConfigRsp = 0xA871;

        private readonly Queue<uint> m2dFifo = new Queue<uint>();
        private readonly Queue<uint> d2mFifo = new Queue<uint>();
        private readonly List<uint> m2dMsg = new List<uint>();
        private uint m2dThreshold = 1;
        private uint d2mThreshold = 1;
        private bool m2dFullError;
        private bool d2mEmptyError;
        private bool d2mFullError;
        private bool ipcInitLatched;
        private bool cm4Alive;
        private uint shmAddr;
        private uint shmSize;
        private uint shmBufSize;
        // Emulated-CM4 RPMsg NS announcement state (binds the "am_ipc" endpoint).
        private readonly IMachine machine;
        private readonly LimitTimer nsPollTimer;
        private const ulong NsPollFrequencyHz = 200;
        private const int NsMaxInjections = 5;
        private bool nsInjectedThisGeneration;
        private int nsTotalInjections;
        private IFlagRegisterField m2dThresholdIrqEnField;
        private IFlagRegisterField m2dErrorIrqEnField;
        private IFlagRegisterField d2mThresholdIrqEnField;
        private IFlagRegisterField d2mErrorIrqEnField;

        private bool M2DThresholdIrqEn => m2dThresholdIrqEnField != null && m2dThresholdIrqEnField.Value;
        private bool M2DErrorIrqEn => m2dErrorIrqEnField != null && m2dErrorIrqEnField.Value;
        private bool D2MThresholdIrqEn => d2mThresholdIrqEnField != null && d2mThresholdIrqEnField.Value;
        private bool D2MErrorIrqEn => d2mErrorIrqEnField != null && d2mErrorIrqEnField.Value;

        // Emulated-CM4 RPMsg name-service announcement. The HOST (CM55) posts
        // RX buffers and parks in xSemaphoreTake(boundSem) after registering
        // "am_ipc"; it binds only when the REMOTE announces the same name.
        // No CM4 core exists, so this synthesizes that announcement directly
        // into the HOST-RX vring in SHM and kicks via D2M + PEND_MSG IRQ:
        //   rpmsg_hdr {src, dst=NS(53), 0, len=40, flags=0} +
        //   ns_msg {"am_ipc", addr=1024, flags=CREATE(0)}.
        // SHM layout (HOST view, MEM_ALIGNMENT=32, VRING_COUNT=2):
        //   status(32) | RX bufs | TX bufs | RX vring | TX vring.
        private const uint RpmsgNsAddr = 0x35;
        private const uint RpmsgNsAnnounceAddr = 1024;
        private const uint SigMsgD2M = 0xA869;
        private const int MemAlignment = 32;
        private const int VringCount = 2;
        private const int VdevStatusSize = 32;
        private const int DefaultRpmsgBufferSize = 704;
        // g_sIpcBackend.buffer_size in HOST RAM (ble_freertos_fit.axf value;
        // per-example address, sanity-bounded on read).
        private const uint HostBackendBufSizeAddr = 0x200000B0u + 28;

        private void OnNsPoll()
        {
            TryInjectNsAnnouncement();
            if(nsPollLogCountdown > 0)
            {
                nsPollLogCountdown--;
            }
        }

        private int nsPollLogCountdown;

        private static ulong RoundUp(ulong x, ulong align)
        {
            return ((x + align - 1) / align) * align;
        }

        private static ulong VqRingSize(uint num, uint bufSize)
        {
            return RoundUp((ulong)bufSize * num, MemAlignment);
        }

        private static ulong VringSize(uint num)
        {
            ulong descsAvails = (ulong)num * 16 + 4 + (ulong)num * 2 + 2;
            return RoundUp(descsAvails, MemAlignment) + 4 + (ulong)num * 8 + 2;
        }

        private static uint OptimalNumDesc(uint shmSize, uint bufSize)
        {
            ulong available = shmSize >= VdevStatusSize ? shmSize - VdevStatusSize : 0;
            ulong single = (ulong)VringCount * (VqRingSize(1, bufSize) + VringSize(1));
            if(single == 0)
            {
                return 0;
            }
            ulong num = available / single;
            uint msb = 0;
            for(uint v = (uint)Math.Min(num, uint.MaxValue); v != 0; v >>= 1)
            {
                msb++;
            }
            if(msb == 0)
            {
                return 0;
            }
            return 1u << (int)(msb - 1);
        }

        private void TryInjectNsAnnouncement()
        {
            if(shmAddr == 0)
            {
                return;
            }
            // Buffer size is a HOST compile-time default (ble_fit uses -D288);
            // read it live from g_sIpcBackend (ble_fit: 0x200000B0+28).
            uint hostBufSize = ReadU32(HostBackendBufSizeAddr);
            uint bufSize = (hostBufSize >= 128 && hostBufSize <= 2048) ? hostBufSize : DefaultRpmsgBufferSize;
            uint num = OptimalNumDesc(shmSize, bufSize);
            if(num == 0)
            {
                return;
            }
            ulong bufsBase = RoundUp((ulong)shmAddr + VdevStatusSize, MemAlignment);
            ulong vqRingSize = VqRingSize(num, bufSize);
            // Two candidate vrings (rx first, then tx); the HOST posts RX buffers
            // into exactly one of them. Follow the posted one (naming varies).
            ulong rxCandVring = bufsBase + (ulong)VringCount * vqRingSize;
            ulong txCandVring = RoundUp(rxCandVring + VringSize(num), MemAlignment);
            // HOST-RX buffers always come from region 1; the posted control
            // vring is picked by avail (naming varies by backend version).
            ulong rxBufsBase = bufsBase;
            ulong rxBufsSize = vqRingSize;
            ulong rxVring = rxCandVring;
            uint availAtRx = ReadU16(rxCandVring + (ulong)num * 16 + 2);
            uint availAtTx = ReadU16(txCandVring + (ulong)num * 16 + 2);
            if(availAtTx >= num && availAtRx < num)
            {
                rxVring = txCandVring;
            }
            // RX vring: desc[num] (16B each), avail {flags,idx,ring[num]} at +num*16,
            // used {flags,idx,ring[num]x8B} ALIGNED (vring_init aligns used).
            ulong availIdxAddr = rxVring + (ulong)num * 16 + 2;
            ulong usedBaseAddr = RoundUp(rxVring + (ulong)num * 16 + 4 + (ulong)num * 2, MemAlignment);
            ulong usedIdxAddr = usedBaseAddr + 2;
            uint availIdx = ReadU16(availIdxAddr);
            uint usedIdx = ReadU16(usedIdxAddr);
            if(nsPollLogCountdown == 0)
            {
                this.Log(LogLevel.Info, "IPC: nspoll num={0} buf={1} vring=0x{2:X} avail={3} used={4} injected={5}/{6}", num, bufSize, rxVring, availIdx, usedIdx, nsInjectedThisGeneration, nsTotalInjections);
                nsPollLogCountdown = 200;
            }
            if(availIdx < num)
            {
                return; // HOST has not posted RX buffers yet
            }
            if(usedIdx == 0 && nsInjectedThisGeneration)
            {
                // Fresh vring generation (re-init resets used.idx): re-arm.
                nsInjectedThisGeneration = false;
                this.Log(LogLevel.Info, "IPC: RX vring re-init detected, re-arming NS announce");
            }
            if(nsInjectedThisGeneration)
            {
                return;
            }
            if(nsTotalInjections >= NsMaxInjections)
            {
                nsPollTimer.Enabled = false;
                this.Log(LogLevel.Warning, "IPC: NS announce attempts exhausted, stopping");
                return;
            }
            uint slot = usedIdx % num;
            uint descId = ReadU16(rxVring + (ulong)num * 16 + 4 + (ulong)slot * 2);
            if(descId >= num)
            {
                this.Log(LogLevel.Warning, "IPC: RX avail slot {0} has insane desc {1}", slot, descId);
                return;
            }
            ulong descAddr = rxVring + (ulong)descId * 16;
            ulong bufAddr = ReadU64(descAddr);
            if(bufAddr < rxBufsBase || bufAddr + 56 > rxBufsBase + rxBufsSize)
            {
                this.Log(LogLevel.Warning, "IPC: RX desc {0} points outside RX bufs (0x{1:X})", descId, bufAddr);
                return;
            }
            // Payload: rpmsg_hdr + ns_msg("am_ipc").
            var payload = new byte[56];
            Array.Copy(BitConverter.GetBytes(RpmsgNsAnnounceAddr), 0, payload, 0, 4); // src
            Array.Copy(BitConverter.GetBytes(RpmsgNsAddr), 0, payload, 4, 4); // dst = NS
            // reserved (8..11) = 0, len = 40 (12..13), flags (14..15) = 0
            Array.Copy(BitConverter.GetBytes((ushort)40), 0, payload, 12, 2);
            var nameBytes = System.Text.Encoding.ASCII.GetBytes("am_ipc");
            Array.Copy(nameBytes, 0, payload, 16, nameBytes.Length);
            Array.Copy(BitConverter.GetBytes(RpmsgNsAnnounceAddr), 0, payload, 48, 4); // ns addr
            // ns flags (52..55) = CREATE(0)
            Sysbus.WriteBytes(payload, bufAddr);
            ulong usedRingBase = usedBaseAddr + 4;
            WriteU32(usedRingBase + (ulong)slot * 8, descId);
            WriteU32(usedRingBase + (ulong)slot * 8 + 4, 56);
            WriteU16(usedIdxAddr, (ushort)(usedIdx + 1));
            nsInjectedThisGeneration = true;
            nsTotalInjections++;
            // Kick the HOST: D2M signal + PEND_MSG IRQ (and polled paths).
            d2mFifo.Enqueue(SigMsgD2M);
            UpdateIRQ();
            this.Log(LogLevel.Info, "IPC: injected NS announce 'am_ipc' (desc {0} buf 0x{1:X}, used {2}->{3}), kicked D2M", descId, bufAddr, usedIdx, usedIdx + 1);
        }

        private IBusController Sysbus => machine.GetSystemBus(this);

        private uint ReadU16(ulong addr)
        {
            return BitConverter.ToUInt16(Sysbus.ReadBytes(addr, 2), 0);
        }

        private uint ReadU32(ulong addr)
        {
            return BitConverter.ToUInt32(Sysbus.ReadBytes(addr, 4), 0);
        }

        private ulong ReadU64(ulong addr)
        {
            return BitConverter.ToUInt64(Sysbus.ReadBytes(addr, 8), 0);
        }

        private void WriteU16(ulong addr, ushort value)
        {
            Sysbus.WriteBytes(BitConverter.GetBytes(value), addr);
        }

        private void WriteU32(ulong addr, uint value)
        {
            Sysbus.WriteBytes(BitConverter.GetBytes(value), addr);
        }

        private enum Registers : long
        {
            M2DData = 0x00,
            D2MData = 0x04,
            Status = 0x08,
            M2DThreshold = 0x10,
            M2DInterruptStatus = 0x14,
            M2DInterruptEnable = 0x18,
            D2MError = 0x1C,
            D2MThreshold = 0x20,
            D2MInterruptStatus = 0x24,
            D2MInterruptEnable = 0x28,
        }
    }
}
