//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;
using System.Linq;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Wireless
{
    /// <summary>
    /// A sysbus-triggered, self-contained two-node EM9305 pairing demo. Writing a non-zero value to offset 0x0 runs the
    /// full live sequence once: connect over air (directed advertising + CONNECT_IND), BLE Secure Connections LKDH
    /// pairing using each side's own P-256 ephemeral key (exactly what LE_Read_Local_P256_Pub_Key, opcode 0x2025, reports),
    /// the START_ENC handshake with the auto-derived LTK, and an AES-128-CTR encrypted Data-PDU round-trip. Every over-air
    /// frame is logged to the console as it happens so the whole exchange can be watched live.
    ///
    /// The two radios run on a private machine added to the current emulation (never clearing it), with SynchronizedTimers
    /// enabled only for the duration of the synchronous cascade, then restored -- so this never disturbs a running board sim.
    /// </summary>
    public class Em9305PairHarness : BasicDoubleWordPeripheral, IKnownSize
    {
        private Machine iso;          // isolated machine hosting the two radios (created on first trigger)

        public long Size => 0x100;

        private bool started = false;

        public Em9305PairHarness(IMachine machine) : base(machine) { }

        public override void WriteDoubleWord(long offset, uint value)
        {
            if(offset == 0x0 && value != 0 && !started)
            {
                started = true;
                Run();
                return;
            }
            base.WriteDoubleWord(offset, value);
        }

        private static string Hex(byte[] b) => Convert.ToHexString(b ?? Array.Empty<byte>()).ToLowerInvariant();
        private static bool SeqEq(byte[] a, byte[] b) => Enumerable.SequenceEqual(a ?? Array.Empty<byte>(), b ?? Array.Empty<byte>());
        private void Say(string m) => Logger.Log(LogLevel.Info, "PAIR2NODE: {0}", m);

        private void Run()
        {
            var emu = EmulationManager.Instance.CurrentEmulation;
            if(emu == null)
            {
                Logger.Log(LogLevel.Error, "PAIR2NODE: no current emulation to run in");
                return;
            }

            var prevMode = emu.Mode;
            try
            {
                iso = new Machine();
                emu.AddMachine(iso);
                emu.Mode = Emulation.EmulationMode.SynchronizedTimers;   // required for the synchronous over-air cascade

                var radioA = new AmbiqEM9305Radio(iso);
                var radioB = new AmbiqEM9305Radio(iso);
                iso.SystemBus.Register(radioA, new BusRangeRegistration(0x4100_0000UL, 0x40));
                iso.SystemBus.Register(radioB, new BusRangeRegistration(0x4100_0040UL, 0x40));

                var medium = new BLEMedium();
                medium.AttachTo(radioA);   // A = advertiser / slave
                medium.AttachTo(radioB);   // B = initiator (drives pairing + START_ENC)
                radioA.Channel = 37;       // both must share the channel for delivery
                radioB.Channel = 37;

                // Stream every over-air frame to the console as it is transmitted.
                radioA.FrameSent += (_, f) => Say("AIR   A->  " + Hex(f));
                radioB.FrameSent += (_, f) => Say("AIR   B->  " + Hex(f));

                // Two real EM9305 SPI controllers, each lazily generating its own P-256 ephemeral key pair -- exactly the
                // material a host reads back via LE_Read_Local_P256_Pub_Key (0x2025).
                var spiA = new AmbiqEM9305(iso);
                var spiB = new AmbiqEM9305(iso);
                var (privA, pubAX, pubAY) = spiA.GetLocalP256Ephemeral();   // 0x2025 answer for controller A
                var (privB, pubBX, pubBY) = spiB.GetLocalP256Ephemeral();   // 0x2025 answer for controller B

                byte[] aAddr = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB };
                byte[] bAddr = { 0xCD, 0xEF, 0x02, 0x24, 0x46, 0x68 };

                var llA = new Em9305LinkLayer(radioA) { OwnAddress = aAddr, PeerAddress = bAddr };
                var llB = new Em9305LinkLayer(radioB) { OwnAddress = bAddr, PeerAddress = aAddr };

                Say("1/4  connect: directed advertising + CONNECT_IND over air ...");
                llA.StartDirectedAdvertising();
                if(!(llA.Connected && llB.Connected))
                {
                    Say("FAIL: connect (A=" + llA.Connected + " B=" + llB.Connected + ")");
                    return;
                }
                Say("     CONNECTED on both sides over the BLE medium");

                byte[] rndA = { 0xA0, 0xB1, 0xC2, 0xD3, 0xE4, 0xF5, 0x06, 0x17, 0x28, 0x39, 0x4A, 0x5B, 0x6C, 0x7D, 0x8E, 0x9F };
                byte[] rndB = { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0, 0x00 };

                Say("2/4  secure connections: LKDH g1 ECDH + f5 (each side uses its own 0x2025 key) ...");
                llB.StartSecureConnections(true, privB, rndB);    // initiator; transmits PairPub_B -> A stores peer
                llA.StartSecureConnections(false, privA, rndA);   // responder; transmits PairPub_A -> completes both

                if(!(llA.PairingComplete && llB.PairingComplete))
                {
                    Say("FAIL: pairing (A=" + llA.PairingComplete + " B=" + llB.PairingComplete + ")");
                    return;
                }
                var dhOk  = SeqEq(llA.PairedDhKey, llB.PairedDhKey);
                var ltkOk = SeqEq(llA.PairedLtk, llB.PairedLtk);
                Say("     PAIRING COMPLETE");
                Say($"     DHKEY match={dhOk}  dhkey={Hex(llA.PairedDhKey)}");
                Say($"     LTK   match={ltkOk}  ltk={Hex(llA.PairedLtk)}");
                Say($"     MACKEY A==B={SeqEq(llA.PairedMacKey, llB.PairedMacKey)}  mackey={Hex(llA.PairedMacKey)}");

                // Independent ECDH oracle built from the two SPI controllers' public points must equal the paired DH key.
                var oracle = BleScCrypto.G1(privA, pubBX, pubBY);
                Say($"     G1(privA,pubB) == PairedDhKey: {SeqEq(oracle, llA.PairedDhKey)}");

                Say("3/4  START_ENC handshake with the auto-derived LTK ...");
                byte[] got = null;
                llA.DataReceived += (_, d) => { got = (byte[])d.Clone(); };
                llB.InitiateStartEnc(0x123456789ABCDEF0UL);       // cascades synchronously -> both sides become encrypted
                if(!(llA.Encrypted && llB.Encrypted))
                {
                    Say("FAIL: encryption not enabled (A=" + llA.Encrypted + " B=" + llB.Encrypted + ")");
                    return;
                }
                Say("     ENCRYPTED on both sides (AES-128 CTR)");

                byte[] plain = System.Text.Encoding.ASCII.GetBytes("A510-LIVE-TWO-NODE-PAYLOAD");
                bool sent = llB.SendEncryptedData(plain);         // encrypts -> transmits over air -> A decrypts
                bool roundtrip = got != null && SeqEq(got, plain);

                Say($"4/4  encrypted Data PDU: tx-ok={sent}  decrypted-on-A match={roundtrip}");
                if(got != null)
                    Say("     plaintext received on A: \"" + System.Text.Encoding.ASCII.GetString(got) + "\"");

                Say(roundtrip ? "SUCCESS: two EM9305 nodes paired over air and exchanged encrypted data"
                              : "FAILURE: pairing ran but the encrypted round-trip did not match");
            }
            catch(Exception ex)
            {
                Logger.Log(LogLevel.Error, "PAIR2NODE exception: {0}", ex.Message);
            }
            finally
            {
                emu.Mode = prevMode;   // restore so a live board sim keeps its original timer mode
            }
        }
    }
}
