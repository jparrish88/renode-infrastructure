//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.SPI;

using NUnit.Framework;

namespace Antmicro.Renode.PeripheralsTests
{
    [TestFixture]
    public class AmbiqEM9305Test
    {
        private Machine machine;
        private AmbiqEM9305 peripheral;

        [SetUp]
        public void Setup()
        {
            machine = new Machine();
            peripheral = new AmbiqEM9305(machine);
        }

        // tx_starts: host clocks [0x42][0x00]; radio answers MISO[0]=0xC0 and MISO[1]=free_space.
        [Test]
        public void TxHeaderReturnsStsReadyThenFreeSpace()
        {
            Assert.AreEqual(0xC0, peripheral.Transmit(0x42), "byte[0] of status reply must be STS ready");

            var freeSpace = peripheral.Transmit(0x00);
            Assert.Greater(freeSpace, 0, "byte[1] (free space) should be non-zero");
        }

        // A byte that is not a valid header while idle must not corrupt state.
        [Test]
        public void IdleGarbageByteLeavesStateIdle()
        {
            var r = peripheral.Transmit(0x55);   // neither 0x42 nor 0x81
            Assert.AreEqual(0x00, r);

            peripheral.FinishTransmission();     // end the stray transaction

            // A subsequent valid header must still be recognised (fresh transaction).
            Assert.AreEqual(0xC0, peripheral.Transmit(0x42));
        }

        // Full round trip: a standard HCI command is processed on the TX path and the
        // resulting Command Complete event can then be read back over the RX handshake.
        [Test]
        public void TxCommandGeneratesReadableResponse()
        {
            var opcode = 0x0045;   // LE controller OGF, not vendor specific
            byte[] cmd = { 0x01, (byte)(opcode & 0xFF), (byte)((opcode >> 8) & 0xFF), 0x00 };

            TxHandshake();
            SendCommand(cmd);

            var resp = ReadResponse();

            Assert.AreEqual(7, resp.Count, "no-data CC is 3 header + 4 params");
            Assert.AreEqual(0x04, resp[0], "event type");
            Assert.AreEqual(0x0E, resp[1], "command complete event code");
            Assert.AreEqual(0x04, resp[2], "plen = numHci+opcode+status = 4");
            Assert.AreEqual(0x01, resp[3], "num_hci_command_packets");
            Assert.AreEqual((byte)(opcode & 0xFF), resp[4], "echoed op_lo");
            Assert.AreEqual((byte)((opcode >> 8) & 0xFF), resp[5], "echoed op_hi");
            Assert.AreEqual(0x00, resp[6], "status must be success");
        }

        // Read BD Address returns the stored public address with a data payload.
        [Test]
        public void ReadBdAddressReturnsSixBytePayload()
        {
            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x09, 0x10, 0x00 });   // opcode 0x1009

            var resp = ReadResponse();

            Assert.AreEqual(13, resp.Count, "CC + status + 6 addr bytes");
            Assert.AreEqual(0x0A, resp[2], "plen = 4 + 6 data");
            Assert.AreEqual(0x09, resp[4]);
            Assert.AreEqual(0x10, resp[5]);
            Assert.AreEqual(0x00, resp[6], "status success");

            byte[] expected = { 0x02, 0x1F, 0xDE, 0xAD, 0xBE, 0xEF };   // default address
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(expected[i], resp[7 + i], "addr byte " + i);
            }
        }

        // VSC SET_DEV_PUB_ADDR stores the address which Read BD Address then echoes.
        [Test]
        public void SetDevPubAddrIsEchoedByReadBdAddress()
        {
            TxHandshake();
            byte[] addr = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
            var setCmd = new List<byte> { 0x01, 0x43, 0xFC, 0x06 };   // VSC 0xFC43, param_len=6
            setCmd.AddRange(addr);
            SendCommand(setCmd.ToArray());

            ReadResponse();   // drain the SetDevPubAddr Command Complete before issuing the next command

            TxHandshake();
            SendCommand(new byte[] { 0x01, 0x09, 0x10, 0x00 });       // Read BD Address
            var resp = ReadResponse();

            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(addr[i], resp[7 + i], "echoed addr byte " + i);
            }
        }

        // Vendor specific commands (OGF = 0x3F) take the VSC path and still echo in a Command Complete.
        [Test]
        public void VendorSpecificOpcodeIsEchoed()
        {
            ushort vscOpcode = 0xF800;   // OGF=63, OOC=0
            TxHandshake();
            SendCommand(new byte[] { 0x01, (byte)(vscOpcode & 0xFF), (byte)((vscOpcode >> 8) & 0xFF), 0x00 });

            var resp = ReadResponse();
            Assert.AreEqual((byte)((vscOpcode >> 8) & 0xFF), resp[5], "VSC echo op_hi");
        }

        // Firmware update: WRITE_AT_ADDRESS (0xFD03) stores bytes into NVM, and READ_AT_ADDRESS
        // (0xFD01) returns exactly those stored bytes.
        [Test]
        public void WriteAtAddressThenReadBackReturnsStoredBytes()
        {
            const uint addr = 0x0031_0000;
            byte[] data = { 0xDE, 0xAD, 0xBE, 0xEF };

            // WRITE: [type][op_lo=03][op_hi=FD][plen=4+len][addr u32 LE][data...]
            var writeCmd = new List<byte> { 0x01, 0x03, 0xFD, (byte)(4 + data.Length) };
            AddU32Le(writeCmd, addr);
            writeCmd.AddRange(data);

            TxHandshake();
            SendCommand(writeCmd.ToArray());
            ReadResponse();   // drain the write Command Complete

            // READ: [type][op_lo=01][op_hi=FD][plen=5][addr u32 LE][len]
            var readCmd = new List<byte> { 0x01, 0x01, 0xFD, 0x05 };
            AddU32Le(readCmd, addr);
            readCmd.Add((byte)data.Length);

            TxHandshake();
            SendCommand(readCmd.ToArray());
            var resp = ReadResponse();

            Assert.AreEqual(7 + data.Length, resp.Count, "CC (7) + stored payload");
            Assert.AreEqual(0x00, resp[6], "status success");
            for (int i = 0; i < data.Length; i++)
            {
                Assert.AreEqual(data[i], resp[7 + i], "stored byte " + i);
            }
        }

        // Firmware update: CRC_CALCULATE (0xFC4E) returns the IEEE CRC-32 of a [start,end) NVM range.
        // Verified against the standard check value CRC-32("123456789") = 0xCBF43926.
        [Test]
        public void CrcCalculateMatchesStandardVector()
        {
            const uint start = 0x0030_0000;
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");

            var writeCmd = new List<byte> { 0x01, 0x03, 0xFD, (byte)(4 + data.Length) };
            AddU32Le(writeCmd, start);
            writeCmd.AddRange(data);
            TxHandshake();
            SendCommand(writeCmd.ToArray());
            ReadResponse();   // drain

            uint end = start + (uint)data.Length;
            var crcCmd = new List<byte> { 0x01, 0x4E, 0xFC, 0x08 };   // VSC 0xFC4E, plen=8
            AddU32Le(crcCmd, start);
            AddU32Le(crcCmd, end);

            TxHandshake();
            SendCommand(crcCmd.ToArray());
            var resp = ReadResponse();

            Assert.AreEqual(11, resp.Count, "CC (7) + 4 CRC bytes");
            Assert.AreEqual(0x00, resp[6], "status success");
            // 0xCBF43926 in little-endian byte order.
            Assert.AreEqual(0x26, resp[7]);
            Assert.AreEqual(0x39, resp[8]);
            Assert.AreEqual(0xF4, resp[9]);
            Assert.AreEqual(0xCB, resp[10]);
        }

        // Firmware update: a command larger than one advertised burst (a 256-byte WRITE_AT_ADDRESS)
        // is split by the host into two data transactions (255 + 1), each preceded by its own [0x42][0x00]
        // handshake. The model must re-assemble it and store all bytes, including the one sent last.
        [Test]
        public void OversizedCommandSplitAcrossBurstsIsReassembled()
        {
            const uint addr = 0x0031_1000;
            const int dataLen = 248;   // total packet = 1+2+1+4+248 = 256 bytes

            var cmd = new List<byte> { 0x01, 0x03, 0xFD, (byte)(4 + dataLen) };
            AddU32Le(cmd, addr);
            for (int i = 0; i < dataLen; i++)
            {
                cmd.Add((byte)i);      // data[i] == i
            }

            cmd[8] = 0x5A;   // marker at data offset 0
            int totalBytes = cmd.Count;   // 256
            Assert.AreEqual(256, totalBytes, "max-size write command");

            const int firstBurst = 255;   // what a single-byte free-space field can advertise

            TxHandshake();                            // handshake for burst 1
            SendDataBurst(cmd, 0, firstBurst);        // first 255 bytes (one IOM transaction)

            TxHandshake();                            // continuation handshake (buffer must NOT be cleared)
            SendDataBurst(cmd, firstBurst, totalBytes - firstBurst);   // final byte

            ReadResponse();                           // drain the write's Command Complete before reading back

            // Read back data[0] (first burst) and data[dataLen-1] (the last byte, second burst).
            Assert.AreEqual((byte)0x5A, ReadNvmByte(addr), "data offset 0 (burst 1)");
            Assert.AreEqual(cmd[totalBytes - 1], ReadNvmByte(addr + (uint)(dataLen - 1)), "last data byte (burst 2)");
        }

        //***************************************************************************
        // Helpers that mirror the IOM transaction structure: every transfer ends with a
        // FinishTransmission() so the slave can classify each IOM transfer.
        //***************************************************************************

        private void TxHandshake()
        {
            Assert.AreEqual(0xC0, peripheral.Transmit(0x42));
            Assert.Greater(peripheral.Transmit(0x00), 0);   // free space advertised
            peripheral.FinishTransmission();                // handshake transaction complete -> arms command assembly
        }

        private void SendCommand(byte[] cmd)
        {
            for (int i = 0; i < cmd.Length; i++)
            {
                peripheral.Transmit(cmd[i]);
            }

            peripheral.FinishTransmission();   // data transaction complete -> process if self-delimiting
        }

        private void SendDataBurst(List<byte> cmd, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                peripheral.Transmit(cmd[start + i]);
            }

            peripheral.FinishTransmission();   // one IOM transaction holding `count` data bytes
        }

        private List<byte> ReadResponse()
        {
            Assert.AreEqual(0xC0, peripheral.Transmit(0x81));
            var available = peripheral.Transmit(0x00);
            Assert.Greater(available, 0, "a response should have been enqueued");
            peripheral.FinishTransmission();   // RX handshake transaction complete

            var resp = new List<byte>();
            for (int i = 0; i < available; i++)
            {
                resp.Add(peripheral.Transmit(0x00));   // MISO returns the queued event bytes
            }

            peripheral.FinishTransmission();           // read data transaction complete
            return resp;
        }

        private void AddU32Le(List<byte> list, uint value)
        {
            list.Add((byte)(value & 0xFF));
            list.Add((byte)((value >> 8) & 0xFF));
            list.Add((byte)((value >> 16) & 0xFF));
            list.Add((byte)((value >> 24) & 0xFF));
        }

        // Read a single byte back from NVM via READ_AT_ADDRESS and return it.
        private byte ReadNvmByte(uint addr)
        {
            var readCmd = new List<byte> { 0x01, 0x01, 0xFD, 0x05 };
            AddU32Le(readCmd, addr);
            readCmd.Add(0x01);

            TxHandshake();
            SendCommand(readCmd.ToArray());
            var resp = ReadResponse();

            return resp[7];   // first payload byte of the Command Complete
        }
    }
}
