//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite clock-reset manager (CRM @ 0x40006000). NEW file — Lite-only
// block, no Apollo510 equivalent exists.
//
// Register map from pack/SVD/apollo510L.svd (17 registers). Each gate follows
// one pattern: CLKEN[0] (RW) drives CLKACTIVE[1] (RO mirror — the HAL polls
// CLKACTIVE via am_hal_delay_us_status_check after every enable, e.g.
// am_hal_crm_control_UART0_CLOCK_SET, so the mirror is load-bearing, same
// lesson as PWRCTRL shared flags), RSTN[2] (some gates), CRMRSTN[3],
// plus CLKSEL/CLKDIV. All gates reset 0x08 (CRMRSTN set, clock off).
// CRMCLKENSTATUS @ 0x1FC mirrors the 16 CLKEN bits (no HAL source polls it).
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510L_CRM : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_CRM(IMachine machine) : base(machine)
        {
            DefineRegisters();
        }

        public long Size => 0x200;

        private void DefineRegisters()
        {
            Registers.ADCCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnADC, name: "ADCCLKEN")
                .WithFlag(1, FieldMode.Read, name: "ADCCLKACTIVE", valueProviderCallback: _ => clkEnADC.Value)
                .WithFlag(2, out _, name: "ADCRSTN")
                .WithFlag(3, out _, name: "ADCCRMRSTN")
                .WithValueField(4, 1, out _, name: "ADCCLKSEL")
                .WithReservedBits(5, 3)
                .WithValueField(8, 2, out _, name: "ADCCLKDIV")
                .WithReservedBits(10, 22)
                ;

            Registers.APBDISPCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnDISP, name: "APBDISPCLKEN")
                .WithFlag(1, FieldMode.Read, name: "APBDISPCLKACTIVE", valueProviderCallback: _ => clkEnDISP.Value)
                .WithFlag(2, out _, name: "APBDISPRSTN")
                .WithFlag(3, out _, name: "APBDISPCRMRSTN")
                .WithReservedBits(4, 28)
                ;

            Registers.APBDISPPHYCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnDISPPHY, name: "APBDISPPHYCLKEN")
                .WithFlag(1, FieldMode.Read, name: "APBDISPPHYCLKACTIVE", valueProviderCallback: _ => clkEnDISPPHY.Value)
                .WithFlag(2, out _, name: "APBDISPPHYRSTN")
                .WithFlag(3, out _, name: "APBDISPPHYCRMRSTN")
                .WithReservedBits(4, 28)
                ;

            Registers.APBUARTCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnUART, name: "APBUARTCLKEN")
                .WithFlag(1, FieldMode.Read, name: "APBUARTCLKACTIVE", valueProviderCallback: _ => clkEnUART.Value)
                .WithFlag(2, out _, name: "APBUARTRSTN")
                .WithFlag(3, out _, name: "APBUARTCRMRSTN")
                .WithReservedBits(4, 28)
                ;

            Registers.APBI2SCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnI2S, name: "APBI2SCLKEN")
                .WithFlag(1, FieldMode.Read, name: "APBI2SCLKACTIVE", valueProviderCallback: _ => clkEnI2S.Value)
                .WithFlag(2, out _, name: "APBI2SRSTN")
                .WithFlag(3, out _, name: "APBI2SCRMRSTN")
                .WithValueField(4, 1, out _, name: "APBI2SCLKSEL")
                .WithReservedBits(5, 27)
                ;

            Registers.APBPDMCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnPDM, name: "APBPDMCLKEN")
                .WithFlag(1, FieldMode.Read, name: "APBPDMCLKACTIVE", valueProviderCallback: _ => clkEnPDM.Value)
                .WithFlag(2, out _, name: "APBPDMRSTN")
                .WithFlag(3, out _, name: "APBPDMCRMRSTN")
                .WithReservedBits(4, 28)
                ;

            Registers.APBUSBCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnUSB, name: "APBUSBCLKEN")
                .WithFlag(1, FieldMode.Read, name: "APBUSBCLKACTIVE", valueProviderCallback: _ => clkEnUSB.Value)
                .WithFlag(2, out _, name: "APBUSBRSTN")
                .WithFlag(3, out _, name: "APBUSBCRMRSTN")
                .WithReservedBits(4, 28)
                ;

            Registers.DISPCLKCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnDISPCLK, name: "DISPCLKCLKEN")
                .WithFlag(1, FieldMode.Read, name: "DISPCLKCLKACTIVE", valueProviderCallback: _ => clkEnDISPCLK.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "DISPCLKCRMRSTN")
                .WithValueField(4, 2, out _, name: "DISPCLKCLKSEL")
                .WithReservedBits(6, 2)
                .WithValueField(8, 4, out _, name: "DISPCLKCLKDIV")
                .WithReservedBits(12, 20)
                ;

            Registers.DPHYPLLREFCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnDPHYPLLREF, name: "DPHYPLLREFCLKEN")
                .WithFlag(1, FieldMode.Read, name: "DPHYPLLREFCLKACTIVE", valueProviderCallback: _ => clkEnDPHYPLLREF.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "DPHYPLLREFCRMRSTN")
                .WithValueField(4, 3, out _, name: "DPHYPLLREFCLKSEL")
                .WithReservedBits(7, 1)
                .WithValueField(8, 3, out _, name: "DPHYPLLREFCLKDIV")
                .WithReservedBits(11, 21)
                ;

            Registers.DSPPDM0CRM.Define(this, 0x08)
                .WithFlag(0, out clkEnDSPPDM0, name: "DSPPDM0CLKEN")
                .WithFlag(1, FieldMode.Read, name: "DSPPDM0CLKACTIVE", valueProviderCallback: _ => clkEnDSPPDM0.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "DSPPDM0CRMRSTN")
                .WithValueField(4, 3, out _, name: "DSPPDM0CLKSEL")
                .WithReservedBits(7, 1)
                .WithValueField(8, 7, out _, name: "DSPPDM0CLKDIV")
                .WithReservedBits(15, 17)
                ;

            Registers.I2S0MCLKCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnI2S0MCLK, name: "I2S0MCLKCLKEN")
                .WithFlag(1, FieldMode.Read, name: "I2S0MCLKCLKACTIVE", valueProviderCallback: _ => clkEnI2S0MCLK.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "I2S0MCLKCRMRSTN")
                .WithValueField(4, 3, out _, name: "I2S0MCLKCLKSEL")
                .WithReservedBits(7, 1)
                .WithValueField(8, 7, out _, name: "I2S0MCLKCLKDIV")
                .WithReservedBits(15, 17)
                ;

            Registers.I2S0MCLKOUTCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnI2S0MCLKOUT, name: "I2S0MCLKOUTCLKEN")
                .WithFlag(1, FieldMode.Read, name: "I2S0MCLKOUTCLKACTIVE", valueProviderCallback: _ => clkEnI2S0MCLKOUT.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "I2S0MCLKOUTCRMRSTN")
                .WithValueField(4, 3, out _, name: "I2S0MCLKOUTCLKSEL")
                .WithReservedBits(7, 1)
                .WithValueField(8, 5, out _, name: "I2S0MCLKOUTCLKDIV")
                .WithReservedBits(13, 19)
                ;

            Registers.I2S0REFCLKCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnI2S0REFCLK, name: "I2S0REFCLKCLKEN")
                .WithFlag(1, FieldMode.Read, name: "I2S0REFCLKCLKACTIVE", valueProviderCallback: _ => clkEnI2S0REFCLK.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "I2S0REFCLKCRMRSTN")
                .WithValueField(4, 2, out _, name: "I2S0REFCLKCLKSEL")
                .WithReservedBits(6, 2)
                .WithValueField(8, 5, out _, name: "I2S0REFCLKCLKDIV")
                .WithReservedBits(13, 19)
                ;

            Registers.UART0HFCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnUART0HF, name: "UART0HFCLKEN")
                .WithFlag(1, FieldMode.Read, name: "UART0HFCLKACTIVE", valueProviderCallback: _ => clkEnUART0HF.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "UART0HFCRMRSTN")
                .WithValueField(4, 1, out _, name: "UART0HFCLKSEL")
                .WithReservedBits(5, 3)
                .WithValueField(8, 5, out _, name: "UART0HFCLKDIV")
                .WithReservedBits(13, 19)
                ;

            Registers.UART1HFCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnUART1HF, name: "UART1HFCLKEN")
                .WithFlag(1, FieldMode.Read, name: "UART1HFCLKACTIVE", valueProviderCallback: _ => clkEnUART1HF.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "UART1HFCRMRSTN")
                .WithValueField(4, 1, out _, name: "UART1HFCLKSEL")
                .WithReservedBits(5, 3)
                .WithValueField(8, 5, out _, name: "UART1HFCLKDIV")
                .WithReservedBits(13, 19)
                ;

            Registers.USBREFCLKCRM.Define(this, 0x08)
                .WithFlag(0, out clkEnUSBREFCLK, name: "USBREFCLKCLKEN")
                .WithFlag(1, FieldMode.Read, name: "USBREFCLKCLKACTIVE", valueProviderCallback: _ => clkEnUSBREFCLK.Value)
                .WithReservedBits(2, 1)
                .WithFlag(3, out _, name: "USBREFCLKCRMRSTN")
                .WithValueField(4, 3, out _, name: "USBREFCLKCLKSEL")
                .WithReservedBits(7, 1)
                .WithValueField(8, 3, out _, name: "USBREFCLKCLKDIV")
                .WithReservedBits(11, 21)
                ;
            Registers.ClockEnableStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "USBREFCLKCLKENABLED", valueProviderCallback: _ => clkEnUSBREFCLK.Value)
                .WithFlag(1, FieldMode.Read, name: "UART1HFCLKENABLED", valueProviderCallback: _ => clkEnUART1HF.Value)
                .WithFlag(2, FieldMode.Read, name: "UART0HFCLKENABLED", valueProviderCallback: _ => clkEnUART0HF.Value)
                .WithFlag(3, FieldMode.Read, name: "I2S0REFCLKCLKENABLED", valueProviderCallback: _ => clkEnI2S0REFCLK.Value)
                .WithFlag(4, FieldMode.Read, name: "I2S0MCLKOUTCLKENABLED", valueProviderCallback: _ => clkEnI2S0MCLKOUT.Value)
                .WithFlag(5, FieldMode.Read, name: "I2S0MCLKCLKENABLED", valueProviderCallback: _ => clkEnI2S0MCLK.Value)
                .WithFlag(6, FieldMode.Read, name: "DSPPDM0CLKENABLED", valueProviderCallback: _ => clkEnDSPPDM0.Value)
                .WithFlag(7, FieldMode.Read, name: "DPHYPLLREFCLKENABLED", valueProviderCallback: _ => clkEnDPHYPLLREF.Value)
                .WithFlag(8, FieldMode.Read, name: "DISPCLKCLKENABLED", valueProviderCallback: _ => clkEnDISPCLK.Value)
                .WithFlag(9, FieldMode.Read, name: "APBUSBCLKENABLED", valueProviderCallback: _ => clkEnUSB.Value)
                .WithFlag(10, FieldMode.Read, name: "APBPDMCLKENABLED", valueProviderCallback: _ => clkEnPDM.Value)
                .WithFlag(11, FieldMode.Read, name: "APBI2SCLKENABLED", valueProviderCallback: _ => clkEnI2S.Value)
                .WithFlag(12, FieldMode.Read, name: "APBUARTCLKENABLED", valueProviderCallback: _ => clkEnUART.Value)
                .WithFlag(13, FieldMode.Read, name: "APBDISPPHYCLKENABLED", valueProviderCallback: _ => clkEnDISPPHY.Value)
                .WithFlag(14, FieldMode.Read, name: "APBDISPCLKENABLED", valueProviderCallback: _ => clkEnDISP.Value)
                .WithFlag(15, FieldMode.Read, name: "ADCCLKENABLED", valueProviderCallback: _ => clkEnADC.Value)
                .WithReservedBits(16, 16)
                ;
        }

        private IFlagRegisterField clkEnADC;
        private IFlagRegisterField clkEnDISP;
        private IFlagRegisterField clkEnDISPPHY;
        private IFlagRegisterField clkEnUART;
        private IFlagRegisterField clkEnI2S;
        private IFlagRegisterField clkEnPDM;
        private IFlagRegisterField clkEnUSB;
        private IFlagRegisterField clkEnDISPCLK;
        private IFlagRegisterField clkEnDPHYPLLREF;
        private IFlagRegisterField clkEnDSPPDM0;
        private IFlagRegisterField clkEnI2S0MCLK;
        private IFlagRegisterField clkEnI2S0MCLKOUT;
        private IFlagRegisterField clkEnI2S0REFCLK;
        private IFlagRegisterField clkEnUART0HF;
        private IFlagRegisterField clkEnUART1HF;
        private IFlagRegisterField clkEnUSBREFCLK;

        private enum Registers : long
        {
            ADCCRM = 0x10,
            APBDISPCRM = 0x14,
            APBDISPPHYCRM = 0x18,
            APBUARTCRM = 0x1C,
            APBI2SCRM = 0x20,
            APBPDMCRM = 0x24,
            APBUSBCRM = 0x28,
            DISPCLKCRM = 0x2C,
            DPHYPLLREFCRM = 0x30,
            DSPPDM0CRM = 0x34,
            I2S0MCLKCRM = 0x38,
            I2S0MCLKOUTCRM = 0x3C,
            I2S0REFCLKCRM = 0x40,
            UART0HFCRM = 0x44,
            UART1HFCRM = 0x48,
            USBREFCLKCRM = 0x4C,
            ClockEnableStatus = 0x1FC,
        }
    }
}
