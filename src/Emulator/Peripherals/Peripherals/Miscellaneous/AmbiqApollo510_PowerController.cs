//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Derived from Ambiq Apollo4 power controller model for the Apollo510 register layout.
//
using System.Linq;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510_PowerController : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510_PowerController(IMachine machine) : base(machine)
        {
            DefineRegisters();
        }

        public long Size => 0x250;

        private void DefineRegisters()
        {
            // FIX for HP mode: FREQ write updates STATUS and ACK; VRSTATUS SIMOBUCK ACT below
            Registers.MCUPerformanceControl.Define(this, 0x00000009)
                .WithValueField(0, 2, out mcuPerfFreq, name: "MCUPERFREQ", writeCallback: (_, v) => { if(mcuPerfStatus != null) mcuPerfStatus.Value = v; if(mcuPerfAck != null) mcuPerfAck.Value = true; })
                .WithFlag(2, out mcuPerfAck, name: "MCUPERFACK", valueProviderCallback: _ => true)
                .WithValueField(3, 2, out mcuPerfStatus, name: "MCUPERFSTATUS", valueProviderCallback: _ => mcuPerfFreq != null ? mcuPerfFreq.Value : 1)
                .WithReservedBits(5, 27)
                ;

            Registers.DevicePowerEnable.Define(this, 0x00200000)
                .WithTaggedFlag("PWRENIOS", 0)
                .WithFlags(1, 4, out powerEnableFlagsIOM0_3, name: "PWRENIOMx")
                .WithFlags(5, 4, out powerEnableFlagsIOM4_7, name: "PWRENIOMx")
                .WithFlags(9, 4, out powerEnableFlagsUart0_3, name: "PWRENUARTx")
                .WithFlag(13, out powerEnableFlagADC, name: "PWRENADC")
                .WithFlag(14, out powerEnableFlagMSPI0, name: "PWRENMSPI0")
                .WithTaggedFlag("PWRENMSPI1", 15)
                .WithTaggedFlag("PWRENMSPI2", 16)
                .WithTaggedFlag("PWRENMSPI3", 17)
                .WithFlag(18, out powerEnableFlagGfx, name: "PWRENGFX")
                .WithFlag(19, out powerEnableFlagDisp, name: "PWRENDISP")
                .WithFlag(20, out powerEnableFlagDispPhy, name: "PWRENDISPPHY")
                .WithFlag(21, out powerEnableFlagCrypto, name: "PWRENCRYPTO")
                .WithTaggedFlag("PWRENSDIO0", 22)
                .WithTaggedFlag("PWRENSDIO1", 23)
                .WithTaggedFlag("PWRENUSB", 24)
                .WithTaggedFlag("PWRENUSBPHY", 25)
                .WithTaggedFlag("PWRENDBG", 26)
                .WithFlag(27, out powerEnableFlagOtp, name: "PWRENOTP")
                .WithTaggedFlag("PWRENIOSFD0", 28)
                .WithTaggedFlag("PWRENIOSFD1", 29)
                .WithReservedBits(30, 2)
                ;

            Registers.DevicePowerStatus.Define(this)
                .WithTaggedFlag("PWRSTIOS", 0)
                .WithFlags(1, 4, FieldMode.Read, name: "PWRSTIOMx", valueProviderCallback: (_, __) => PowerStatusIOM0_3)
                .WithFlags(5, 4, FieldMode.Read, name: "PWRSTIOMx", valueProviderCallback: (_, __) => PowerStatusIOM4_7)
                .WithFlags(9, 4, FieldMode.Read, name: "PWRSTUARTx", valueProviderCallback: (_, __) => PowerStatusUart0_3)
                .WithFlag(13, FieldMode.Read, name: "PWRSTADC", valueProviderCallback: _ => powerEnableFlagADC.Value)
                .WithFlag(14, FieldMode.Read, name: "PWRSTMSPI0", valueProviderCallback: _ => powerEnableFlagMSPI0.Value)
                .WithTaggedFlag("PWRSTMSPI1", 15)
                .WithTaggedFlag("PWRSTMSPI2", 16)
                .WithTaggedFlag("PWRSTMSPI3", 17)
                .WithFlag(18, FieldMode.Read, name: "PWRSTGFX", valueProviderCallback: _ => powerEnableFlagGfx.Value)
                .WithFlag(19, FieldMode.Read, name: "PWRSTDISP", valueProviderCallback: _ => powerEnableFlagDisp.Value)
                .WithFlag(20, FieldMode.Read, name: "PWRSTDISPPHY", valueProviderCallback: _ => powerEnableFlagDispPhy.Value)
                .WithFlag(21, FieldMode.Read, name: "PWRSTCRYPTO", valueProviderCallback: _ => powerEnableFlagCrypto.Value)
                .WithTaggedFlag("PWRSTSDIO0", 22)
                .WithTaggedFlag("PWRSTSDIO1", 23)
                .WithTaggedFlag("PWRSTUSB", 24)
                .WithTaggedFlag("PWRSTUSBPHY", 25)
                .WithTaggedFlag("PWRSTDBG", 26)
                .WithFlag(27, FieldMode.Read, name: "PWRSTOTP", valueProviderCallback: _ => powerEnableFlagOtp.Value)
                .WithTaggedFlag("PWRSTIOSFD0", 28)
                .WithTaggedFlag("PWRSTIOSFD1", 29)
                .WithReservedBits(30, 2)
                ;

            Registers.AudioSubsystemPowerEnable.Define(this)
                .WithTaggedFlag("PWRENAUDREC", 0)
                .WithTaggedFlag("PWRENAUDPB", 1)
                .WithTaggedFlag("PWRENPDM0", 2)
                .WithTaggedFlag("PWRENPDM1", 3)
                .WithTaggedFlag("PWRENPDM2", 4)
                .WithTaggedFlag("PWRENPDM3", 5)
                .WithTaggedFlag("PWRENI2S0", 6)
                .WithTaggedFlag("PWRENI2S1", 7)
                .WithReservedBits(8, 2)
                .WithTaggedFlag("PWRENAUDADC", 10)
                .WithTaggedFlag("PWRENDSPA", 11)
                .WithReservedBits(12, 20)
                ;

            Registers.AudioSubsystemPowerStatus.Define(this)
                .WithTaggedFlag("PWRSTAUDREC", 0)
                .WithTaggedFlag("PWRSTAUDPB", 1)
                .WithTaggedFlag("PWRSTPDM0", 2)
                .WithTaggedFlag("PWRSTPDM1", 3)
                .WithTaggedFlag("PWRSTPDM2", 4)
                .WithTaggedFlag("PWRSTPDM3", 5)
                .WithTaggedFlag("PWRSTI2S0", 6)
                .WithTaggedFlag("PWRSTI2S1", 7)
                .WithReservedBits(8, 2)
                .WithTaggedFlag("PWRSTAUDADC", 10)
                .WithTaggedFlag("PWRSTDSPA", 11)
                .WithReservedBits(12, 20)
                ;

            Registers.MemoryPowerEnable.Define(this, 0x0000003F)
                .WithTag("PWRENDTCM", 0, 3)
                .WithTaggedFlag("PWRENNVM0", 3)
                .WithFlag(4, out powerEnableFlagCacheB0, name: "PWRENCACHEB0")
                .WithFlag(5, out powerEnableFlagCacheB2, name: "PWRENCACHEB2")
                .WithReservedBits(6, 26)
                ;

            Registers.MemoryPowerStatus.Define(this, 0x0000003F)
                .WithTag("PWRSTDTCM", 0, 3)
                .WithTaggedFlag("PWRSTNVM0", 3)
                .WithFlag(4, FieldMode.Read, name: "PWRSTCACHEB0", valueProviderCallback: _ => powerEnableFlagCacheB0.Value)
                .WithFlag(5, FieldMode.Read, name: "PWRSTCACHEB2", valueProviderCallback: _ => powerEnableFlagCacheB2.Value)
                .WithReservedBits(6, 26)
                ;

            Registers.MemoryRetConfiguration.Define(this, 0x00000008)
                .WithTag("DTCMPWDSLP", 0, 3)
                .WithTaggedFlag("NVM0PWDSLP", 3)
                .WithTaggedFlag("CACHEPWDSLP", 4)
                .WithReservedBits(5, 27)
                ;

            Registers.SystemPowerStatus.Define(this, 0x0000000F)
                .WithTaggedFlag("PWRSTMCUL", 0)
                .WithTaggedFlag("PWRSTMCUH", 1)
                .WithTaggedFlag("PWRSTDSP0H", 2)
                .WithTaggedFlag("PWRSTDSP1H", 3)
                .WithReservedBits(4, 25)
                .WithTaggedFlag("CORESLEEP", 29)
                .WithTaggedFlag("COREDEEPSLEEP", 30)
                .WithTaggedFlag("SYSDEEPSLEEP", 31)
                ;

            Registers.SharedSRAMPowerEnable.Define(this)
                .WithValueField(0, 3, out powerEnableFlagSSRAM, name: "PWRENSSRAM")
                .WithReservedBits(3, 29)
                ;

            Registers.SharedSRAMPowerStatus.Define(this, 0x00000003)
                .WithValueField(0, 3, name: "SSRAMPWRST", valueProviderCallback: _ => powerEnableFlagSSRAM.Value)
                .WithReservedBits(3, 29)
                ;

            Registers.SharedSRAMRetConfiguration.Define(this, 0x000003FC)
                .WithValueField(0, 10, name: "SSRAMPWR")
                .WithReservedBits(10, 22)
                ;

            Registers.DevicePowerEventEnable.Define(this)
                .WithTaggedFlag("MCULEVEN", 0)
                .WithTaggedFlag("MCUHEVEN", 1)
                .WithTaggedFlag("HCPAEVEN", 2)
                .WithTaggedFlag("HCPBEVEN", 3)
                .WithTaggedFlag("HCPCEVEN", 4)
                .WithTaggedFlag("ADCEVEN", 5)
                .WithTaggedFlag("MSPIEVEN", 6)
                .WithTaggedFlag("AUDEVEN", 7)
                .WithReservedBits(8, 24)
                ;

            Registers.MemoryPowerEventEnable.Define(this)
                .WithTag("DTCMEN", 0, 3)
                .WithTaggedFlag("NVM0EN", 3)
                .WithTaggedFlag("CACHEB0EN", 4)
                .WithTaggedFlag("CACHEB2EN", 5)
                .WithReservedBits(6, 26)
                ;

            Registers.MultimediaSystemOverride.Define(this, 0x00000FFC)
                .WithValueField(0, 12, name: "MMSOVR")
                .WithReservedBits(12, 20)
                ;

            Registers.DSP0PowerAndResetControls.Define(this, 0x00000008)
                .WithTag("DSP0PCMRSTDLY", 0, 4)
                .WithTaggedFlag("DSP0PCMRSTOR", 4)
                .WithReservedBits(5, 27)
                ;

            Registers.DSP0PerformanceControl.Define(this, 0x00000009)
                .WithTag("DSP0PERFREQ", 0, 2)
                .WithTaggedFlag("DSP0PERFACK", 2)
                .WithTag("DSP0PERFSTATUS", 3, 2)
                .WithReservedBits(5, 27)
                ;

            Registers.DSP0MemoryPowerEnable.Define(this)
                .WithTaggedFlag("PWRENDSP0RAM", 0)
                .WithTaggedFlag("PWRENDSP0ICACHE", 1)
                .WithReservedBits(2, 30)
                ;

            Registers.DSP0MemoryPowerStatus.Define(this)
                .WithTaggedFlag("PWRSTDSP0RAM", 0)
                .WithTaggedFlag("PWRSTDSP0ICACHE", 1)
                .WithReservedBits(2, 30)
                ;

            Registers.DSP0MemoryRetConfiguration.Define(this)
                .WithTaggedFlag("RAMPWDDSP0OFF", 0)
                .WithTaggedFlag("DSP0RAMACTMCU", 1)
                .WithTaggedFlag("ICACHEPWDDSP0OFF", 2)
                .WithTaggedFlag("DSP0RAMACTDISP", 3)
                .WithTaggedFlag("DSP0RAMACTGFX", 4)
                .WithReservedBits(5, 27)
                ;

            Registers.DSP1PowerAndResetControls.Define(this, 0x00000008)
                .WithTag("DSP1PCMRSTDLY", 0, 4)
                .WithTaggedFlag("DSP1PCMRSTOR", 4)
                .WithReservedBits(5, 27)
                ;

            Registers.DSP1PerformanceControl.Define(this, 0x00000009)
                .WithTag("DSP1PERFREQ", 0, 2)
                .WithTaggedFlag("DSP1PERFACK", 2)
                .WithTag("DSP1PERFSTATUS", 3, 2)
                .WithReservedBits(5, 27)
                ;

            Registers.DSP1MemoryPowerEnable.Define(this)
                .WithTaggedFlag("PWRENDSP1RAM", 0)
                .WithTaggedFlag("PWRENDSP1ICACHE", 1)
                .WithReservedBits(2, 30)
                ;

            Registers.DSP1MemoryPowerStatus.Define(this)
                .WithTaggedFlag("PWRSTDSP1RAM", 0)
                .WithTaggedFlag("PWRSTDSP1ICACHE", 1)
                .WithReservedBits(2, 30)
                ;

            Registers.DSP1MemoryRetConfiguration.Define(this)
                .WithTaggedFlag("RAMPWDDSP1OFF", 0)
                .WithTaggedFlag("DSP1RAMACTMCU", 1)
                .WithTaggedFlag("ICACHEPWDDSP1OFF", 2)
                .WithTaggedFlag("DSP1RAMACTDISP", 3)
                .WithTaggedFlag("DSP1RAMACTGFX", 4)
                .WithReservedBits(5, 27)
                ;

            Registers.VoltageRegulatorsControl.Define(this)
                .WithTaggedFlag("SIMOBUCKEN", 0)
                .WithReservedBits(1, 31)
                ;

            Registers.VoltageRegulatorsLegacyLowPowerOverrides.Define(this)
                .WithTaggedFlag("IGNOREIOS", 0)
                .WithTaggedFlag("IGNOREHCPA", 1)
                .WithTaggedFlag("IGNOREHCPB", 2)
                .WithTaggedFlag("IGNOREHCPC", 3)
                .WithTaggedFlag("IGNOREHCPD", 4)
                .WithTaggedFlag("IGNOREHCPE", 5)
                .WithTaggedFlag("IGNOREMSPI", 6)
                .WithTaggedFlag("IGNOREGFX", 7)
                .WithTaggedFlag("IGNOREDISP", 8)
                .WithTaggedFlag("IGNOREDISPPHY", 9)
                .WithTaggedFlag("IGNORECRYPTO", 10)
                .WithTaggedFlag("IGNORESDIO", 11)
                .WithTaggedFlag("IGNOREUSB", 12)
                .WithTaggedFlag("IGNOREUSBPHY", 13)
                .WithTaggedFlag("IGNOREAUD", 14)
                .WithTaggedFlag("IGNOREDSPA", 15)
                .WithTaggedFlag("IGNOREDSP0H", 16)
                .WithTaggedFlag("IGNOREDSP1H", 17)
                .WithTaggedFlag("IGNOREDBG", 18)
                .WithReservedBits(19, 13)
                ;

            // FIX: SIMOBUCKST must be ACT=3 (0b11) at bits 4-5 => 0x30 for HP switch check am_hal_pwrctrl_mcu_mode_select: PWRCTRL_VRSTATUS_SIMOBUCKST_ACT=3 per apollo510.h:82157
            Registers.VoltageRegulatorsStatus.Define(this, 0x00000030)
                .WithTag("CORELDOST", 0, 2)
                .WithTag("MEMLDOST", 2, 2)
                .WithTag("SIMOBUCKST", 4, 2)
                .WithReservedBits(6, 26)
                ;

            Registers.ULPLowPowerWeights0.Define(this)
                .WithTag("WTULPMCU", 0, 4)
                .WithTag("WTULPDSP0", 4, 4)
                .WithTag("WTULPDSP1", 8, 4)
                .WithTag("WTULPIOS", 12, 4)
                .WithTag("WTULPUART0", 16, 4)
                .WithTag("WTULPUART1", 20, 4)
                .WithTag("WTULPUART2", 24, 4)
                .WithTag("WTULPUART3", 28, 4)
                ;

            Registers.ULPLowPowerWeights1.Define(this)
                .WithTag("WTULPIOM0", 0, 4)
                .WithTag("WTULPIOM1", 4, 4)
                .WithTag("WTULPIOM2", 8, 4)
                .WithTag("WTULPIOM3", 12, 4)
                .WithTag("WTULPIOM4", 16, 4)
                .WithTag("WTULPIOM5", 20, 4)
                .WithTag("WTULPIOM6", 24, 4)
                .WithTag("WTULPIOM7", 28, 4)
                ;

            Registers.ULPLowPowerWeights2.Define(this)
                .WithTag("WTULPADC", 0, 4)
                .WithTag("WTULPMSPI0", 4, 4)
                .WithTag("WTULPMSPI1", 8, 4)
                .WithTag("WTULPGFX", 12, 4)
                .WithTag("WTULPDISP", 16, 4)
                .WithTag("WTULPCRYPTO", 20, 4)
                .WithTag("WTULPSDIO", 24, 4)
                .WithTag("WTULPUSB", 28, 4)
                ;

            Registers.ULPLowPowerWeights3.Define(this)
                .WithTag("WTULPDSPA", 0, 4)
                .WithTag("WTULPDBG", 4, 4)
                .WithTag("WTULPAUDREC", 8, 4)
                .WithTag("WTULPAUDPB", 12, 4)
                .WithTag("WTULPAUDADC", 16, 4)
                .WithReservedBits(20, 8)
                .WithTag("WTULPMSPI2", 28, 4)
                ;

            Registers.ULPLowPowerWeights4.Define(this)
                .WithTag("WTULPI2S0", 0, 4)
                .WithTag("WTULPI2S1", 4, 4)
                .WithReservedBits(8, 8)
                .WithTag("WTULPPDM0", 16, 4)
                .WithTag("WTULPPDM1", 20, 4)
                .WithTag("WTULPPDM2", 24, 4)
                .WithTag("WTULPPDM3", 28, 4)
                ;

            Registers.ULPLowPowerWeights5.Define(this)
                .WithTag("WTULPDISPPHY", 0, 4)
                .WithTag("WTULPUSBPHY", 4, 4)
                .WithReservedBits(8, 24)
                ;

            Registers.LPLowPowerWeights0.Define(this)
                .WithTag("WTLPMCU", 0, 4)
                .WithTag("WTLPDSP0", 4, 4)
                .WithTag("WTLPDSP1", 8, 4)
                .WithTag("WTLPIOS", 12, 4)
                .WithTag("WTLPUART0", 16, 4)
                .WithTag("WTLPUART1", 20, 4)
                .WithTag("WTLPUART2", 24, 4)
                .WithTag("WTLPUART3", 28, 4)
                ;

            Registers.LPLowPowerWeights1.Define(this)
                .WithTag("WTLPIOM0", 0, 4)
                .WithTag("WTLPIOM1", 4, 4)
                .WithTag("WTLPIOM2", 8, 4)
                .WithTag("WTLPIOM3", 12, 4)
                .WithTag("WTLPIOM4", 16, 4)
                .WithTag("WTLPIOM5", 20, 4)
                .WithTag("WTLPIOM6", 24, 4)
                .WithTag("WTLPIOM7", 28, 4)
                ;

            Registers.LPLowPowerWeights2.Define(this)
                .WithTag("WTLPADC", 0, 4)
                .WithTag("WTLPMSPI0", 4, 4)
                .WithTag("WTLPMSPI1", 8, 4)
                .WithTag("WTLPGFX", 12, 4)
                .WithTag("WTLPDISP", 16, 4)
                .WithTag("WTLPCRYPTO", 20, 4)
                .WithTag("WTLPSDIO", 24, 4)
                .WithTag("WTLPUSB", 28, 4)
                ;

            Registers.LPLowPowerWeights3.Define(this)
                .WithTag("WTLPDSPA", 0, 4)
                .WithTag("WTLPDBG", 4, 4)
                .WithTag("WTLPAUDREC", 8, 4)
                .WithTag("WTLPAUDPB", 12, 4)
                .WithTag("WTLPAUDADC", 16, 4)
                .WithReservedBits(20, 8)
                .WithTag("WTLPMSPI2", 28, 4)
                ;

            Registers.LPLowPowerWeights4.Define(this)
                .WithTag("WTLPI2S0", 0, 4)
                .WithTag("WTLPI2S1", 4, 4)
                .WithReservedBits(8, 8)
                .WithTag("WTLPPDM0", 16, 4)
                .WithTag("WTLPPDM1", 20, 4)
                .WithTag("WTLPPDM2", 24, 4)
                .WithTag("WTLPPDM3", 28, 4)
                ;

            Registers.LPLowPowerWeights5.Define(this)
                .WithTag("WTLPDISPPHY", 0, 4)
                .WithTag("WTLPUSBPHY", 4, 4)
                .WithReservedBits(8, 24)
                ;

            Registers.HPLowPowerWeights0.Define(this)
                .WithTag("WTHPMCU", 0, 4)
                .WithTag("WTHPDSP0", 4, 4)
                .WithTag("WTHPDSP1", 8, 4)
                .WithTag("WTHPIOS", 12, 4)
                .WithTag("WTHPUART0", 16, 4)
                .WithTag("WTHPUART1", 20, 4)
                .WithTag("WTHPUART2", 24, 4)
                .WithTag("WTHPUART3", 28, 4)
                ;

            Registers.HPLowPowerWeights1.Define(this)
                .WithTag("WTHPIOM0", 0, 4)
                .WithTag("WTHPIOM1", 4, 4)
                .WithTag("WTHPIOM2", 8, 4)
                .WithTag("WTHPIOM3", 12, 4)
                .WithTag("WTHPIOM4", 16, 4)
                .WithTag("WTHPIOM5", 20, 4)
                .WithTag("WTHPIOM6", 24, 4)
                .WithTag("WTHPIOM7", 28, 4)
                ;

            Registers.HPLowPowerWeights2.Define(this)
                .WithTag("WTHPADC", 0, 4)
                .WithTag("WTHPMSPI0", 4, 4)
                .WithTag("WTHPMSPI1", 8, 4)
                .WithTag("WTHPGFX", 12, 4)
                .WithTag("WTHPDISP", 16, 4)
                .WithTag("WTHPCRYPTO", 20, 4)
                .WithTag("WTHPSDIO", 24, 4)
                .WithTag("WTHPUSB", 28, 4)
                ;

            Registers.HPLowPowerWeights3.Define(this)
                .WithTag("WTHPDSPA", 0, 4)
                .WithTag("WTHPDBG", 4, 4)
                .WithTag("WTHPAUDREC", 8, 4)
                .WithTag("WTHPAUDPB", 12, 4)
                .WithTag("WTHPAUDADC", 16, 4)
                .WithReservedBits(20, 8)
                .WithTag("WTHPMSPI2", 28, 4)
                ;

            Registers.HPLowPowerWeights4.Define(this)
                .WithTag("WTHPI2S0", 0, 4)
                .WithTag("WTHPI2S1", 4, 4)
                .WithReservedBits(8, 8)
                .WithTag("WTHPPDM0", 16, 4)
                .WithTag("WTHPPDM1", 20, 4)
                .WithTag("WTHPPDM2", 24, 4)
                .WithTag("WTHPPDM3", 28, 4)
                ;

            Registers.HPLowPowerWeights5.Define(this)
                .WithTag("WTHPDISPPHY", 0, 4)
                .WithTag("WTHPUSBPHY", 4, 4)
                .WithReservedBits(8, 24)
                ;

            Registers.SleepLowPowerWeights.Define(this)
                .WithTag("WTDSMCU", 0, 4)
                .WithReservedBits(4, 28)
                ;

            Registers.VoltageRegulatorsDemotionThreshold.Define(this)
                .WithTag("VRDEMOTIONTHR", 0, 32)
                ;

            Registers.SRAMControl.Define(this)
                .WithReservedBits(0, 1)
                .WithTaggedFlag("SRAMCLKGATE", 1)
                .WithTaggedFlag("SRAMMASTERCLKGATE", 2)
                .WithReservedBits(3, 5)
                .WithTag("SRAMLIGHTSLEEP", 8, 12)
                .WithReservedBits(20, 12)
                ;

            Registers.ADCPowerStatus.Define(this, 0x0000003F)
                .WithTaggedFlag("ADCPWD", 0)
                .WithTaggedFlag("BGTPWD", 1)
                .WithTaggedFlag("VPTATPWD", 2)
                .WithTaggedFlag("VBATPWD", 3)
                .WithTaggedFlag("REFKEEPPWD", 4)
                .WithTaggedFlag("REFBUFPWD", 5)
                .WithReservedBits(6, 26)
                ;

            Registers.AudioADCPowerStatus.Define(this, 0x0000003F)
                .WithTaggedFlag("AUDADCPWD", 0)
                .WithTaggedFlag("AUDBGTPWD", 1)
                .WithTaggedFlag("AUDVPTATPWD", 2)
                .WithTaggedFlag("AUDVBATPWD", 3)
                .WithTaggedFlag("AUDREFKEEPPWD", 4)
                .WithTaggedFlag("AUDREFBUFPWD", 5)
                .WithReservedBits(6, 26)
                ;

            Registers.EnergyMonitorControl.Define(this, 0x000000FF)
                .WithTag("FREEZE", 0, 8)
                .WithTag("CLEAR", 8, 8)
                .WithReservedBits(16, 16)
                ;

            Registers.EnergyMonitorModeSelect0.Define(this)
                .WithTag("EMONSEL0", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect1.Define(this)
                .WithTag("EMONSEL1", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect2.Define(this)
                .WithTag("EMONSEL2", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect3.Define(this)
                .WithTag("EMONSEL3", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect4.Define(this)
                .WithTag("EMONSEL4", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect5.Define(this)
                .WithTag("EMONSEL5", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect6.Define(this)
                .WithTag("EMONSEL6", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorModeSelect7.Define(this)
                .WithTag("EMONSEL7", 0, 8)
                .WithReservedBits(8, 24)
                ;

            Registers.EnergyMonitorCount0.Define(this)
                .WithTag("EMONCOUNT0", 0, 32)
                ;

            Registers.EnergyMonitorCount1.Define(this)
                .WithTag("EMONCOUNT1", 0, 32)
                ;

            Registers.EnergyMonitorCount2.Define(this)
                .WithTag("EMONCOUNT2", 0, 32)
                ;

            Registers.EnergyMonitorCount3.Define(this)
                .WithTag("EMONCOUNT3", 0, 32)
                ;

            Registers.EnergyMonitorCount4.Define(this)
                .WithTag("EMONCOUNT4", 0, 32)
                ;

            Registers.EnergyMonitorCount5.Define(this)
                .WithTag("EMONCOUNT5", 0, 32)
                ;

            Registers.EnergyMonitorCount6.Define(this)
                .WithTag("EMONCOUNT6", 0, 32)
                ;

            Registers.EnergyMonitorCount7.Define(this)
                .WithTag("EMONCOUNT7", 0, 32)
                ;

            Registers.EnergyMonitorStatus.Define(this)
                .WithTaggedFlag("EMONOVERFLOW0", 0)
                .WithTaggedFlag("EMONOVERFLOW1", 1)
                .WithTaggedFlag("EMONOVERFLOW2", 2)
                .WithTaggedFlag("EMONOVERFLOW3", 3)
                .WithTaggedFlag("EMONOVERFLOW4", 4)
                .WithTaggedFlag("EMONOVERFLOW5", 5)
                .WithTaggedFlag("EMONOVERFLOW6", 6)
                .WithTaggedFlag("EMONOVERFLOW7", 7)
                .WithReservedBits(8, 24)
                ;
        }

        // Some modules share a single power domain, e.g. enabling IOM1 powers on all IOM0..3 modules.
        private bool PowerStatusIOM0_3 => powerEnableFlagsIOM0_3.Any(flag => flag.Value);

        private bool PowerStatusIOM4_7 => powerEnableFlagsIOM4_7.Any(flag => flag.Value);

        private bool PowerStatusUart0_3 => powerEnableFlagsUart0_3.Any(flag => flag.Value);

        private IFlagRegisterField powerEnableFlagADC;
        private IFlagRegisterField powerEnableFlagCrypto;
        private IFlagRegisterField powerEnableFlagOtp;
        private IFlagRegisterField powerEnableFlagMSPI0;
        private IFlagRegisterField powerEnableFlagGfx;
        private IFlagRegisterField powerEnableFlagDisp;
        private IFlagRegisterField powerEnableFlagDispPhy;
        private IFlagRegisterField powerEnableFlagCacheB0;
        private IFlagRegisterField powerEnableFlagCacheB2;
        private IValueRegisterField powerEnableFlagSSRAM;
        private IFlagRegisterField[] powerEnableFlagsIOM0_3;
        private IFlagRegisterField[] powerEnableFlagsIOM4_7;
        private IFlagRegisterField[] powerEnableFlagsUart0_3;
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
            DSP0PowerAndResetControls = 0x50,
            DSP0PerformanceControl = 0x54,
            DSP0MemoryPowerEnable = 0x58,
            DSP0MemoryPowerStatus = 0x5C,
            DSP0MemoryRetConfiguration = 0x60,
            DSP1PowerAndResetControls = 0x70,
            DSP1PerformanceControl = 0x74,
            DSP1MemoryPowerEnable = 0x78,
            DSP1MemoryPowerStatus = 0x7C,
            DSP1MemoryRetConfiguration = 0x80,
            VoltageRegulatorsControl = 0x100,
            VoltageRegulatorsLegacyLowPowerOverrides = 0x104,
            VoltageRegulatorsStatus = 0x108,
            ULPLowPowerWeights0 = 0x140,
            ULPLowPowerWeights1 = 0x144,
            ULPLowPowerWeights2 = 0x148,
            ULPLowPowerWeights3 = 0x14C,
            ULPLowPowerWeights4 = 0x150,
            ULPLowPowerWeights5 = 0x154,
            LPLowPowerWeights0 = 0x158,
            LPLowPowerWeights1 = 0x15C,
            LPLowPowerWeights2 = 0x160,
            LPLowPowerWeights3 = 0x164,
            LPLowPowerWeights4 = 0x168,
            LPLowPowerWeights5 = 0x16C,
            HPLowPowerWeights0 = 0x170,
            HPLowPowerWeights1 = 0x174,
            HPLowPowerWeights2 = 0x178,
            HPLowPowerWeights3 = 0x17C,
            HPLowPowerWeights4 = 0x180,
            HPLowPowerWeights5 = 0x184,
            SleepLowPowerWeights = 0x188,
            VoltageRegulatorsDemotionThreshold = 0x18C,
            SRAMControl = 0x190,
            ADCPowerStatus = 0x194,
            AudioADCPowerStatus = 0x198,
            EnergyMonitorControl = 0x200,
            EnergyMonitorModeSelect0 = 0x204,
            EnergyMonitorModeSelect1 = 0x208,
            EnergyMonitorModeSelect2 = 0x20C,
            EnergyMonitorModeSelect3 = 0x210,
            EnergyMonitorModeSelect4 = 0x214,
            EnergyMonitorModeSelect5 = 0x218,
            EnergyMonitorModeSelect6 = 0x21C,
            EnergyMonitorModeSelect7 = 0x220,
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