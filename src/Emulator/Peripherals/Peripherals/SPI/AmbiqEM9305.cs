//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Time;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.SPI
{
    /// <summary>
    /// Marker interface for SPI slaves that require true full-duplex behaviour:
    /// the master must capture the bytes returned by Transmit() (MISO) even while
    /// driving a write transaction. Slaves that do not implement this keep the
    /// legacy half-duplex behaviour where MISO is discarded on writes.
    /// </summary>
    public interface ISpiFullDuplexSlave { }

    //***************************************************************************
    //
    /// <summary>
    /// Behavioral model of the Ambiq EM9305 BLE 5.4 radio die as seen from an
    /// Apollo510B host over the Serial-HCI-over-SPI transport (IOM module 6).
    ///
    /// Wire protocol (from am_devices_em9305.c / cordio):
    ///   TX handshake: host clocks [0x42][0x00] in one IOM transaction; radio returns
    ///                 MISO[1]=free_space (bytes the FIFO can accept, 1-byte field).
    ///   RX handshake: host clocks [0x81][0x00]; radio returns MISO[1]=bytes_available.
    ///   0xC0 (EM9305_STS1_READY_VALUE) marks the status byte; byte[1] is a count.
    ///   Commands are self-delimiting HCI/VSC packets: [type][op_lo][op_hi][plen][params...].
    ///   MISO during data-write transactions is not consumed by the host (any value OK).
    ///
    /// The host's am_devices_em9305_block_write() loops, and each iteration does one TX
    /// handshake transaction followed by a data transaction of at most `free_space` bytes.
    /// Because free_space is a single byte (max 255) and the largest command
    /// (WRITE_AT_ADDRESS with a 248-byte payload = 1+2+1+4+248 = 256 bytes) exceeds that,
    /// such commands are split across two bursts (255 + 1 byte), each preceded by its own
    /// [0x42][0x00] handshake. The model therefore re-assembles a command across multiple
    /// data transactions and only dispatches it once plen bytes have been received. A data
    /// transaction is never exactly the two-byte sequence [0x42,0x00], so that pattern is an
    /// unambiguous handshake marker even when firmware payload happens to contain 0x42.
    ///
    /// RDY line semantics (required for am_devices_em9305.c to succeed):
    ///   HIGH while a command-complete/event packet is queued unread, or CS is low and the
    ///         radio is ready (tx_starts asserts CS then waits up to 1.2s for RDY to rise
    ///         before clocking). LOW when CS is high and nothing is pending.
    ///
    /// Firmware update (ble_firmware_update example, am_devices_em9305.c):
    ///   * Configuration mode is entered by pulsing EN then driving a 30 kHz square wave on
    ///     the CM/CLK GPIO (ball 15) for ~40 ms. The radio replies with event {0x04,0xFF,0x01,
    ///     0x03} ("entered CM"). The model detects sustained toggling on ball 15 and emits it.
    ///   * NVM/flash is emulated: WRITE_AT_ADDRESS(0xFD03) stores bytes, READ_AT_ADDRESS
    ///     (0xFD01) returns stored bytes, CRC_CALCULATE(0xFC4E) returns the IEEE CRC-32 of a
    ///     [start,end) range so check_programed()/check_data() can verify against am_hal_crc32.
    /// </summary>
    public class AmbiqEM9305 : ISPIPeripheral, ISpiFullDuplexSlave, IGPIOReceiver
    {
        private const byte HeaderTx = 0x42;   // EM9305_SPI_HEADER_TX: host -> radio write
        private const byte HeaderRx = 0x81;   // EM9305_SPI_HEADER_RX: host requests read

        // Apollo510B SiP ball/PAD numbers (host GPIO pin index == PAD number).
        private const int EnBall = 93;    // EM9305_EN: enable/reset input.
        private const int CsBall = 149;   // IOM6 chip select output from the host.
        private const int CmBall = 15;    // EM9305_CM/CLK: CM-entry square-wave / clock pin.

        private const byte StsReady = 0xC0;    // EM9305_STS1_READY_VALUE
        private const byte FreeSpaceByte = 255; // advertised FIFO space (single-byte field, max value)

        // HCI packet types and event code.
        private const byte TypeCommand = 0x01;
        private const byte TypeEvent = 0x04;
        private const byte EvtCommandComplete = 0x0E;

        // Opcodes answered with a data payload during host bring-up / HciResetSequence.
        private const ushort OpcReadBdAddress          = 0x1009;
        private const ushort OpcLeReadBufferSize       = 0x2002;
        private const ushort OpcLeReadLocalSupFeat     = 0x2003;
        private const ushort OpcLeRand                 = 0x2018;
        private const ushort OpcLeReadSupportedStates  = 0x201C;
        private const ushort OpcLeReadResolvingList    = 0x202A;
        private const ushort OpcLeReadMaxDataLength    = 0x202F;

        // Standard LE advertising opcodes (answered so the host can drive real over-the-air ADV_Ind).
        private const ushort OpcLeSetAdvParams     = 0x2006;
        private const ushort OpcLeSetRandomAddr    = 0x2007;
        private const ushort OpcLeSetAdvData       = 0x2008;
        private const ushort OpcLeSetScanRspData   = 0x2009;
        private const ushort OpcLeSetAdvEnable     = 0x200A;

        // Vendor-specific opcodes (OGF == 0x3F).
        private const ushort VscSetDevPubAddr   = 0xFC43;
        private const ushort VscCrcCalculate    = 0xFC4E;
        private const ushort VscReadAtAddress   = 0xFD01;
        private const ushort VscWriteAtAddress  = 0xFD03;
        private const ushort VscNvmEraseMain    = 0xFD06;
        private const ushort VscNvmErasePage    = 0xFD07;
        private const ushort VscSetLocalSupFeat = 0xFFF2;

        // Firmware-update constants (am_devices_em9305.h).
        private const int MaxReadData = 248;      // EM9305_NVM_INFO_READ_LEN / PACKET_SIZE

        // NVM/flash map. First firmware record lives at 0x300000 and the largest record
        // (234240 bytes at 0x302400) spans to ~0x33BAC0; the version info page is at 0x402000.
        private const uint NvmBase = 0x0030_0000;
        private const int NvmSize = 0x110_000;    // ~1.06 MB, covers records + the info page

        private enum State { Idle, RxCountReq, RxData }

        public AmbiqEM9305(IMachine machine)
        {
            this.machine = machine;
            Rdypin = new GPIO();
            Enpin = new GPIO();
            cmdBuffer = new List<byte>();
            txnBuf = new List<byte>();
            rxTxFifo = new Queue<byte>();
            deviceAddress = new byte[] { 0x02, 0x1F, 0xDE, 0xAD, 0xBE, 0xEF };
            localSupFeat = DefaultLocalSupFeat();

            // EN (ball 93) is host-driven. A low->high edge triggers the reset /
            // enter-active-state sequence, mirroring am_devices_em9305_reset().
            Enpin.AddStateChangedHook(OnEnChanged);
        }

        // Exposed as read-only properties: Renode's .repl connection parser resolves connectable
        // pins via reflection GetProperty(), so field-based pins are not discoverable.
        /// <summary>Radio-ready / interrupt line (EM9305 -> Apollo ball 117).</summary>
        public GPIO Rdypin { get; }

        /// <summary>Enable/reset input (Apollo ball 93 -> EM9305).</summary>
        public GPIO Enpin { get; }

        public void Reset()
        {
            state = State.Idle;
            commandInProgress = false;
            cmdBuffer.Clear();
            txnBuf.Clear();
            rxTxFifo.Clear();
            bytesLeftToServe = 0;
            csLow = false;
            radioReady = false;
            cmEdgeCount = 0;
            cmFiredThisEp = false;
            cmLastLevel = false;
            advActive = false;
            advData = null;
            advSeq = 0;
            advChannel = 37;
            booting = true;   // RDY held at power-up reset value until EN is asserted.
            Rdypin.Set(true);
        }

        //***************************************************************************
        //
        /// <summary>
        /// Drive one full-duplex SPI byte: <paramref name="data"/> is the host's MOSI
        /// input, and the returned value is the EM9305 MISO output for that bit period.
        /// </summary>
        public byte Transmit(byte data)
        {
            // Track transaction membership so FinishTransmission can classify each IOM transfer.
            if (!inTxn)
            {
                inTxn = true;
                txnLen = 0;
                txnBuf.Clear();
            }

            int pos = txnLen++;
            txnBuf.Add(data);
            this.Log(LogLevel.Info, "TX pos={0} mosi=0x{1:X2} cmdInProg={2} state={3}", pos, data, commandInProgress, state);

            // --- TX handshake MISO (first AND any continuation handshake) ----------
            // am_devices_em9305_tx_starts() requires byte[0]==0xC0 and byte[1]!=0 on every
            // [0x42][0x00] exchange, so this must be answered regardless of command state.
            if (pos == 0 && data == HeaderTx)
            {
                this.Log(LogLevel.Noisy, "EM9305: TX header 0x{0:X2} seen", data);
                return StsReady;   // MISO[0] of the status reply
            }

            if (pos == 1 && txnBuf.Count >= 2 && txnBuf[0] == HeaderTx && data == 0x00)
            {
                this.Log(LogLevel.Noisy, "EM9305: TX handshake accepted, advertising {0} byte(s)", FreeSpaceByte);
                return FreeSpaceByte;   // MISO[1]: FIFO space for the following data burst
            }

            // --- RX read path (only when not in the middle of assembling a command) -
            this.Log(LogLevel.Noisy, "EM9305: RX path check commandInProgress={0} state={1} pos={2} data=0x{3:X2} txnLen={4}", commandInProgress, state, pos, data, txnLen);
            if (!commandInProgress)
            {
                switch (state)
                {
                    case State.Idle:
                        this.Log(LogLevel.Noisy, "EM9305: Idle check pos={0} data=0x{1:X2} txnLen={2} txnBuf=[{3}]", pos, data, txnLen, string.Join(",", txnBuf));
                        if (pos == 0 && data == HeaderRx)
                        {
                            state = State.RxCountReq;
                            this.Log(LogLevel.Noisy, "EM9305: RX header 0x{0:X2} seen pos={1} txnLen={2}", data, pos, txnLen);
                            return StsReady;   // MISO[0] of the status reply
                        }
                        // Also handle RX header at pos==1 when previous byte was TX header's second byte
                        // Host may do [0x42,0x00] + data in one burst, but RX is separate
                        if (data == HeaderRx)
                        {
                            state = State.RxCountReq;
                            this.Log(LogLevel.Noisy, "EM9305: RX header 0x{0:X2} seen at pos={1} (late)", data, pos);
                            return StsReady;
                        }

                        // 0x00 filler while idle is normal host polling for RDY (T_RDY hunt)
                        // Don't warn for 0x00, just return 0x00
                        if (data == 0x00)
                        {
                            return 0x00;
                        }
                        this.Log(LogLevel.Warning, "EM9305: unexpected MOSI byte 0x{0:X2} while idle (txnLen={1} txnBuf=[{2}] inTxn={3} csLow={4} state={5} cmdInProg={6})", data, txnLen, string.Join(",", txnBuf), inTxn, csLow, state, commandInProgress);
                        return 0x00;

                    case State.RxCountReq:
                        this.Log(LogLevel.Noisy, "EM9305: RxCountReq pos={0} data=0x{1:X2} avail={2} state={3}", pos, data, rxTxFifo.Count, state);
                        int avail = rxTxFifo.Count;
                        bytesLeftToServe = avail;
                        // Stay Idle when nothing is queued: lingering in RxData after an empty "ready"
                        // made the next [0x81] hit State.RxData and return MISO[0]=0x00 instead of 0xC0.
                        state = avail > 0 ? State.RxData : State.Idle;
                        this.Log(LogLevel.Info, "EM9305: RxCountReq -> MISO count 0x{0:X2} ({1} avail) state={2}", (byte)(avail & 0xFF), avail, state);
                        return (byte)(avail & 0xFF);

                    case State.RxData:
                        if (rxTxFifo.Count > 0 && bytesLeftToServe > 0)
                        {
                            var outByte = rxTxFifo.Dequeue();
                            this.Log(LogLevel.Noisy, "EM9305: RX data pos={0} -> MISO 0x{1:X2}", pos, outByte);
                            bytesLeftToServe--;
                            if (bytesLeftToServe == 0)
                            {
                                state = State.Idle;   // logical read complete
                            }

                            UpdateRdy();
                            return outByte;
                        }

                        this.Log(LogLevel.Info, "EM9305: RX read but nothing queued (fifo={0}) -> MISO 0x00", rxTxFifo.Count);
                        state = State.Idle;
                        return 0x00;
                }
            }

            // --- command data write (MISO unused by the host during payload writes) --
            return 0x00;
        }

        //***************************************************************************
        //
        /// <summary>
        /// Called by the master when an IOM transaction completes. Classifies the just-finished
        /// transfer: a [0x42,0x00] handshake arms command assembly (clearing the buffer only for a
        /// brand-new command), and any other bytes while a command is in flight are appended to it
        /// until the self-delimiting packet completes.
        /// </summary>
        public void FinishTransmission()
        {
            inTxn = false;

            bool isTxHandshake = txnBuf.Count == 2 && txnBuf[0] == HeaderTx && txnBuf[1] == 0x00;
            this.Log(LogLevel.Info, "FIN handshake={0} n={1} buf=[{2}] cmdInProg={3}", isTxHandshake, txnBuf.Count, string.Join(",", txnBuf), commandInProgress);

            if (isTxHandshake)
            {
                // A data burst follows. Start a fresh command only when none is already in flight;
                // otherwise this is a continuation of an over-long command split across bursts.
                if (!commandInProgress)
                {
                    cmdBuffer.Clear();
                }

                commandInProgress = true;
            }
            else if (commandInProgress)
            {
                for (int i = 0; i < txnBuf.Count; i++)
                {
                    cmdBuffer.Add(txnBuf[i]);
                }

                if (cmdBuffer.Count >= 4)
                {
                    int total = 4 + cmdBuffer[3];   // header(4) + param_len
                    if (cmdBuffer.Count >= total)
                    {
                        ProcessPacket(cmdBuffer.ToArray());
                        commandInProgress = false;
                        state = State.Idle;
                    }
                }
            }

            // Do not reset RxCountReq here - the host does [0x81] + [0x00] as two
            // separate single-byte transactions for the RX handshake. The first
            // sets state to RxCountReq, the second should be handled as RxCountReq
            // not Idle. Only reset if no RX header was seen (state should stay
            // RxCountReq for the next transaction's second byte).
            // The original dangling check was for a single-byte [0x81] with no follow-up,
            // but in practice the host always does two bytes, so we keep the state.
            // State will be handled in the next Transmit call's RxCountReq case.

            UpdateRdy();
        }

        //***************************************************************************
        //
        /// <summary>Trigger the enter-active-state sequence (EN low->high edge).</summary>
        public void StartResetSequence()
        {
            this.Log(LogLevel.Info, "EM9305: EN rising edge -> entering active state");

            state = State.Idle;
            commandInProgress = false;
            cmdBuffer.Clear();
            rxTxFifo.Clear();
            bytesLeftToServe = 0;
            radioReady = false;
            booting = true;
            DriveRdy(true);   // power-up value: SPI_RDY is pulled high first.

            // Each EN pulse starts a fresh configuration-mode entry attempt (the host pulses EN
            // before every CM-entry square wave), so re-arm the CM edge counter for this cycle.
            cmEdgeCount = 0;
            cmFiredThisEp = false;

            // RDY drops during the (short) boot, then rises once the active-state event
            // becomes available for the host to read.
            machine.ScheduleAction(TimeInterval.FromMilliseconds(1), _ =>
            {
                DriveRdy(false);   // reset in progress

                machine.ScheduleAction(TimeInterval.FromMilliseconds(1), _ =>
                {
                    booting = false;
                    radioReady = true;
                    EnqueueData(new byte[] { 0x04, 0xFF, 0x01, 0x01 });   // active_state_entered_evt
                    this.Log(LogLevel.Info, "EM9305: active state entered (reset event queued)");
                }, "em9305-active");
            }, "em9305-boot");
        }

        //***************************************************************************
        //
        private void ProcessPacket(byte[] pkt)
        {
            if (pkt.Length < 4)
            {
                return;
            }

            byte type = pkt[0];
            ushort opcode = (ushort)(pkt[1] | (pkt[2] << 8));   // OGF|OOC

            if (type == TypeCommand)
            {
                this.Log(LogLevel.Info, "EM9305: HCI cmd opcode=0x{0:X4} param_len={1}", opcode, pkt[3]);
                EmitCapture("TX", pkt);   // firmware -> radio: full H4 command frame [type][opc_lo][opc_hi][plen][params]
                HandleCommand(opcode, pkt);
            }
            else
            {
                this.Log(LogLevel.Noisy, "EM9305: ignored non-command packet type=0x{0:X2} opcode=0x{1:X4}", type, opcode);
            }
        }

        private void HandleCommand(ushort opcode, byte[] cmd)
        {
            int ogf = (opcode >> 10) & 0x3F;

            if (ogf == 0x3F)   // vendor specific
            {
                HandleVendorSpecific(opcode, cmd);
                return;
            }

            switch (opcode)
            {
                case OpcReadBdAddress:
                    EnqueueCommandComplete(opcode, 0x00, deviceAddress);
                    break;

                case OpcLeReadBufferSize:
                {
                    // status + HCI_ACL_Data_Length(u16 LE) + Num_HCI_Data_Packets(u8).
                    var p = new byte[] { 0xE0, 0x03, 0x06 };   // 992 bytes, 6 buffers
                    EnqueueCommandComplete(opcode, 0x00, p);
                    break;
                }

                case OpcLeReadLocalSupFeat:
                    EnqueueCommandComplete(opcode, 0x00, localSupFeat);
                    break;

                case OpcLeRand:
                {
                    var rnd = new byte[8];
                    for (int i = 0; i < 8; i++)
                    {
                        rnd[i] = NextRandom();
                    }

                    EnqueueCommandComplete(opcode, 0x00, rnd);
                    break;
                }

                case OpcLeReadSupportedStates:
                    EnqueueCommandComplete(opcode, 0x00, new byte[] { 0xFF, 0xFF, 0x1F, 0x01, 0x00, 0x00, 0x00, 0x00 });
                    break;

                case OpcLeReadResolvingList:
                    EnqueueCommandComplete(opcode, 0x00, new byte[] { 0x04 });   // resolving list size
                    break;

                case OpcLeReadMaxDataLength:
                    EnqueueCommandComplete(opcode, 0x00, new byte[] { 0xC0, 0x03, 0xC0, 0x03 });   // tx/rx octets
                    break;

                case OpcLeSetAdvParams:   // [advIntMin u16][advIntMax u16][type][txAddr][chanMap ...]
                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case OpcLeSetRandomAddr:  // [random address 6 bytes] (advertise the public deviceAddress instead)
                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case OpcLeSetAdvData:     // HCI params are the raw advertising data (no inner length byte).
                    {
                        int alen = Math.Min((int)cmd[3], cmd.Length - 4);   // plen, clamped to what arrived
                        if (alen < 0)
                        {
                            alen = 0;
                        }

                        advData = new byte[alen];
                        Array.Copy(cmd, 4, advData, 0, alen);              // params start at index 4
                        this.Log(LogLevel.Info, "EM9305: Set Advertising Data len={0}", alen);
                    }

                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case OpcLeSetScanRspData: // [length u8][scan response data ...] (not needed for ADV_Ind)
                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case OpcLeSetAdvEnable:   // [enable u8]: 1 = start advertising
                    if (cmd.Length >= 5)
                    {
                        StartAdvertising(cmd[4] != 0);
                    }

                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                default:
                    // Set/config commands and everything else -> Command Complete, no return data.
                    EnqueueCommandComplete(opcode, 0x00);
                    break;
            }
        }

        private void HandleVendorSpecific(ushort opcode, byte[] cmd)
        {
            this.Log(LogLevel.Info, "EM9305: VSC opcode=0x{0:X4} param_len={1}", opcode, cmd.Length >= 4 ? cmd[3] : 0);

            switch (opcode)
            {
                case VscSetDevPubAddr:   // params = 6-byte public address
                    if (cmd.Length >= 10)
                    {
                        Array.Copy(cmd, 4, deviceAddress, 0, 6);
                    }

                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case VscSetLocalSupFeat: // params = 8-byte LE feature set
                    if (cmd.Length >= 12)
                    {
                        Array.Copy(cmd, 4, localSupFeat, 0, 8);
                    }

                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case VscCrcCalculate:
                {
                    // params = [start u32 LE][end u32 LE]; reply carries the IEEE CRC-32 of [start,end).
                    uint start = ReadU32Le(cmd, 4);
                    uint end = cmd.Length >= 12 ? ReadU32Le(cmd, 8) : start;
                    int count = (int)(end - start);
                    if (count < 0)
                    {
                        count = 0;
                    }

                    uint crc = Crc32Range(start, count);
                    EnqueueCommandComplete(opcode, 0x00, new byte[] { (byte)crc, (byte)(crc >> 8), (byte)(crc >> 16), (byte)(crc >> 24) });
                    break;
                }

                case VscReadAtAddress:
                {
                    // params = [addr u32 LE][len]; reply carries `len` bytes from NVM.
                    uint addr = ReadU32Le(cmd, 4);
                    int len = cmd.Length >= 9 ? cmd[8] : 0;
                    if (len > MaxReadData)
                    {
                        len = MaxReadData;
                    }

                    EnqueueCommandComplete(opcode, 0x00, ReadNvmRange(addr, len));
                    break;
                }

                case VscWriteAtAddress:
                {
                    // params = [addr u32 LE][data...]; store into NVM.
                    if (cmd.Length >= 9)
                    {
                        uint addr = ReadU32Le(cmd, 4);
                        int dataLen = cmd.Length - 8;   // total packet minus header(4) and address(4)
                        var buf = new byte[dataLen];
                        Array.Copy(cmd, 8, buf, 0, dataLen);
                        WriteNvm(addr, buf);
                    }

                    EnqueueCommandComplete(opcode, 0x00);
                    break;
                }

                case VscNvmEraseMain:    // no params: erase the whole main flash area to erased state.
                    EraseNvm(NvmBase, NvmSize);
                    this.Log(LogLevel.Info, "EM9305: erased NVM main area");
                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                case VscNvmErasePage:    // params = [area][page]: clear that page (informational here).
                    this.Log(LogLevel.Noisy, "EM9305: erase NVM page area={0} page={1}", cmd.Length >= 4 ? cmd[4] : 0, cmd.Length >= 5 ? cmd[5] : 0);
                    EnqueueCommandComplete(opcode, 0x00);
                    break;

                default:
                    EnqueueCommandComplete(opcode, 0x00);
                    break;
            }
        }

        /// <summary>
        /// Build and enqueue a Command Complete event. Layout (matches the cordio host):
        /// [type=04][evt=0E][plen][numHci=1][op_lo][op_hi][status][data...] where plen = 4 + data.Length.
        /// The host reads status at byte offset 6 and payload from offset 7 onward.
        /// </summary>
        private void EnqueueCommandComplete(ushort opcode, byte status, params byte[] data)
        {
            int plen = 4 + data.Length;   // numHci(1) + opcode(2) + status(1) + data

            var evt = new List<byte>(3 + plen);
            evt.Add(TypeEvent);
            evt.Add(EvtCommandComplete);
            evt.Add((byte)plen);
            evt.Add(0x01);                                  // num_hci_command_packets
            evt.Add((byte)(opcode & 0xFF));                 // op_lo
            evt.Add((byte)((opcode >> 8) & 0xFF));          // op_hi
            evt.Add(status);
            foreach (var b in data)
            {
                evt.Add(b);
            }

            this.Log(LogLevel.Info, "EM9305: CMD_CMPL opcode=0x{0:X4} status=0x{1:X2} dataLen={2}", opcode, status, data.Length);
            EnqueueData(evt);
        }

        private void EnqueueData(IEnumerable<byte> data)
        {
            byte[] arr;
            if (data is byte[] existing)
            {
                arr = existing;
            }
            else
            {
                var list = new List<byte>(data);
                arr = list.ToArray();
            }

            // Every enqueued packet is a complete HCI event already framed with its H4 type byte
            // ([type=0x04][evt_code][plen][...]); emit it so the host<->radio traffic can be captured.
            if (arr.Length > 0 && arr[0] >= 1 && arr[0] <= 4)
            {
                EmitCapture("RX", arr);   // radio -> firmware: full H4 event frame
            }

            int n = 0;
            foreach (var b in arr)
            {
                rxTxFifo.Enqueue(b);
                n++;
            }

            UpdateRdy();
            this.Log(LogLevel.Noisy, "EM9305: enqueued {0} byte(s), fifo={1}", n, rxTxFifo.Count);
        }

        //***************************************************************************
        //
        /// <summary>
        /// Emit a structured capture line for one complete HCI-over-SPI frame. The run harness parses
        /// these lines and reassembles them into a Bluetooth-HCI (DLT 103, H4) .pcapng viewable in Wireshark.
        /// Format: EM9305CAP &lt;TX|RX&gt; &lt;hex bytes of the full H4 frame&gt;.
        /// </summary>
        private void EmitCapture(string dir, byte[] frame)
        {
            var hex = new char[frame.Length * 2];
            for (int i = 0; i < frame.Length; i++)
            {
                hex[i * 2] = "0123456789ABCDEF"[frame[i] >> 4];
                hex[i * 2 + 1] = "0123456789ABCDEF"[frame[i] & 0x0F];
            }

            this.Log(LogLevel.Info, "EM9305CAP {0} {1}", dir, new string(hex));
        }

        //***************************************************************************
        // Over-the-air advertising. When the host enables standard LE advertising with data, the model emits a
        // legacy ADV_Ind link-layer frame on an advertising channel and logs it as an EM9305AIR line (BLESniffer-style
        // 10-byte metadata prefix + raw air bytes). The run harness turns those lines into a Bluetooth-LE linklayer pcap
        // so Wireshark dissects the OTA PDU layer per example.
        //***************************************************************************
        private void StartAdvertising(bool enable)
        {
            if (!enable)
            {
                advActive = false;
                this.Log(LogLevel.Info, "EM9305: advertising disabled");
                return;
            }

            if (advData == null || advData.Length == 0)
            {
                // No advertising data set yet; emit a minimal valid AD structure so the PDU is well-formed.
                advData = new byte[] { 0x02, 0x01, 0x82 };
            }

            advActive = true;
            this.Log(LogLevel.Info, "EM9305: advertising enabled -> emitting ADV_Ind over the air");
            SendAdvertisement();
        }

        private void SendAdvertisement()
        {
            if (!advActive || advData == null)
            {
                return;
            }

            advChannel = 37 + (advSeq % 3);   // hop across the three advertising channels (37/38/39)
            EmitAirFrame(BuildAdvIndFrame());
            advSeq++;

            // Reschedule the next advertisement. Scheduling needs a running time source; when the peripheral is driven
            // outside an active emulation (e.g. unit tests) this is best-effort and skipped silently.
            try
            {
                machine.ScheduleAction(TimeInterval.FromMilliseconds(40), _ => SendAdvertisement(), "em9305-adv");
            }
            catch (Exception)
            {
                // Not emulating -- a single advertisement has already been emitted; nothing to reschedule.
            }
        }

        // Legacy ADV_Ind link-layer frame: AA(D6 BE 89 8E) + PDU header (0x02 = ADV_Ind, public addr) + AdvA(6) + AdvData.
        private byte[] BuildAdvIndFrame()
        {
            var frame = new byte[11 + advData.Length];
            frame[0] = 0xD6;
            frame[1] = 0xBE;
            frame[2] = 0x89;
            frame[3] = 0x8E;                       // advertising access address (little-endian on air)
            frame[4] = 0x00;                        // LL PDU header: PDU type ADV_IND (0), public device address
            Array.Copy(deviceAddress, 0, frame, 5, 6);    // AdvA
            Array.Copy(advData, 0, frame, 11, advData.Length);   // Advertising Data (AD structures)
            return frame;
        }

        private void EmitAirFrame(byte[] airFrame)
        {
            var framed = FrameWithSnifferHeader(airFrame);
            var hex = new char[framed.Length * 2];
            for (int i = 0; i < framed.Length; i++)
            {
                hex[i * 2] = "0123456789ABCDEF"[framed[i] >> 4];
                hex[i * 2 + 1] = "0123456789ABCDEF"[framed[i] & 0x0F];
            }

            this.Log(LogLevel.Info, "EM9305AIR {0}", new string(hex));
            AirFrameSent?.Invoke(framed);
        }

        // Mirrors BLESniffer.InsertHeaderToPacket() for the legacy-advertising case (access address == 0x8E89BED6): a 10-byte
        // Ubertooth-style metadata prefix ([chanIdx][signalPwr][noisePwr][aaOffenses][refAA x4][flags u16]) followed by the raw
        // over-the-air PDU. This is exactly what Renode writes into its native BLE tap, so a linktype-256 pcap built from these
        // frames dissects identically in Wireshark's Bluetooth-LE dissector.
        private byte[] FrameWithSnifferHeader(byte[] airFrame)
        {
            var outPkt = new byte[airFrame.Length + 10];
            outPkt[0] = (byte)(advChannel == 37 ? 0 : (advChannel == 38 ? 12 : 39));   // Wireshark channel index
            outPkt[1] = 0x00;     // signal power
            outPkt[2] = 0x00;     // noise power
            outPkt[3] = 0x00;     // access address offenses
            Array.Copy(airFrame, 0, outPkt, 4, 4);   // reference access address (AA)
            ushort flags = 0x3C3F;                    // BLESniffer advertisement flag set (Advertisement bit stays clear)
            outPkt[8] = (byte)(flags & 0xFF);
            outPkt[9] = (byte)((flags >> 8) & 0xFF);
            Array.Copy(airFrame, 0, outPkt, 10, airFrame.Length);   // raw over-the-air PDU bytes
            return outPkt;
        }

        //***************************************************************************
        //
        /// <summary>
        /// Receives a drive from an Apollo GPIO output (wired as `<ball> -> em9305@<ball>`).
        /// EN (ball 93) triggers the reset sequence; CS (ball 149) selects the radio and, while
        /// asserted with the radio ready, holds RDY high; CM/CLK (ball 15) carries the configuration-
        /// mode entry square wave.
        /// </summary>
        public void OnGPIO(int number, bool value)
        {
            if (number == EnBall)
            {
                Enpin.Set(value);
            }
            else if (number == CsBall)
            {
                // EM9305 SPI CS is ACTIVE-LOW: am_devices_em9305 selects with OUTPUT_CLEAR (ball low)
                // and deselects with OUTPUT_SET (ball high). So "selected" == pin LOW.
                csLow = !value;
                this.Log(LogLevel.Noisy, "EM9305: CS {0}", value ? "released" : "asserted");
                UpdateRdy();
            }
            else if (number == CmBall)
            {
                DetectCmWave(value);
            }
            else
            {
                this.Log(LogLevel.Warning, "EM9305: unhandled GPIO input on ball {0} (value {1})", number, value);
            }
        }

        private void OnEnChanged(bool isSet)
        {
            if (isSet)
            {
                StartResetSequence();
            }
        }

        //***************************************************************************
        //
        /// <summary>
        /// Detect the 30 kHz CM-entry square wave on ball 15. The host pulses EN before every attempt and
        /// then drives ~40 ms of rapid toggling, so a fresh edge counter is armed on each EN pulse (see
        /// StartResetSequence) and counts edges until enough have accumulated to declare the wave; it emits
        /// the "entered configuration mode" event {0x04,0xFF,0x01,0x03} exactly once per attempt and asserts
        /// RDY so enter_cm_mode's block_read proceeds. Pending events (e.g. the reset active-state event) are
        /// discarded for a clean CM entry reply.
        /// </summary>
        private void DetectCmWave(bool value)
        {
            if (value == cmLastLevel)
            {
                return;   // not an edge
            }

            cmLastLevel = value;
            cmEdgeCount++;

            if (!cmFiredThisEp && cmEdgeCount >= CmEpisodeThreshold)
            {
                cmFiredThisEp = true;
                this.Log(LogLevel.Info, "EM9305: CM square wave detected ({0} edges) -> entering configuration mode", cmEdgeCount);

                rxTxFifo.Clear();   // drop stale events (e.g. active_state) so the reply is unambiguous
                EnqueueData(new byte[] { 0x04, 0xFF, 0x01, 0x03 });   // entered_cm_evt
                radioReady = true;
                UpdateRdy();        // assert RDY: SPI data ready for enter_cm_mode to read
            }
        }

        private void DriveRdy(bool v)
        {
            if (v != rdyLast)
            {
                this.Log(LogLevel.Info, "EM9305: RDY -> {0}", v ? 1 : 0);
                rdyLast = v;
            }

            Rdypin.Set(v);
        }

        /// <summary>
        /// RDY/SPI_RDY is the "device present & ready" line: it must read HIGH whenever the radio is up
        /// and in active state so that am_devices_em9305_tx_starts()/em9305_spi_begin() can start a WRITE
        /// handshake (it asserts CS then polls RDY until high). Holding it low while idle made every
        /// command write time out before the 0x42 header was even sent, so no HCI command ever went out.
        /// "No data" for reads is signalled by the MISO count byte being 0 (block_read then returns
        /// NOT_READY after a bounded retry), not by deasserting RDY. During the boot window the reset
        /// sequence drives the pin explicitly instead.
        /// </summary>
        private void UpdateRdy()
        {
            if (booting)
            {
                return;   // reset sequence owns the pin for now
            }

            // SPI_RDY is asserted high in two distinct situations:
            //  - "data available": while the RX FIFO holds an unread event/response. block_read's
            //    `do{...}while(RDY)` re-checks RDY only after EM9305_SPI_DEVICE_DESELECT() (CSN
            //    deasserted), so at that point csLow is false and RDY correctly drops once drained.
            //  - "write ready": while CSN is asserted (a host block_write has selected the radio).
            //    am_devices_em9305_tx_starts()/em9305_spi_begin() asserts CS then polls RDY until it
            //    goes high before clocking the [0x42] header; with an empty FIFO holding RDY low made
            //    every command write time out, so no HCI command ever went out.
            DriveRdy(rxTxFifo.Count > 0 || csLow);
        }

        //***************************************************************************
        // NVM/flash storage. Lazily allocated and initialized to the erased value (0xFF).
        //***************************************************************************
        private void EnsureNvm()
        {
            if (nvm == null)
            {
                nvm = new byte[NvmSize];
                Array.Fill(nvm, (byte)0xFF);
            }
        }

        private static uint ReadU32Le(byte[] b, int off) =>
            (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));

        private void WriteNvm(uint addr, byte[] data)
        {
            EnsureNvm();
            long start = (long)addr - NvmBase;
            for (int i = 0; i < data.Length; i++)
            {
                long idx = start + i;
                if (idx >= 0 && idx < nvm.Length)
                {
                    nvm[idx] = data[i];
                }
            }
        }

        private byte[] ReadNvmRange(uint addr, int len)
        {
            EnsureNvm();
            var outBuf = new byte[len];
            long start = (long)addr - NvmBase;
            for (int i = 0; i < len; i++)
            {
                long idx = start + i;
                outBuf[i] = (idx >= 0 && idx < nvm.Length) ? nvm[idx] : (byte)0xFF;   // out of range reads as erased
            }

            return outBuf;
        }

        private void EraseNvm(uint addr, int len)
        {
            EnsureNvm();
            long start = (long)addr - NvmBase;
            for (int i = 0; i < len; i++)
            {
                long idx = start + i;
                if (idx >= 0 && idx < nvm.Length)
                {
                    nvm[idx] = 0xFF;
                }
            }
        }

        //***************************************************************************
        // IEEE CRC-32 (zlib/Ethernet): reflected, polynomial 0x04C11DB7 (table form 0xEDB88320),
        // init 0xFFFFFFFF, final XOR-out 0xFFFFFFFF. Matches Ambiq's am_hal_crc32() hardware engine.
        //***************************************************************************
        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
                }

                t[n] = c;
            }

            return t;
        }

        private uint Crc32Range(uint startAddr, int count)
        {
            EnsureNvm();
            uint crc = 0xFFFF_FFFFu;
            long idx = (long)startAddr - NvmBase;
            for (int i = 0; i < count; i++)
            {
                byte b = ((idx + i) >= 0 && (idx + i) < nvm.Length) ? nvm[idx + i] : (byte)0xFF;
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return unchecked(crc ^ 0xFFFF_FFFFu);
        }

        private byte NextRandom()
        {
            randState = (randState * 1664525u) + 1013904223u;   // LCG, good enough for HCI randoms
            return unchecked((byte)(randState >> 8));
        }

        private static byte[] DefaultLocalSupFeat()
        {
            // Conservative BLE feature set: basic LE + encryption + conn-param update. Deliberately
            // omits Enhanced Privacy / Extended Data Length so the host skips those optional steps.
            return new byte[] { 0x1F, 0xFE, 0x2C, 0xFF, 0x23, 0xB7, 0x08, 0x00 };
        }

        private readonly IMachine machine;
        private State state = State.Idle;
        private int bytesLeftToServe;
        private readonly List<byte> cmdBuffer;
        private readonly Queue<byte> rxTxFifo;

        // Transaction bookkeeping for re-assembling commands that span multiple data bursts.
        private bool inTxn;
        private int txnLen;
        private readonly List<byte> txnBuf;
        private bool commandInProgress;

        private bool csLow;
        private bool radioReady;
        private bool booting = true;

        // CM-entry square-wave detection on ball 15 (counted per EN-pulsed attempt).
        private const int CmEpisodeThreshold = 40;    // edges required before declaring a wave episode
        private bool cmLastLevel;
        private int cmEdgeCount;
        private bool cmFiredThisEp;
        private bool rdyLast = false;

        private byte[] deviceAddress;
        private byte[] localSupFeat;
        private uint randState = 0x1234_5678u;
        private byte[] nvm;   // lazy NVM/flash, allocated on first firmware-update access

        // Standard-LE advertising state (drives the over-the-air ADV_Ind capture path).
        private byte[] advData;      // advertising AD structures from Set Advertising Data
        private bool advActive;      // true while Set Advertising Enable(1) is in effect
        private int advSeq;          // counts emitted advertisements (for channel hopping)
        private int advChannel = 37; // current advertising channel (hops across 37/38/39)

        // Raised with a BLESniffer-framed record each time an over-the-air PDU is emitted, so tests and external
        // consumers can observe the air frames directly instead of parsing EM9305AIR log text. Same bytes as logged.
        public event Action<byte[]> AirFrameSent;
    }
}
