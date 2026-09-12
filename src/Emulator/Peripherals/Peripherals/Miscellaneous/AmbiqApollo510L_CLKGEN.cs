//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite clock generator (CLKGEN @ 0x40004000).
// NEW file for the Lite family.
//
// Register map from pack/SVD/apollo510L.svd (12 registers). Lite has NO
// CLOCKEN control registers — only CLOCKENSTAT/2/3STAT status. Per-peripheral
// gating lives in CRM (see AmbiqApollo510L_CRM).
//
// CLOCKENSTAT behavior: all clocks report ready (ideal-timing abstraction,
// same approach as the proven Apollo510B platform stub). Verified: no Lite
// HAL source polls these registers, and hello/rtc/stimer boot with them
// ready. Revisit when a clock-gating firmware vehicle (HP-mode/DSP) exists.
//
// CLKCTRL cross-checked against CMSIS (SVD omits PLLVCOEN/PLLDIVEN/
// XTHSMUXSEL/DISPCTRLCLKEN/FORCEON/GFXCORE bits); other registers per SVD.
// Reset 0x80 = DISPCTRLCLKEN; firmware clears it during clock init.
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510L_CLKGEN : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_CLKGEN(IMachine machine) : base(machine)
        {
            DefineRegisters();
        }

        public long Size => 0x800;

        private void DefineRegisters()
        {
            Registers.OutputControl.Define(this)
                .WithReservedBits(0, 6)
                .WithFlag(6, out _, name: "RTCOSEL")
                .WithFlag(7, out _, name: "SECURERTCOSEL")
                .WithReservedBits(8, 24)
                ;

            Registers.ClockOutput.Define(this)
                .WithValueField(0, 6, out _, name: "CKSEL")
                .WithReservedBits(6, 1)
                .WithFlag(7, out _, name: "CKEN")
                .WithFlag(8, out _, name: "AOCLKFORCEEN")
                .WithReservedBits(9, 23)
                ;

            Registers.HFAdjust.Define(this, 0x0025B800)
                .WithFlag(0, out _, name: "HFADJEN")
                .WithValueField(1, 3, out _, name: "HFADJCK")
                .WithReservedBits(4, 4)
                .WithValueField(8, 12, out _, name: "HFXTADJ")
                .WithFlag(20, out _, name: "HFWARMUP")
                .WithValueField(21, 3, out _, name: "HFADJGAIN")
                .WithValueField(24, 5, out _, name: "HFADJMAXDELTA")
                .WithReservedBits(29, 3)
                ;

            // Ideal-timing abstraction: every clock reports ready.
            Registers.ClockEnableStatus.Define(this)
                .WithValueField(0, 32, name: "CLOCKENSTAT", valueProviderCallback: _ => 0xFFFFFFFF);
            Registers.ClockEnableStatus2.Define(this)
                .WithValueField(0, 32, name: "CLOCKEN2STAT", valueProviderCallback: _ => 0xFFFFFFFF);
            Registers.ClockEnableStatus3.Define(this)
                .WithValueField(0, 32, name: "CLOCKEN3STAT", valueProviderCallback: _ => 0xFFFFFFFF);

            Registers.Misc.Define(this)
                .WithFlag(0, out _, name: "FRCHFRC")
                .WithFlag(1, out _, name: "FRCBURSTOFF")
                .WithReservedBits(2, 7)
                .WithFlag(9, out _, name: "PWRONCLKENDISP")
                .WithFlag(10, out _, name: "PWRONCLKENGFX")
                .WithFlag(11, out _, name: "PWRONCLKENUSB")
                .WithFlag(12, out _, name: "PWRONCLKENSDIO")
                .WithFlag(13, out _, name: "PWRONCLKENCRYPTO")
                .WithFlag(14, out _, name: "PWRONCLKENI2S0")
                .WithFlag(15, out _, name: "AXIXACLKENOVRRIDE")
                .WithFlag(16, out _, name: "PWRONCLKENI2S0REFCLK")
                .WithFlag(17, out _, name: "PWRONCLKENUSBREFCLK")
                .WithFlag(18, out _, name: "CM4DAXICLKGATEEN")
                .WithFlag(19, out _, name: "GFXCLKCLKGATEEN")
                .WithFlag(20, out _, name: "GFXAXICLKCLKGATEEN")
                .WithFlag(21, out _, name: "APBDMACPUCLKCLKGATEEN")
                .WithFlag(22, out _, name: "ETMTRACECLKCLKGATEEN")
                .WithFlag(23, out _, name: "HFRCFUNCCLKGATEEN")
                .WithFlag(24, out _, name: "HFRC96TRUNKGATE")
                .WithFlag(25, out _, name: "CLKGENMISCSPARE")
                .WithReservedBits(26, 6)
                ;

            Registers.LFRCControl.Define(this)
                .WithFlag(0, out _, name: "LFRCOUT")
                .WithFlag(1, out _, name: "LFRCPWD")
                .WithReservedBits(2, 30)
                ;

            Registers.Spares.Define(this)
                .WithValueField(0, 32, out _, name: "CLKGENSPARES");

            Registers.HFRCIdleCounters.Define(this, 0x00000105)
                .WithValueField(0, 6, out _, name: "HFRCPWRDOWNDELAY")
                .WithValueField(6, 6, out _, name: "HFRCCLKREQDELAY")
                .WithFlag(12, out _, name: "UPDATEENABLE")
                .WithReservedBits(13, 19)
                ;

            Registers.MSPIIOClockControl.Define(this, 0x00004214)
                .WithFlag(0, out _, name: "MSPI0IOCLKEN")
                .WithValueField(1, 4, out _, name: "MSPI0IOCLKSEL")
                .WithFlag(5, out _, name: "MSPI1IOCLKEN")
                .WithValueField(6, 4, out _, name: "MSPI1IOCLKSEL")
                .WithFlag(10, out _, name: "MSPI2IOCLKEN")
                .WithValueField(11, 4, out _, name: "MSPI2IOCLKSEL")
                .WithReservedBits(15, 17)
                ;

            // Full map from CMSIS apollo510L.h (the SVD only names I3CCLKEN).
            // Reset 0x80 = DISPCTRLCLKEN; firmware clears it during clock init.
            Registers.ClockControl.Define(this, 0x00000080)
                .WithFlag(0, out _, name: "GFXCORECLKEN")
                .WithFlag(1, out _, name: "GFXCORECLKSEL")
                .WithFlag(2, out _, name: "STCLKFORCEON")
                .WithFlag(3, out _, name: "PMUCLKFORCEON")
                .WithFlag(4, out _, name: "DBGCLKFORCEON")
                .WithFlag(5, out _, name: "IWICCLKFORCEON")
                .WithFlag(6, out _, name: "MCUCLKFORCEON")
                .WithFlag(7, out _, name: "DISPCTRLCLKEN")
                .WithFlag(8, out _, name: "XTHSMUXSEL")
                .WithFlag(9, out _, name: "PLLDIVEN")
                .WithFlag(10, out _, name: "PLLVCOEN")
                .WithFlag(11, out _, name: "I3CCLKEN")
                .WithReservedBits(12, 20)
                ;
        }

        private enum Registers : long
        {
            OutputControl = 0x0C,
            ClockOutput = 0x10,
            HFAdjust = 0x20,
            ClockEnableStatus = 0x30,
            ClockEnableStatus2 = 0x34,
            ClockEnableStatus3 = 0x38,
            Misc = 0x44,
            LFRCControl = 0x78,
            Spares = 0x88,
            HFRCIdleCounters = 0x8C,
            MSPIIOClockControl = 0x110,
            ClockControl = 0x120,
        }
    }
}
