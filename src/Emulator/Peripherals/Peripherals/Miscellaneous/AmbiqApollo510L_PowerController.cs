//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite power controller (PWRCTRL @ 0x4000C000 on Lite).
// NEW file for the Lite family — does not modify AmbiqApollo510_PowerController.
//
// Authoritative sources:
//   SDK pack/SVD/apollo510L.svd  (register offsets, bit positions, reset values)
//   mcu/apollo510L/hal/am_hal_pwrctrl.c (status-poll + OTP-before-CRYPTO rules)
//
// Lite deltas vs Apollo510 (all from the SVD):
//   DEVPWREN/STATUS @0x04/0x08, reset 0x08200000 (OTP+CRYPTO on).
//   Only UART0-1 (bits 9-10), IOM0-5 (bits 1-6), MSPI0-2 (bits 14-16).
//   New bits: I3CPHY(7), I3C(30), NETAOL(31). No UART2-3 / IOM6-7 / MSPI3.
//   Power domains (HAL masks): HCPB=IOM0-3, HCPC=IOM4-5+I3C, HCPA=UART0-1 —
//     status mirrors the OR of the domain's enable bits (hardware powers the
//     domain as a unit), same technique as the 510 model.
//   PWRSTPLL(17) has no enable counterpart; reads reset value 0 (faithful to
//     SVD reset 0x08200000). Tie to SYSPLL/CRM state in Phase C clock work.
//   MEMPWREN @0x14: TCM[1:0], NVM[3], ROM[5]; STATUS adds PWRSTCACHE[4] with no
//     enable bit — reads constant true (reset value) pending cache-power work.
//   AUDSS @0x0C/0x10: PDM0(2), I2S0(6) only. No AUDADC on Lite.
//   New: CM4PWRCTRL @0x98 (CM4 radio power request), CM4PWRSTATE @0x9C,
//     MRAMEXTCTRL @0x1C8, I3CISOCTRL @0x1D0. No DSP0/DSP1 or LP/HP weight tables.
//   VRSTATUS @0x108 reset 0x0 — reported HONESTLY (no fake SIMOBUCK ACT).
//     If an HP-mode firmware polls for ACT, that is a real modeling gap to fix
//     with the trimver/SpotMgr sequence, not a silent constant.
//
using System.Linq;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510L_PowerController : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_PowerController(IMachine machine) : base(machine)
        {
            DefineRegisters();
        }

        public long Size => 0x250;

        // CM4 radio-subsystem boot output: asserted while CM4POWERONREQ is set.
        // Wired in the repl to the CM55IPC CM4-alive input so the emulated CM4
        // raises the IPCINIT handshake (am_hal_pwrctrl_rss_bootup waits on it).
        public GPIO CM4Boot { get; } = new GPIO();

        public override void Reset()
        {
            base.Reset();
            CM4Boot.Unset();
        }

        private void DefineRegisters()
        {
            Registers.MCUPerformanceControl.Define(this, 0x2000002D)
                .WithValueField(0, 2, out mcuPerfFreq, name: "MCUPERFREQ", writeCallback: (_, v) => { if(mcuPerfStatus != null) mcuPerfStatus.Value = v; if(mcuPerfAck != null) mcuPerfAck.Value = true; })
                .WithFlag(2, out mcuPerfAck, name: "MCUPERFACK", valueProviderCallback: _ => true)
                .WithValueField(3, 2, out mcuPerfStatus, name: "MCUPERFSTATUS", valueProviderCallback: _ => mcuPerfFreq != null ? mcuPerfFreq.Value : 1)
                .WithFlag(5, out _, name: "WAITFORHP2")
                .WithReservedBits(6, 23)
                .WithFlag(29, out mcuPowerEnable, name: "PWRENMCUE")
                .WithValueField(30, 2, out _, name: "PWRSTMCUE")
                ;

            Registers.DevicePowerEnable.Define(this, 0x08200000)
                .WithReservedBits(0, 1)
                .WithFlags(1, 4, out powerEnableFlagsIOM0_3, name: "PWRENIOMx")
                .WithFlags(5, 2, out powerEnableFlagsIOM4_5, name: "PWRENIOMx")
                .WithFlag(7, out powerEnableFlagI3CPhy, name: "PWRENI3CPHY")
                .WithReservedBits(8, 1)
                .WithFlags(9, 2, out powerEnableFlagsUart0_1, name: "PWRENUARTx")
                .WithReservedBits(11, 2)
                .WithFlag(13, out powerEnableFlagADC, name: "PWRENADC")
                .WithFlag(14, out powerEnableFlagMSPI0, name: "PWRENMSPI0")
                .WithFlag(15, out powerEnableFlagMSPI1, name: "PWRENMSPI1")
                .WithFlag(16, out powerEnableFlagMSPI2, name: "PWRENMSPI2")
                .WithReservedBits(17, 1)
                .WithFlag(18, out powerEnableFlagGfx, name: "PWRENGFX")
                .WithFlag(19, out powerEnableFlagDisp, name: "PWRENDISP")
                .WithFlag(20, out powerEnableFlagDispPhy, name: "PWRENDISPPHY")
                .WithFlag(21, out powerEnableFlagCrypto, name: "PWRENCRYPTO")
                .WithFlag(22, out powerEnableFlagSDIO0, name: "PWRENSDIO0")
                .WithFlag(23, out powerEnableFlagSDIO1, name: "PWRENSDIO1")
                .WithFlag(24, out powerEnableFlagUSB, name: "PWRENUSB")
                .WithFlag(25, out powerEnableFlagUSBPhy, name: "PWRENUSBPHY")
                .WithFlag(26, out powerEnableFlagDbg, name: "PWRENDBG")
                .WithFlag(27, out powerEnableFlagOtp, name: "PWRENOTP")
                .WithFlag(28, out powerEnableFlagIOSFD0, name: "PWRENIOSFD0")
                .WithFlag(29, out powerEnableFlagIOSFD1, name: "PWRENIOSFD1")
                .WithFlag(30, out powerEnableFlagI3C, name: "PWRENI3C")
                .WithFlag(31, out powerEnableFlagNetaol, name: "PWRENNETAOL")
                ;

            Registers.DevicePowerStatus.Define(this, 0x08200000)
                .WithReservedBits(0, 1)
                .WithFlags(1, 4, FieldMode.Read, name: "PWRSTIOMx", valueProviderCallback: (_, __) => PowerStatusIOM0_3)
                .WithFlags(5, 2, FieldMode.Read, name: "PWRSTIOMx", valueProviderCallback: (_, __) => PowerStatusIOM4_5_I3C)
                .WithFlag(7, FieldMode.Read, name: "PWRSTI3CPHY", valueProviderCallback: _ => powerEnableFlagI3CPhy.Value)
                .WithReservedBits(8, 1)
                .WithFlags(9, 2, FieldMode.Read, name: "PWRSTUARTx", valueProviderCallback: (_, __) => PowerStatusUart0_1)
                .WithReservedBits(11, 2)
                .WithFlag(13, FieldMode.Read, name: "PWRSTADC", valueProviderCallback: _ => powerEnableFlagADC.Value)
                .WithFlag(14, FieldMode.Read, name: "PWRSTMSPI0", valueProviderCallback: _ => powerEnableFlagMSPI0.Value)
                .WithFlag(15, FieldMode.Read, name: "PWRSTMSPI1", valueProviderCallback: _ => powerEnableFlagMSPI1.Value)
                .WithFlag(16, FieldMode.Read, name: "PWRSTMSPI2", valueProviderCallback: _ => powerEnableFlagMSPI2.Value)
                .WithFlag(17, out _, name: "PWRSTPLL")
                .WithFlag(18, FieldMode.Read, name: "PWRSTGFX", valueProviderCallback: _ => powerEnableFlagGfx.Value)
                .WithFlag(19, FieldMode.Read, name: "PWRSTDISP", valueProviderCallback: _ => powerEnableFlagDisp.Value)
                .WithFlag(20, FieldMode.Read, name: "PWRSTDISPPHY", valueProviderCallback: _ => powerEnableFlagDispPhy.Value)
                .WithFlag(21, FieldMode.Read, name: "PWRSTCRYPTO", valueProviderCallback: _ => powerEnableFlagCrypto.Value)
                .WithFlag(22, FieldMode.Read, name: "PWRSTSDIO0", valueProviderCallback: _ => powerEnableFlagSDIO0.Value)
                .WithFlag(23, FieldMode.Read, name: "PWRSTSDIO1", valueProviderCallback: _ => powerEnableFlagSDIO1.Value)
                .WithFlag(24, FieldMode.Read, name: "PWRSTUSB", valueProviderCallback: _ => powerEnableFlagUSB.Value)
                .WithFlag(25, FieldMode.Read, name: "PWRSTUSBPHY", valueProviderCallback: _ => powerEnableFlagUSBPhy.Value)
                .WithFlag(26, FieldMode.Read, name: "PWRSTDBG", valueProviderCallback: _ => powerEnableFlagDbg.Value)
                .WithFlag(27, FieldMode.Read, name: "PWRSTOTP", valueProviderCallback: _ => powerEnableFlagOtp.Value)
                .WithFlag(28, FieldMode.Read, name: "PWRSTIOSFD0", valueProviderCallback: _ => powerEnableFlagIOSFD0.Value)
                .WithFlag(29, FieldMode.Read, name: "PWRSTIOSFD1", valueProviderCallback: _ => powerEnableFlagIOSFD1.Value)
                .WithFlag(30, FieldMode.Read, name: "PWRSTI3C", valueProviderCallback: _ => PowerStatusIOM4_5_I3C)
                .WithFlag(31, FieldMode.Read, name: "PWRSTNETAOL", valueProviderCallback: _ => powerEnableFlagNetaol.Value)
                ;

            Registers.AudioSubsystemPowerEnable.Define(this)
                .WithReservedBits(0, 2)
                .WithFlag(2, out powerEnableFlagPDM0, name: "PWRENPDM0")
                .WithReservedBits(3, 3)
                .WithFlag(6, out powerEnableFlagI2S0, name: "PWRENI2S0")
                .WithReservedBits(7, 25)
                ;

            Registers.AudioSubsystemPowerStatus.Define(this)
                .WithReservedBits(0, 2)
                .WithFlag(2, FieldMode.Read, name: "PWRSTPDM0", valueProviderCallback: _ => powerEnableFlagPDM0.Value)
                .WithReservedBits(3, 3)
                .WithFlag(6, FieldMode.Read, name: "PWRSTI2S0", valueProviderCallback: _ => powerEnableFlagI2S0.Value)
                .WithReservedBits(7, 25)
                ;

            Registers.MemoryPowerEnable.Define(this, 0x0000002B)
                .WithValueField(0, 2, out powerEnableFlagTCM, name: "PWRENTCM")
                .WithReservedBits(2, 1)
                .WithFlag(3, out powerEnableFlagNVM, name: "PWRENNVM")
                .WithReservedBits(4, 1)
                .WithFlag(5, out powerEnableFlagROM, name: "PWRENROM")
                .WithReservedBits(6, 26)
                ;

            Registers.MemoryPowerStatus.Define(this, 0x0000009B)
                .WithValueField(0, 2, name: "PWRSTTCM", valueProviderCallback: _ => powerEnableFlagTCM.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, FieldMode.Read, name: "PWRSTNVM0", valueProviderCallback: _ => powerEnableFlagNVM.Value)
                .WithFlag(4, FieldMode.Read, name: "PWRSTCACHE", valueProviderCallback: _ => true)
                .WithReservedBits(5, 2)
                .WithFlag(7, FieldMode.Read, name: "PWRSTROM", valueProviderCallback: _ => powerEnableFlagROM.Value)
                .WithReservedBits(8, 24)
                ;

            Registers.MemoryRetConfiguration.Define(this, 0x00000002)
                .WithFlag(0, out _, name: "TCMPWDSLP")
                .WithFlag(1, out _, name: "NVMPWDSLP")
                .WithValueField(2, 2, out _, name: "NVMPWDCFG")
                .WithReservedBits(4, 28)
                ;

            Registers.SystemPowerStatus.Define(this, 0x00000003)
                .WithFlags(0, 2, out _, name: "PWRSTMCU")
                .WithReservedBits(2, 26)
                .WithFlag(28, out _, name: "SYSDEEPERSLEEP")
                .WithFlag(29, out _, name: "CORESLEEP")
                .WithFlag(30, out _, name: "COREDEEPSLEEP")
                .WithFlag(31, out _, name: "SYSDEEPSLEEP")
                ;

            Registers.SharedSRAMPowerEnable.Define(this)
                .WithValueField(0, 2, out powerEnableFlagSSRAM, name: "PWRENSSRAM")
                .WithReservedBits(2, 30)
                ;

            Registers.SharedSRAMPowerStatus.Define(this, 0x00000003)
                .WithValueField(0, 2, name: "SSRAMPWRST", valueProviderCallback: _ => powerEnableFlagSSRAM.Value)
                .WithReservedBits(2, 30)
                ;

            Registers.SharedSRAMRetConfiguration.Define(this, 0x000000FC)
                .WithValueField(0, 2, out _, name: "SSRAMPWDSLP")
                .WithValueField(2, 2, out _, name: "SSRAMACTMCU")
                .WithValueField(4, 2, out _, name: "SSRAMACTGFX")
                .WithValueField(6, 2, out _, name: "SSRAMACTDISP")
                .WithReservedBits(8, 24)
                ;

            Registers.DevicePowerEventEnable.Define(this)
                .WithFlags(0, 6, out _, name: "DEVEVEN")
                .WithReservedBits(6, 1)
                .WithFlags(7, 4, out _, name: "DEVEVEN")
                .WithReservedBits(11, 1)
                .WithFlag(12, out _, name: "PDMEVEN")
                .WithReservedBits(13, 19)
                ;

            Registers.MemoryPowerEventEnable.Define(this)
                .WithValueField(0, 3, out _, name: "DTCMEN")
                .WithFlag(3, out _, name: "NVM0EN")
                .WithReservedBits(4, 28)
                ;

            Registers.MultimediaSystemOverride.Define(this, 0x000000EE)
                .WithFlag(0, out _, name: "MMSOVRMCULGFX")
                .WithFlag(1, out _, name: "MMSOVRSSRAMGFX")
                .WithValueField(2, 2, out _, name: "MMSOVRSSRAMRETGFX")
                .WithFlag(4, out _, name: "MMSOVRMCULDISP")
                .WithFlag(5, out _, name: "MMSOVRSSRAMDISP")
                .WithValueField(6, 2, out _, name: "MMSOVRSSRAMRETDISP")
                .WithReservedBits(8, 24)
                ;

            // CPUPWRCTRL layout per SVD (bit 8 and [31:14] reserved).
            Registers.CPUPerformanceControl.Define(this)
                .WithReservedBits(0, 2)
                .WithFlag(2, out _, name: "SLEEPMODE")
                .WithFlag(3, out _, name: "EPUMODEOVREN")
                .WithValueField(4, 2, out _, name: "EPUPWRSTATE")
                .WithFlag(6, out _, name: "CPUCLKOVREN")
                .WithFlag(7, out _, name: "IWICCLKSTATE")
                .WithReservedBits(8, 1)
                .WithFlag(9, out _, name: "PMUCLKSTATE")
                .WithFlag(10, out _, name: "FCLKSTATE")
                .WithFlag(11, out _, name: "DBGCLKSTATE")
                .WithFlag(12, out _, name: "DISFUNCRETMODE")
                .WithFlag(13, out _, name: "DEEPERSLEEPEN")
                .WithReservedBits(14, 18)
                ;

            // CPUPWRSTATUS is written by the HAL during power transitions, so
            // every SVD bit is a real stored field (reset 0x00800006).
            Registers.CPUPerformanceStatus.Define(this, 0x00800006)
                .WithFlags(0, 5, out cpuStatusLow, name: "CPUSTATUS")
                .WithReservedBits(5, 14)
                .WithFlags(19, 12, out cpuStatusCache, name: "CPUCACHESTATUS")
                .WithReservedBits(31, 1)
                ;

            Registers.PowerAckOverride.Define(this)
                .WithFlags(0, 27, out _, name: "PWRACKOVERRIDE")
                .WithReservedBits(27, 5)
                ;

            Registers.PowerCountDefault.Define(this, 0x00000208)
                .WithValueField(0, 6, out _, name: "PWRDEFVALDEVSTMC")
                .WithValueField(6, 10, out _, name: "PWRACKWAITDELSIMOSTMC")
                .WithReservedBits(16, 16)
                ;

            Registers.EPUDomainRetConfig.Define(this, 0x00000001)
                .WithFlag(0, out _, name: "EPURETSLP")
                .WithReservedBits(1, 31)
                ;

            // CM4 radio subsystem power. Setting CM4POWERONREQ reports CM4PWRSTATUS
            // ON and asserts CM4Boot (the emulated CM4 is alive); clearing the
            // request reports OFF and deasserts CM4Boot. This satisfies the
            // am_hal_pwrctrl_cm4_wakeup_req poll (CM4PWRSTATUS != OFF) and drives
            // the IPCINIT handshake into CM55IPC.
            Registers.CM4PowerControl.Define(this)
                .WithFlag(0, out cm4PowerOnRequest, name: "CM4POWERONREQ",
                    writeCallback: (_, v) => { cm4PowerStatus.Value = v ? 1u : 0u; if(v) { CM4Boot.Set(); } else { CM4Boot.Unset(); } this.Log(LogLevel.Info, "PWRCTRL: CM4 {0} (PWRSTATUS={1})", v ? "wakeup request" : "power off", cm4PowerStatus.Value); })
                .WithReservedBits(1, 31)
                ;

            Registers.CM4PowerState.Define(this)
                .WithValueField(0, 2, out cm4PowerStatus, name: "CM4PWRSTATUS")
                .WithReservedBits(2, 30)
                ;

            Registers.VoltageRegulatorsControl.Define(this)
                .WithFlag(0, out _, name: "SIMOBUCKEN")
                .WithReservedBits(1, 31)
                ;

            // LEGACYVRLPOVR layout per SVD (bit 6 and [31:24] reserved).
            // Firmware writes IGNORENETAOL(21) during power init.
            Registers.VoltageRegulatorsLegacyLowPowerOverrides.Define(this)
                .WithFlags(0, 6, out legacyLpOvrLow, name: "IGNORE")
                .WithReservedBits(6, 1)
                .WithFlags(7, 17, out legacyLpOvrHigh, name: "IGNORE")
                .WithReservedBits(24, 8)
                ;

            Registers.VoltageRegulatorsStatus.Define(this)
                .WithValueField(0, 2, out _, name: "CORELDOST")
                .WithValueField(2, 2, out _, name: "MEMLDOST")
                .WithValueField(4, 2, out _, name: "SIMOBUCKST")
                .WithReservedBits(6, 26)
                ;

            Registers.SRAMControl.Define(this)
                .WithReservedBits(0, 1)
                .WithFlag(1, out _, name: "SRAMCLKGATE")
                .WithFlag(2, out _, name: "SRAMMASTERCLKGATE")
                .WithReservedBits(3, 5)
                .WithValueField(8, 24, out _, name: "SRAMLIGHTSLEEP")
                ;

            Registers.ADCStatus.Define(this, 0x0000003F)
                .WithFlags(0, 6, out _, name: "ADCPWD")
                .WithReservedBits(6, 26)
                ;

            Registers.MRAMExtendedControl.Define(this, 0x00000002)
                .WithFlag(0, out _, name: "MRAMTMCCLKGATDIS")
                .WithFlag(1, out _, name: "OTPAUTOWAKEUPCTRL")
                .WithFlag(2, out _, name: "MRAMAUTOWAKEUPCTRL")
                .WithFlag(3, out _, name: "MRAMLPRDYNAMIC")
                .WithReservedBits(4, 28)
                ;

            Registers.I3CIsolationControl.Define(this)
                .WithFlag(0, out _, name: "I3CISOSEL")
                .WithFlag(1, out _, name: "I3CISOEN")
                .WithReservedBits(2, 30)
                ;

            Registers.EnergyMonitorControl.Define(this, 0x000000FF)
                .WithValueField(0, 8, out _, name: "FREEZE")
                .WithValueField(8, 8, out _, name: "CLEAR")
                .WithReservedBits(16, 16)
                ;

            Registers.EnergyMonitorConfig0.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG0")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig1.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG1")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig2.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG2")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig3.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG3")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig4.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG4")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig5.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG5")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig6.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG6")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorConfig7.Define(this)
                .WithValueField(0, 8, out _, name: "EMONCFG7")
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorCount0.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT0")
                ;

            Registers.EnergyMonitorCount1.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT1")
                ;

            Registers.EnergyMonitorCount2.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT2")
                ;

            Registers.EnergyMonitorCount3.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT3")
                ;

            Registers.EnergyMonitorCount4.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT4")
                ;

            Registers.EnergyMonitorCount5.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT5")
                ;

            Registers.EnergyMonitorCount6.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT6")
                ;

            Registers.EnergyMonitorCount7.Define(this)
                .WithValueField(0, 32, out _, name: "EMONCOUNT7")
                ;

            Registers.EnergyMonitorStatus.Define(this)
                .WithFlag(0, out _, name: "EMONOVERFLOW0")
                .WithFlag(1, out _, name: "EMONOVERFLOW1")
                .WithFlag(2, out _, name: "EMONOVERFLOW2")
                .WithFlag(3, out _, name: "EMONOVERFLOW3")
                .WithFlag(4, out _, name: "EMONOVERFLOW4")
                .WithFlag(5, out _, name: "EMONOVERFLOW5")
                .WithFlag(6, out _, name: "EMONOVERFLOW6")
                .WithFlag(7, out _, name: "EMONOVERFLOW7")
                .WithReservedBits(8, 24)
                ;
        }

        // Power domains shared across modules (see HAL HCPx masks):
        // enabling any member powers the domain, and every member's status
        // bit reflects the domain state.
        private bool PowerStatusIOM0_3 => powerEnableFlagsIOM0_3.Any(flag => flag.Value);

        private bool PowerStatusIOM4_5_I3C => powerEnableFlagsIOM4_5.Any(flag => flag.Value) || powerEnableFlagI3C.Value;

        private bool PowerStatusUart0_1 => powerEnableFlagsUart0_1.Any(flag => flag.Value);

        private IFlagRegisterField powerEnableFlagADC;
        private IFlagRegisterField powerEnableFlagCrypto;
        private IFlagRegisterField powerEnableFlagOtp;
        private IFlagRegisterField powerEnableFlagMSPI0;
        private IFlagRegisterField powerEnableFlagMSPI1;
        private IFlagRegisterField powerEnableFlagMSPI2;
        private IFlagRegisterField powerEnableFlagGfx;
        private IFlagRegisterField powerEnableFlagDisp;
        private IFlagRegisterField powerEnableFlagDispPhy;
        private IFlagRegisterField powerEnableFlagSDIO0;
        private IFlagRegisterField powerEnableFlagSDIO1;
        private IFlagRegisterField powerEnableFlagUSB;
        private IFlagRegisterField powerEnableFlagUSBPhy;
        private IFlagRegisterField powerEnableFlagDbg;
        private IFlagRegisterField powerEnableFlagIOSFD0;
        private IFlagRegisterField powerEnableFlagIOSFD1;
        private IFlagRegisterField powerEnableFlagI3C;
        private IFlagRegisterField powerEnableFlagI3CPhy;
        private IFlagRegisterField powerEnableFlagNetaol;
        private IFlagRegisterField powerEnableFlagPDM0;
        private IFlagRegisterField powerEnableFlagI2S0;
        private IFlagRegisterField powerEnableFlagNVM;
        private IFlagRegisterField powerEnableFlagROM;
        private IFlagRegisterField cm4PowerOnRequest;
        private IValueRegisterField cm4PowerStatus;
        private IFlagRegisterField mcuPowerEnable;
        private IValueRegisterField powerEnableFlagTCM;
        private IValueRegisterField powerEnableFlagSSRAM;
        private IFlagRegisterField[] powerEnableFlagsIOM0_3;
        private IFlagRegisterField[] powerEnableFlagsIOM4_5;
        private IFlagRegisterField[] powerEnableFlagsUart0_1;
        private IFlagRegisterField[] cpuStatusLow;
        private IFlagRegisterField[] cpuStatusCache;
        private IFlagRegisterField[] legacyLpOvrLow;
        private IFlagRegisterField[] legacyLpOvrHigh;
        private IValueRegisterField mcuPerfFreq;
        private IFlagRegisterField mcuPerfAck;
        private IValueRegisterField mcuPerfStatus;

        private enum Registers : long
        {
            MCUPerformanceControl = 0x0,
            DevicePowerEnable = 0x4,
            DevicePowerStatus = 0x8,
            AudioSubsystemPowerEnable = 0xC,
            AudioSubsystemPowerStatus = 0x10,
            MemoryPowerEnable = 0x14,
            MemoryPowerStatus = 0x18,
            MemoryRetConfiguration = 0x1C,
            SystemPowerStatus = 0x20,
            SharedSRAMPowerEnable = 0x24,
            SharedSRAMPowerStatus = 0x28,
            SharedSRAMRetConfiguration = 0x2C,
            DevicePowerEventEnable = 0x30,
            MemoryPowerEventEnable = 0x34,
            MultimediaSystemOverride = 0x40,
            CPUPerformanceControl = 0x50,
            CPUPerformanceStatus = 0x58,
            PowerAckOverride = 0x84,
            PowerCountDefault = 0x88,
            EPUDomainRetConfig = 0x94,
            CM4PowerControl = 0x98,
            CM4PowerState = 0x9C,
            VoltageRegulatorsControl = 0x100,
            VoltageRegulatorsLegacyLowPowerOverrides = 0x104,
            VoltageRegulatorsStatus = 0x108,
            SRAMControl = 0x190,
            ADCStatus = 0x194,
            MRAMExtendedControl = 0x1C8,
            I3CIsolationControl = 0x1D0,
            EnergyMonitorControl = 0x200,
            EnergyMonitorConfig0 = 0x204,
            EnergyMonitorConfig1 = 0x208,
            EnergyMonitorConfig2 = 0x20C,
            EnergyMonitorConfig3 = 0x210,
            EnergyMonitorConfig4 = 0x214,
            EnergyMonitorConfig5 = 0x218,
            EnergyMonitorConfig6 = 0x21C,
            EnergyMonitorConfig7 = 0x220,
            EnergyMonitorCount0 = 0x228,
            EnergyMonitorCount1 = 0x22C,
            EnergyMonitorCount2 = 0x230,
            EnergyMonitorCount3 = 0x234,
            EnergyMonitorCount4 = 0x238,
            EnergyMonitorCount5 = 0x23C,
            EnergyMonitorCount6 = 0x240,
            EnergyMonitorCount7 = 0x244,
            EnergyMonitorStatus = 0x24C,
        }
    }
}
