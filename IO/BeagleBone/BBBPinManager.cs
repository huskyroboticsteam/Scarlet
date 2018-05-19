using Scarlet.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Scarlet.IO.BeagleBone
{
	public static class BBBPinManager
	{
		private static Dictionary<BBBPin, PinAssignment> GPIOMappings, PWMMappings, I2CMappings, SPIMappings, CANMappings, UARTMappings;
		private static Dictionary<BBBPin, int> ADCMappings;
		private static bool[] EnableI2CBuses = new bool[2];
		private static bool[] EnableSPIBuses = new bool[2];
		private static bool[] EnablePWMBuses = new bool[3];
		private static bool[] EnableCANBuses = new bool[2];
		private static bool[] EnableUARTBuses = new bool[4];

		#region Adding Mappings
		/// <summary>
		/// Adds a GPIO mapping to the BBB device tree overlay, preparing the pin for use in hardware & kernel.
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="SelectedPin"> The pin to assign as GPIO. </param>
		/// <param name="IsOutput"> Whether this pin will be used as input or output. </param>
		/// <param name="Resistor"> The type of resistance to apply to the pin. Only useful for inputs. </param>
		/// <param name="FastSlew"> Whether to use fast or slow rising/falling edges. Leave at true unless this causes issues in your application. </param>
		/// <exception cref="InvalidOperationException"> If this pin cannot be used for GPIO at this time. Reason will be given. </exception>
		public static void AddMappingGPIO(BBBPin SelectedPin, bool IsOutput, ResistorState Resistor, bool FastSlew = true)
		{
			byte Mode = Pin.GetModeID(SelectedPin, BBBPinMode.GPIO);
			if (Mode == 255) { throw new InvalidOperationException("This type of output is not supported on this pin."); }
			if (!Pin.CheckPin(SelectedPin, BeagleBone.Peripherals)) { throw new InvalidOperationException("This pin cannot be used without disabling some peripherals first."); }
			if (Pin.GetOffset(SelectedPin) == 0x000) { throw new InvalidOperationException("This pin is not valid for device tree registration."); }

			if (PWMMappings != null && PWMMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for GPIO."); }
			if (I2CMappings != null && I2CMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for GPIO."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for GPIO."); }
			if (CANMappings != null && CANMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for GPIO."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for GPIO."); }

			if (GPIOMappings == null) { GPIOMappings = new Dictionary<BBBPin, PinAssignment>(); }
			PinAssignment NewMap = new PinAssignment(SelectedPin, Pin.GetPinMode(FastSlew, !IsOutput, Resistor, Mode));
			lock (GPIOMappings)
			{
				if (GPIOMappings.ContainsKey(SelectedPin))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding GPIO pin setting. This may mean that you have a pin usage conflict.");
					GPIOMappings[SelectedPin] = NewMap;
				}
				else { GPIOMappings.Add(SelectedPin, NewMap); }
			}
		}

		/// <summary>
		/// Adds a PWM mapping to the BBB device tree overlay, preparing the pin for use in hardware & kernel.
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="SelectedPin"> The pin to assign as a PWM output. Must be one of the PWM-capable pins. See the BBB chart in OneNote. </param>
		/// <exception cref="InvalidOperationException"> If this pin cannot be used as a PWM output at this time. Reason will be given. </exception>
		public static void AddMappingPWM(BBBPin SelectedPin)
		{
			byte Mode = Pin.GetModeID(SelectedPin, BBBPinMode.PWM);
			if (Mode == 255) { throw new InvalidOperationException("This pin cannot be used for PWM output."); }
			if (!Pin.CheckPin(SelectedPin, BeagleBone.Peripherals)) { throw new InvalidOperationException("This pin cannot be used for PWM without disabling some peripherals first."); }
			if (Pin.GetOffset(SelectedPin) == 0x000) { throw new InvalidOperationException("This pin is not valid for device tree registration."); }

			if (GPIOMappings != null && GPIOMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for PWM."); }
			if (I2CMappings != null && I2CMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for PWM."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for PWM."); }
			if (CANMappings != null && CANMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for PWM."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for PWM."); }

			switch (PWMBBB.PinToPWMID(SelectedPin))
			{
				case BBBCSIO.PWMPortEnum.PWM0_A: case BBBCSIO.PWMPortEnum.PWM0_B: EnablePWMBuses[0] = true; break;
				case BBBCSIO.PWMPortEnum.PWM1_A: case BBBCSIO.PWMPortEnum.PWM1_B: EnablePWMBuses[1] = true; break;
				case BBBCSIO.PWMPortEnum.PWM2_A: case BBBCSIO.PWMPortEnum.PWM2_B: EnablePWMBuses[2] = true; break;
			}

			if (PWMMappings == null) { PWMMappings = new Dictionary<BBBPin, PinAssignment>(); }
			PinAssignment NewMap = new PinAssignment(SelectedPin, Mode);
			lock (PWMMappings)
			{
				if (PWMMappings.ContainsKey(SelectedPin))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding PWM pin setting. This may mean that you have a pin usage conflict.");
					PWMMappings[SelectedPin] = NewMap;
				}
				else { PWMMappings.Add(SelectedPin, NewMap); }
			}
		}

		/// <summary>
		/// Adds an I2C mapping to the BBB device tree overlay, preparing the pins for use in hardware & kernel.
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="ClockPin"> The pin to use for the I2C clock signal. </param>
		/// <param name="DataPin"> The pin to use for the I2C data signal. </param>
		/// <exception cref="InvalidOperationException"> If one of the given pins cannot be used for the given I2C task at this time. Reason will be given. </exception>
		public static void AddMappingsI2C(BBBPin ClockPin, BBBPin DataPin)
		{
			byte ClockMode = Pin.GetModeID(ClockPin, BBBPinMode.I2C);
			byte DataMode = Pin.GetModeID(DataPin, BBBPinMode.I2C);
			if (ClockMode == 255) { throw new InvalidOperationException("This pin cannot be used for I2C clock line."); }
			if (DataMode == 255) { throw new InvalidOperationException("This pin cannot be used for I2C data line."); }
			if (!Pin.CheckPin(ClockPin, BeagleBone.Peripherals)) { throw new InvalidOperationException("I2C clock pin cannot be used without disabling some peripherals first."); }
			if (!Pin.CheckPin(DataPin, BeagleBone.Peripherals)) { throw new InvalidOperationException("I2C data pin cannot be used without disabling some peripherals first."); }
			if (Pin.GetOffset(ClockPin) == 0x000) { throw new InvalidOperationException("I2C clock pin is not valid for device tree registration."); }
			if (Pin.GetOffset(DataPin) == 0x000) { throw new InvalidOperationException("I2C data pin is not valid for device tree registration."); }

			if (ClockPin == BBBPin.P9_17 || ClockPin == BBBPin.P9_24) // Port 1 SCL
			{
				if (DataPin != BBBPin.P9_18 && DataPin != BBBPin.P9_26) // Not Port 1 SDA
				{
					throw new InvalidOperationException("I2C SDA pin selected is invalid with the selected SCL pin. Make sure that it is part of the same I2C port.");
				}
			}
			else if (ClockPin == BBBPin.P9_19 || ClockPin == BBBPin.P9_21) // Port 2 SCL
			{
				if (DataPin != BBBPin.P9_20 && DataPin != BBBPin.P9_22) // Not Port 2 SDA
				{
					throw new InvalidOperationException("I2C SDA pin selected is invalid with the selected SCL pin. Make sure that it is part of the same I2C port.");
				}
			}
			else { throw new InvalidOperationException("Given pin is not a possible I2C SCL pin. Check the documentation for pinouts."); }

			if (GPIOMappings != null && GPIOMappings.ContainsKey(ClockPin)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for I2C Clock."); }
			if (GPIOMappings != null && GPIOMappings.ContainsKey(DataPin)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for I2C Data."); }
			if (PWMMappings != null && PWMMappings.ContainsKey(ClockPin)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for I2C Clock."); }
			if (PWMMappings != null && PWMMappings.ContainsKey(DataPin)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for I2C Data."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(ClockPin)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for I2C Clock."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(DataPin)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for I2C Data."); }
			if (CANMappings != null && CANMappings.ContainsKey(ClockPin)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for I2C Clock."); }
			if (CANMappings != null && CANMappings.ContainsKey(DataPin)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for I2C Data."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(ClockPin)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for I2C Clock."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(DataPin)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for I2C Data."); }

			if (ClockPin == BBBPin.P9_17 || ClockPin == BBBPin.P9_24) { EnableI2CBuses[0] = true; }
			if (ClockPin == BBBPin.P9_19 || ClockPin == BBBPin.P9_21) { EnableI2CBuses[1] = true; }

			if (I2CMappings == null) { I2CMappings = new Dictionary<BBBPin, PinAssignment>(); }
			PinAssignment ClockMap = new PinAssignment(ClockPin, Pin.GetPinMode(false, true, ResistorState.PULL_UP, ClockMode));
			PinAssignment DataMap = new PinAssignment(DataPin, Pin.GetPinMode(false, true, ResistorState.PULL_UP, DataMode));
			lock (I2CMappings)
			{
				if (I2CMappings.ContainsKey(ClockPin))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding I2C SCL pin setting. This may mean that you have a pin usage conflict.");
					I2CMappings[ClockPin] = ClockMap;
				}
				else { I2CMappings.Add(ClockPin, ClockMap); }

				if (I2CMappings.ContainsKey(DataPin))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding I2C SDA pin setting. This may mean that you have a pin usage conflict.");
					I2CMappings[DataPin] = DataMap;
				}
				else { I2CMappings.Add(DataPin, DataMap); }
			}
		}

		/// <summary>
		/// Adds an SPI mapping to the BBB device tree overlay, preparing the pins for use in hardware & kernel.
		/// To add Slave-/Chip- select lines, use AddMappingSPI_CS().
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="MISO"> The pin to use as Master-In, Slave-Out data line. Can be BBBPin.NONE if you don't need communication in this direction. </param>
		/// <param name="MOSI"> The pin to use as Master-Out, Slave-In data line. Can be BBBPin.NONE if you don't need communication in this direction. </param>
		/// <param name="Clock"> The pin to use as the SPI clock line. </param>
		/// <exception cref="InvalidOperationException"> If one of the given pins cannot be used for the given SPI task at this time. Reason will be given. </exception>
		public static void AddMappingsSPI(BBBPin MISO, BBBPin MOSI, BBBPin Clock)
		{
			byte ClockMode = Pin.GetModeID(Clock, BBBPinMode.SPI);
			byte MISOMode = 255;
			byte MOSIMode = 255;
			if (ClockMode == 255) { throw new InvalidOperationException("This pin cannot be used for SPI clock."); }
			if (!Pin.CheckPin(Clock, BeagleBone.Peripherals)) { throw new InvalidOperationException("SPI pin cannot be used without disabling some peripherals first."); }
			if (Pin.GetOffset(Clock) == 0x000) { throw new InvalidOperationException("SPI pin is not valid for device tree registration."); }

			if (MISO == BBBPin.NONE && MOSI == BBBPin.NONE) { throw new InvalidOperationException("You must set either MOSI or MISO, or both."); }

			if (MISO != BBBPin.NONE)
			{
				MISOMode = Pin.GetModeID(MISO, BBBPinMode.SPI);
				if (MISOMode == 255) { throw new InvalidOperationException("This pin cannot be used for SPI MISO."); }
				if (!Pin.CheckPin(MISO, BeagleBone.Peripherals)) { throw new InvalidOperationException("SPI pin cannot be used without disabling some peripherals first."); }
				if (Pin.GetOffset(MISO) == 0x000) { throw new InvalidOperationException("SPI pin is not valid for device tree registration."); }
			}
			if (MOSI != BBBPin.NONE)
			{
				MOSIMode = Pin.GetModeID(MOSI, BBBPinMode.SPI);
				if (MOSIMode == 255) { throw new InvalidOperationException("This pin cannot be used for SPI MOSI."); }
				if (!Pin.CheckPin(MOSI, BeagleBone.Peripherals)) { throw new InvalidOperationException("SPI pin cannot be used without disabling some peripherals first."); }
				if (Pin.GetOffset(MOSI) == 0x000) { throw new InvalidOperationException("SPI pin is not valid for device tree registration."); }
			}

			if (GPIOMappings != null && MISO != BBBPin.NONE && GPIOMappings.ContainsKey(MISO)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for SPI MISO."); }
			if (GPIOMappings != null && MOSI != BBBPin.NONE && GPIOMappings.ContainsKey(MOSI)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for SPI MOSI."); }
			if (GPIOMappings != null && GPIOMappings.ContainsKey(Clock)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for SPI Clock."); }
			if (PWMMappings != null && MISO != BBBPin.NONE && PWMMappings.ContainsKey(MISO)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for SPI MISO."); }
			if (PWMMappings != null && MOSI != BBBPin.NONE && PWMMappings.ContainsKey(MOSI)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for SPI MOSI."); }
			if (PWMMappings != null && PWMMappings.ContainsKey(Clock)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for SPI Clock."); }
			if (I2CMappings != null && MISO != BBBPin.NONE && I2CMappings.ContainsKey(MISO)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for SPI MISO."); }
			if (I2CMappings != null && MOSI != BBBPin.NONE && I2CMappings.ContainsKey(MOSI)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for SPI MOSI."); }
			if (I2CMappings != null && I2CMappings.ContainsKey(Clock)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for SPI Clock."); }
			if (CANMappings != null && MISO != BBBPin.NONE && CANMappings.ContainsKey(MISO)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for SPI MISO."); }
			if (CANMappings != null && MOSI != BBBPin.NONE && CANMappings.ContainsKey(MOSI)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for SPI MOSI."); }
			if (CANMappings != null && CANMappings.ContainsKey(Clock)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for SPI Clock."); }
			if (UARTMappings != null && MISO != BBBPin.NONE && UARTMappings.ContainsKey(MISO)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for SPI MISO."); }
			if (UARTMappings != null && MOSI != BBBPin.NONE && UARTMappings.ContainsKey(MOSI)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for SPI MOSI."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(Clock)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for SPI Clock."); }

			if (Clock == BBBPin.P9_22) // Port 0
			{
				if (MISO != BBBPin.NONE && MISO != BBBPin.P9_21) { throw new InvalidOperationException("MISO pin selected is invalid with the selected clock pin. Make sure that they are part of the same SPI port."); }
				if (MOSI != BBBPin.NONE && MOSI != BBBPin.P9_18) { throw new InvalidOperationException("MOSI pin selected is invalid with the selected clock pin. Make sure that they are part of the same SPI port."); }

			}
			else if (Clock == BBBPin.P9_31 || Clock == BBBPin.P9_42) // Port 1
			{
				if (MISO != BBBPin.NONE && MISO != BBBPin.P9_29) { throw new InvalidOperationException("MISO pin selected is invalid with the selected clock pin. Make sure that they are part of the same SPI port."); }
				if (MOSI != BBBPin.NONE && MOSI != BBBPin.P9_30) { throw new InvalidOperationException("MOSI pin selected is invalid with the selected clock pin. Make sure that they are part of the same SPI port."); }
			}
			else { throw new InvalidOperationException("SPI Clock pin selected is invalid. Make sure MISO, MOSI, and clock are valid selections and part of the same port."); }

			if (SPIMappings == null) { SPIMappings = new Dictionary<BBBPin, PinAssignment>(); }
			PinAssignment ClockMap = new PinAssignment(Clock, Pin.GetPinMode(true, true, ResistorState.PULL_UP, ClockMode));
			PinAssignment MOSIMap = null;
			PinAssignment MISOMap = null;
			if (MOSI != BBBPin.NONE) { MOSIMap = new PinAssignment(MOSI, Pin.GetPinMode(true, false, ResistorState.PULL_UP, MOSIMode)); }
			if (MISO != BBBPin.NONE) { MISOMap = new PinAssignment(MISO, Pin.GetPinMode(true, true, ResistorState.PULL_UP, MISOMode)); }

			if (Clock == BBBPin.P9_22) { EnableSPIBuses[0] = true; }
			if (Clock == BBBPin.P9_31 || Clock == BBBPin.P9_42) { EnableSPIBuses[1] = true; }

			lock (SPIMappings)
			{
				if (SPIMappings.ContainsKey(Clock))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding SPI clock pin setting. This may mean that you have a pin usage conflict.");
					SPIMappings[Clock] = ClockMap;
				}
				else { SPIMappings.Add(Clock, ClockMap); }

				if (MISO != BBBPin.NONE)
				{
					if (SPIMappings.ContainsKey(MISO))
					{
						Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding SPI MISO pin setting. This may mean that you have a pin usage conflict.");
						SPIMappings[MISO] = MISOMap;
					}
					else { SPIMappings.Add(MISO, MISOMap); }
				}

				if (MOSI != BBBPin.NONE)
				{
					if (SPIMappings.ContainsKey(MOSI))
					{
						Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding SPI MOSI pin setting. This may mean that you have a pin usage conflict.");
						SPIMappings[MOSI] = MOSIMap;
					}
					else { SPIMappings.Add(MOSI, MOSIMap); }
				}
			}
		}

		/// <summary>
		/// Adds an SPI chip-select mapping to the BBB device tree overlay, preparing the pin for use in hardware & kernel.
		/// To prepare the data/clock lines, use AddMappingsSPI().
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="ChipSelect"> The pin to prepare for use as a chip-select line. It will be treated like a GPIO output, and the pull-up will be enabled. </param>
		/// <exception cref="InvalidOperationException"> If the given pin cannot be used for chip-select at this time. Reason will be given. </exception>
		public static void AddMappingSPI_CS(BBBPin ChipSelect)
		{
			AddMappingGPIO(ChipSelect, true, ResistorState.PULL_UP);
		}

		/// <summary>
		/// Adds a CAN bus mapping to the BBB device tree overlay, preparing the pin for use in hardware & kernel.
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="TX"> The pin to use for the transmit line. </param>
		/// <param name="RX"> The pin to use for the receive line. </param>
		/// <exception cref="InvalidOperationException"> If one of the given pins cannot be used for CAN at this time. Reason will be given. </exception>
		public static void AddMappingsCAN(BBBPin TX, BBBPin RX)
		{
			byte TXMode = Pin.GetModeID(TX, BBBPinMode.CAN);
			byte RXMode = Pin.GetModeID(RX, BBBPinMode.CAN);
			if (TXMode == 255) { throw new InvalidOperationException("This pin cannot be used for CAN TX line."); }
			if (RXMode == 255) { throw new InvalidOperationException("This pin cannot be used for CAN RX line."); }
			if (!Pin.CheckPin(TX, BeagleBone.Peripherals)) { throw new InvalidOperationException("CAN TX pin cannot be used without disabling some peripherals first."); }
			if (!Pin.CheckPin(RX, BeagleBone.Peripherals)) { throw new InvalidOperationException("CAN RX pin cannot be used without disabling some peripherals first."); }
			if (Pin.GetOffset(TX) == 0x000) { throw new InvalidOperationException("CAN TX pin is not valid for device tree registration."); }
			if (Pin.GetOffset(RX) == 0x000) { throw new InvalidOperationException("CAN RX pin is not valid for device tree registration."); }
			if (TX == BBBPin.P9_20)
			{
				if (RX != BBBPin.P9_19) { throw new InvalidOperationException("CAN RX pin selected is invalid with the selected TX pin. Make sure that it is part of the same CAN Bus."); }
			}
			else if (TX == BBBPin.P9_26)
			{
				if (RX != BBBPin.P9_24) { throw new InvalidOperationException("CAN RX pin selected is invalid with the selected TX pin. Make sure that it is part of the same CAN Bus."); }
			}
			else { throw new InvalidOperationException("Given pin is not a possible CAN pin. Check the documentation for pinouts."); }

			if (GPIOMappings != null && GPIOMappings.ContainsKey(TX)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for CAN TX."); }
			if (GPIOMappings != null && GPIOMappings.ContainsKey(RX)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for CAN RX."); }
			if (PWMMappings != null && PWMMappings.ContainsKey(TX)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for CAN TX."); }
			if (PWMMappings != null && PWMMappings.ContainsKey(RX)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for CAN RX."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(TX)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for CAN TX."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(RX)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for CAN RX."); }
			if (I2CMappings != null && I2CMappings.ContainsKey(TX)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for CAN TX."); }
			if (I2CMappings != null && I2CMappings.ContainsKey(RX)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for CAN RX."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(TX)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for CAN TX."); }
			if (UARTMappings != null && UARTMappings.ContainsKey(RX)) { throw new InvalidOperationException("This pin is already registered as UART, cannot also use for CAN RX."); }

			switch (CANBBB.PinToCANBus(TX))
			{
				case 0: EnableCANBuses[0] = true; break;
				case 1: EnableCANBuses[1] = true; break;
			}

			if (CANMappings == null) { CANMappings = new Dictionary<BBBPin, PinAssignment>(); }
			PinAssignment TXMap = new PinAssignment(TX, Pin.GetPinMode(true, false, ResistorState.PULL_UP, TXMode));
			PinAssignment RXMap = new PinAssignment(RX, Pin.GetPinMode(true, true, ResistorState.PULL_UP, RXMode));

			lock (CANMappings)
			{
				if (CANMappings.ContainsKey(TX))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding CAN TX pin setting. This may mean that you have a pin usage conflict.");
					CANMappings[TX] = TXMap;
				}
				else { CANMappings.Add(TX, TXMap); }

				if (CANMappings.ContainsKey(RX))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding CAN RX pin setting. This may mean that you have a pin usage conflict.");
					CANMappings[RX] = RXMap;
				}
				else { CANMappings.Add(RX, RXMap); }
			}
		}

		/// <summary>
		/// Adds a UART mapping to the BBB device tree overlay, preparing the pin for use in hardware & kernel.
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="SelectedPin"> The pin to use for the UART pin (RX or TX). </param>
		/// <exception cref="InvalidOperationException"> If the given pin cannot be used for UART at this time. Reason will be given. </exception>
		public static void AddMappingUART(BBBPin SelectedPin)
		{
			byte Mode = Pin.GetModeID(SelectedPin, BBBPinMode.UART);
			if (Mode == 255) { throw new InvalidOperationException("This type of output is not supported on this pin."); }
			if (!Pin.CheckPin(SelectedPin, BeagleBone.Peripherals)) { throw new InvalidOperationException("This pin cannot be used without disabling some peripherals first."); }
			if (Pin.GetOffset(SelectedPin) == 0x000) { throw new InvalidOperationException("This pin is not valid for device tree registration."); }

			if (GPIOMappings != null && GPIOMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as GPIO, cannot also use for UART."); }
			if (PWMMappings != null && PWMMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as PWM, cannot also use for UART."); }
			if (I2CMappings != null && I2CMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as I2C, cannot also use for UART."); }
			if (SPIMappings != null && SPIMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as SPI, cannot also use for UART."); }
			if (CANMappings != null && CANMappings.ContainsKey(SelectedPin)) { throw new InvalidOperationException("This pin is already registered as CAN, cannot also use for UART."); }

			byte Bus = UARTBBB.PinToUARTBus(SelectedPin);
			if (Bus == 255) { throw new InvalidOperationException("This pin is not valid for UART."); }
			EnableUARTBuses[Bus - 1] = true;

			if (UARTMappings == null) { UARTMappings = new Dictionary<BBBPin, PinAssignment>(); }
			PinAssignment NewMap = new PinAssignment(SelectedPin, Pin.GetPinMode(true, !UARTBBB.PinIsTX(SelectedPin), ResistorState.PULL_DOWN, Mode));
			lock (UARTMappings)
			{
				if (UARTMappings.ContainsKey(SelectedPin))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding UART pin setting. This may mean that you have a pin usage conflict.");
					UARTMappings[SelectedPin] = NewMap;
				}
				else { UARTMappings.Add(SelectedPin, NewMap); }
			}
		}

		/// <summary>
		/// Adds an ADC mapping to the BBB device tree overlay, preparing the pin for use in hardware & kernel.
		/// You'll need to call ApplyPinSettings() to actually apply the device tree overlay. Please read the OneNote documentation regarding this, as it is a complex process.
		/// </summary>
		/// <param name="SelectedPin"> The pin to prepare for use as ADC input. </param>
		/// <exception cref="InvalidOperationException"> If the given pin cannot be used as an ADC input at this time. Reason will be given. </exception>
		public static void AddMappingADC(BBBPin SelectedPin)
		{
			int ADCNum = -1;
			switch (SelectedPin)
			{
				case BBBPin.P9_39: ADCNum = 0; break;
				case BBBPin.P9_40: ADCNum = 1; break;
				case BBBPin.P9_37: ADCNum = 2; break;
				case BBBPin.P9_38: ADCNum = 3; break;
				case BBBPin.P9_33: ADCNum = 4; break;
				case BBBPin.P9_36: ADCNum = 5; break;
				case BBBPin.P9_35: ADCNum = 6; break;
				default: throw new InvalidOperationException("This pin is not an ADC pin. Cannot be registered for ADC use.");
			}
			if (ADCMappings == null) { ADCMappings = new Dictionary<BBBPin, int>(); }
			lock (ADCMappings)
			{
				if (ADCMappings.ContainsKey(SelectedPin))
				{
					Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Overriding ADC pin setting. This may mean you have a pin usage conflict.");
					ADCMappings[SelectedPin] = ADCNum;
				}
				else { ADCMappings.Add(SelectedPin, ADCNum); }
			}
		}
		#endregion

		public enum ApplicationMode { NO_CHANGES, APPLY_IF_NONE, REMOVE_AND_APPLY, APPLY_REGARDLESS }

		/// <summary>
		/// Generates the device tree file, compiles it, and instructs the kernel to load the overlay though the cape manager. May take a while. Should only be run once per program execution.
		/// We recommend you only do this once per BBB OS reboot, as removing the device tree overlay can cause serious issues. Please read the OneNote page for more info.
		/// </summary>
		/// <param name="Mode">
		/// The behaviour to use when determining what to do during application of the overlay. Please read the OneNote documentation for a more thorough explanation.
		/// NO_CHANGES: Does not apply the device tree overlay regardles
		/// APPLY_IF_NONE: Apply the overlay only if there is no Scarlet overlay already applied.
		/// REMOVE_AND_APPLY: Removes old Scarlet overlays, and applies the new one.
		/// APPLY_REGARDLESS: Blindly applies the current overlay, regardless of current state.
		/// </param>
		public static void ApplyPinSettings(ApplicationMode Mode)
		{
			// Generate the device tree
			if ((GPIOMappings == null || GPIOMappings.Count == 0) &&
			   (PWMMappings == null || PWMMappings.Count == 0) &&
			   (I2CMappings == null || I2CMappings.Count == 0) &&
			   (SPIMappings == null || SPIMappings.Count == 0) &&
			   (CANMappings == null || CANMappings.Count == 0) &&
			   (UARTMappings == null || UARTMappings.Count == 0) &&
			   (ADCMappings == null || ADCMappings.Count == 0))
			{ Log.Output(Log.Severity.INFO, Log.Source.HARDWAREIO, "No pins defined, skipping device tree application."); return; }
			if (!StateStore.Started) { throw new Exception("Please start the StateStore system first."); }
			string FileName = "Scarlet-DT";
			string PrevNum = StateStore.GetOrCreate("Scarlet-DevTreeNum", "0");
			string PrevHash = StateStore.GetOrCreate("Scarlet-DevTreeHash", "NONE");

			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Generating device tree...");
			List<string> DeviceTree = GenerateDeviceTree();
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Last run was #" + PrevNum + ", hash " + PrevHash);
			bool New = PrevHash != DeviceTree.GetHashCode().ToString();
			if (New) { StateStore.Set("Scarlet-DevTreeNum", (int.Parse(PrevNum) + 1).ToString()); }
			FileName += StateStore.Get("Scarlet-DevTreeNum");
			StateStore.Set("Scarlet-DevTreeHash", DeviceTree.GetHashCode().ToString());
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Now on " + FileName);
			StateStore.Save();
			string OutputDTFile = FileName + ".dts";

			// Save the device tree to file
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Saving to DTS file...");
			File.WriteAllLines(OutputDTFile, DeviceTree);
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "DTS file saved.");

			bool AttemptOverlayChanges = false;
			bool WarnAboutApplication = false;
			switch (Mode)
			{
				case ApplicationMode.APPLY_IF_NONE:
					AttemptOverlayChanges = FindScarletOverlays().Count == 0;
					break;
				case ApplicationMode.APPLY_REGARDLESS:
					AttemptOverlayChanges = true;
					New = true;
					break;
				case ApplicationMode.REMOVE_AND_APPLY:
					RemovePinSettings();
					AttemptOverlayChanges = true;
					break;
				case ApplicationMode.NO_CHANGES:
					if (FindScarletOverlays().Count > 0) { WarnAboutApplication = true; }
					break;
			}

			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "AttemptChanges: " + AttemptOverlayChanges + ", new: " + New);
			if (AttemptOverlayChanges)
			{
				if (New)
				{
					// Compile the device tree file
					// Command: dtc -O dtb -o Scarlet-DT-00A0.dtbo -b 0 -@ Scarlet-DT.dts
					string CompiledDTFile = FileName + "-00A0.dtbo";
					Process Compile = new Process();
					Compile.StartInfo.FileName = "dtc";
					Compile.StartInfo.Arguments = "-O dtb -o \"" + CompiledDTFile + "\" -b 0 -@ \"" + OutputDTFile + "\"";
					Log.Output(Log.Severity.INFO, Log.Source.HARDWAREIO, "Compiling device tree...");
					Compile.Start();
					Compile.WaitForExit();
					Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Compilation done.");

					// Remove previous device tree
					RemovePinSettings();

					// Copy the compiled file to the firmware folder, removing the existing one
					// Command: cp Scarlet-DT-00A0.dtbo /lib/firmware
					Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Copying compile tree to lib folder...");
					if (!File.Exists(CompiledDTFile)) { throw new FileNotFoundException("Failed to get compiled device tree!"); }
					if (File.Exists("/lib/firmware/" + CompiledDTFile)) { File.Delete("/lib/firmware/" + CompiledDTFile); }
					File.Copy(CompiledDTFile, "/lib/firmware/" + CompiledDTFile, true);
					Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Copying done.");

					// Delete the compiled tree file in execution folder
					File.Delete(CompiledDTFile);
				}
				// Apply the device tree
				// Command: echo Scarlet-DT > /sys/devices/platform/bone_capemgr/slots
				try
				{
					Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Applying device tree overlay...");
					using (StreamWriter SlotWriter = File.AppendText("/sys/devices/platform/bone_capemgr/slots"))
					{
						SlotWriter.WriteLine(FileName);
						SlotWriter.Flush();
					}
					Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Done applying.");
				}
				catch (IOException Exc)
				{
					Log.Output(Log.Severity.ERROR, Log.Source.HARDWAREIO, "Failed to apply device tree overlay. This is likely caused by a conflict. Please read 'Common Issues' in the documentation.");
					throw;
				}

				Thread.Sleep(100);
			}
			if (WarnAboutApplication) { Log.Output(Log.Severity.WARNING, Log.Source.HARDWAREIO, "Scarlet device tree overlays have not been applied. Ensure that this is what you intended, otherwise I/O pins may not work as expected."); }

			// Start relevant components.
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Initializing components...");
			I2CBBB.Initialize(EnableI2CBuses);
			SPIBBB.Initialize(EnableSPIBuses);
			PWMBBB.Initialize(EnablePWMBuses);
			CANBBB.Initialize(EnableCANBuses, new bool[] { true, false });
			UARTBBB.Initialize(EnableUARTBuses);
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "BBB ready!");
		}

		/// <summary>
		/// Unloads all Scarlet device tree overlays.
		/// NOTE: This has a high probability of causing instability, issues, and possibly even a kernel panic. Only do this if it is really necessary.
		/// </summary>
		private static void RemovePinSettings()
		{
			List<int> ToRemove = FindScarletOverlays();
			Log.Output(Log.Severity.DEBUG, Log.Source.HARDWAREIO, "Removing overlays: " + ToRemove);
			StreamWriter SlotManager = File.AppendText("/sys/devices/platform/bone_capemgr/slots");
			// Command: echo -[NUM] > /sys/devices/platform/bone_capemgr/slots
			ToRemove.ForEach(Num => SlotManager.Write('-' + Num + Environment.NewLine));
			SlotManager.Flush();
			SlotManager.Close();
		}

		private static List<int> FindScarletOverlays()
		{
			// Command: cat /sys/devices/platform/bone_capemgr/slots
			string[] Overlays = File.ReadAllLines("/sys/devices/platform/bone_capemgr/slots");
			List<int> ToRemove = new List<int>();
			foreach (string Overlay in Overlays)
			{
				if (Overlay.Contains("Scarlet-DT"))
				{
					ToRemove.Add(int.Parse(Overlay.Substring(0, Overlay.IndexOf(":"))));
				}
			}
			return ToRemove;
		}

		private class PinAssignment
		{
			public BBBPin Pin { get; private set; }
			public byte Mode { get; private set; }

			public PinAssignment(BBBPin Pin, byte Mode)
			{
				this.Pin = Pin;
				this.Mode = Mode;
			}
		}

		/// <summary> Uses all of the previously created device tree overlay mappings (using the AddMapping___() functions), and generates a device tree overlay for application via the cape manager. </summary>
		/// <remarks> This is probably the longest function I've ever written. It's reasonably simple to follwo though, there's just a lot of output it needs to be able to generate. </remarks>
		/// <returns> A list of lines to save to a file, which can then be compiled into a DTBO and applied as an overlay. </returns>
		/// Fragment IDs:
		/// GPIO: 0, 1
		/// PWM: 2, 10, 11, 12, 13, 14, 15
		/// I2C: 3, 20, 21
		/// SPI: 4, 30, 31
		/// ADC: 5
		/// CAN: 6, 40, 41
		/// UART: 7, 50, 51, 52, 53
		static List<string> GenerateDeviceTree()
		{
			List<string> Output = new List<string>();

			Output.Add("/dts-v1/;");
			Output.Add("/plugin/;");
			Output.Add("");
			Output.Add("/ {");
			Output.Add("    /* Generated by Scarlet. */");
			Output.Add("    compatible = \"ti,beaglebone\", \"ti,beaglebone-black\";");
			Output.Add("    part-number = \"scarlet-pins\";");
			Output.Add("    version = \"00A0\";");
			Output.Add("    ");

			List<string> ExclusiveUseList = new List<string>();

			Dictionary<BBBPin, PinAssignment> PWMDev0 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> PWMDev1 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> PWMDev2 = new Dictionary<BBBPin, PinAssignment>();

			Dictionary<BBBPin, PinAssignment> I2CDev1 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> I2CDev2 = new Dictionary<BBBPin, PinAssignment>();

			Dictionary<BBBPin, PinAssignment> SPIDev0 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> SPIDev1 = new Dictionary<BBBPin, PinAssignment>();

			Dictionary<BBBPin, PinAssignment> CANDev0 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> CANDev1 = new Dictionary<BBBPin, PinAssignment>();

			Dictionary<BBBPin, PinAssignment> UARTDev1 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> UARTDev2 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> UARTDev3 = new Dictionary<BBBPin, PinAssignment>();
			Dictionary<BBBPin, PinAssignment> UARTDev4 = new Dictionary<BBBPin, PinAssignment>();

			// Build device lists and create exclusive-use list
			if (PWMMappings != null)
			{
				lock (PWMMappings)
				{
					// Sort PWM pins into devices
					foreach (KeyValuePair<BBBPin, PinAssignment> Entry in PWMMappings)
					{
						switch (Entry.Key)
						{
							case BBBPin.P9_22:
							case BBBPin.P9_31: // 0_A
							case BBBPin.P9_21:
							case BBBPin.P9_29: // 0_B
								PWMDev0.Add(Entry.Key, Entry.Value); continue;

							case BBBPin.P9_14:
							case BBBPin.P8_36: // 1_A
							case BBBPin.P9_16:
							case BBBPin.P8_34: // 1_B
								PWMDev1.Add(Entry.Key, Entry.Value); continue;

							case BBBPin.P8_19:
							case BBBPin.P8_45: // 2_A
							case BBBPin.P8_13:
							case BBBPin.P8_46: // 2_B
								PWMDev2.Add(Entry.Key, Entry.Value); continue;
						}
					}
					// Add PWM pins to exclusive-use list
					if (PWMDev0.Count > 0)
					{
						ExclusiveUseList.Add("epwmss0");
						ExclusiveUseList.Add("ehrpwm0");
						foreach (PinAssignment OnePin in PWMDev0.Values)
						{
							string PinName = OnePin.Pin.ToString("F").Replace('_', '.');
							ExclusiveUseList.Add(PinName);
						}
					}
					if (PWMDev1.Count > 0)
					{
						ExclusiveUseList.Add("epwmss1");
						ExclusiveUseList.Add("ehrpwm1");
						foreach (PinAssignment OnePin in PWMDev1.Values)
						{
							string PinName = OnePin.Pin.ToString("F").Replace('_', '.');
							ExclusiveUseList.Add(PinName);
						}
					}
					if (PWMDev2.Count > 0)
					{
						ExclusiveUseList.Add("epwmss2");
						ExclusiveUseList.Add("ehrpwm2");
						foreach (PinAssignment OnePin in PWMDev2.Values)
						{
							string PinName = OnePin.Pin.ToString("F").Replace('_', '.');
							ExclusiveUseList.Add(PinName);
						}
					}
				}
			}

			if (I2CMappings != null)
			{
				lock (I2CMappings)
				{
					// Sort I2C pins into devices
					foreach (KeyValuePair<BBBPin, PinAssignment> Entry in I2CMappings)
					{
						switch (Entry.Key)
						{
							case BBBPin.P9_17:
							case BBBPin.P9_24: // 1_SCL
							case BBBPin.P9_18:
							case BBBPin.P9_26: // 1_SDA
								I2CDev1.Add(Entry.Key, Entry.Value); continue;

							case BBBPin.P9_19:
							case BBBPin.P9_21: // 2_SCL
							case BBBPin.P9_20:
							case BBBPin.P9_22: // 2_SDA
								I2CDev2.Add(Entry.Key, Entry.Value); continue;
						}
					}
					// Add I2C pins to exclusive-use list
					if (I2CDev1.Count > 0)
					{
						ExclusiveUseList.Add("i2c1");
						foreach (PinAssignment OnePin in I2CDev1.Values)
						{
							string PinName = OnePin.Pin.ToString("F").Replace('_', '.');
							ExclusiveUseList.Add(PinName);
						}
					}
					if (I2CDev2.Count > 0)
					{
						ExclusiveUseList.Add("i2c2");
						foreach (PinAssignment OnePin in I2CDev2.Values)
						{
							string PinName = OnePin.Pin.ToString("F").Replace('_', '.');
							ExclusiveUseList.Add(PinName);
						}
					}
				}
			}

			if (SPIMappings != null)
			{
				lock (SPIMappings)
				{
					// Sort SPI pins into devices
					foreach (KeyValuePair<BBBPin, PinAssignment> Entry in SPIMappings)
					{
						switch (Entry.Key)
						{
							case BBBPin.P9_22: // 0_CLK
							case BBBPin.P9_21: // 0_D0
							case BBBPin.P9_18: // 0_D1
							case BBBPin.P9_17: // 0_CS0
								SPIDev0.Add(Entry.Key, Entry.Value); continue;
							case BBBPin.P9_31:
							case BBBPin.P9_42: // 1_CLK
							case BBBPin.P9_29: // 1_D0
							case BBBPin.P9_30: // 1_D1
							case BBBPin.P9_20:
							case BBBPin.P9_28: // 1_CS0
							case BBBPin.P9_19: // 1_CS1
								SPIDev1.Add(Entry.Key, Entry.Value); continue;
						}
					}

					// Add SPI pins to the exclusive-use list
					// TODO: See if this is needed.
					// TODO: Yes it is. If the universal cape is loaded, our overlay will appear to load fine, but the pins won't function.
					//       To prevent this, we can make ours exclusive-use, which will throw an error during application, making the user fix the issue.
				}
			}

			if (CANMappings != null)
			{
				lock (CANMappings)
				{
					// Sort CAN pins into devices
					foreach (KeyValuePair<BBBPin, PinAssignment> Entry in CANMappings)
					{
						switch (CANBBB.PinToCANBus(Entry.Key))
						{
							case 0: CANDev0.Add(Entry.Key, Entry.Value); continue;
							case 1: CANDev1.Add(Entry.Key, Entry.Value); continue;
						}
					}
				}
			}

			if (UARTMappings != null)
			{
				lock (UARTMappings)
				{
					// Sort UART pins into devices
					foreach (KeyValuePair<BBBPin, PinAssignment> Entry in UARTMappings)
					{
						switch (UARTBBB.PinToUARTBus(Entry.Key))
						{
							case 1: UARTDev1.Add(Entry.Key, Entry.Value); continue;
							case 2: UARTDev2.Add(Entry.Key, Entry.Value); continue;
							case 3: UARTDev3.Add(Entry.Key, Entry.Value); continue;
							case 4: UARTDev4.Add(Entry.Key, Entry.Value); continue;
						}
					}
				}
			}

			// Output exclusive-use list
			if (ExclusiveUseList.Count > 0)
			{
				Output.Add("    exclusive-use =");
				for (int i = 0; i < ExclusiveUseList.Count; i++)
				{
					Output.Add("        \"" + ExclusiveUseList[i] + "\"" + ((i == ExclusiveUseList.Count - 1) ? ';' : ','));
				}
				Output.Add("    ");
			}

			// Output GPIO mappings
			if (GPIOMappings != null)
			{
				lock (GPIOMappings)
				{
					Output.Add("    fragment@0 {");
					Output.Add("        target = <&am33xx_pinmux>;");
					Output.Add("        __overlay__ {");
					Output.Add("            scarlet_pins: scarlet_pin_set {");
					Output.Add("                pinctrl-single,pins = <");

					foreach (PinAssignment PinAss in GPIOMappings.Values)
					{
						string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
						string Mode = String.Format("0x{0:X2}", PinAss.Mode);
						Output.Add("                    " + Offset + " " + Mode);
					}

					Output.Add("                >;");
					Output.Add("            };");
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");
					Output.Add("    fragment@1 {");
					Output.Add("        target = <&ocp>;");
					Output.Add("        __overlay__ {");
					Output.Add("            scarlet_pinmux: scarlet {");
					Output.Add("                compatible = \"bone-pinmux-helper\";");
					Output.Add("                pinctrl-names = \"default\";");
					Output.Add("                pinctrl-0 = <&scarlet_pins>;");
					Output.Add("                status = \"okay\";");
					Output.Add("            };");
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");
				}
			}

			// Output PWM device fragments
			if (PWMMappings != null)
			{
				lock (PWMMappings)
				{
					Output.Add("    fragment@2 {");
					Output.Add("        target = <&am33xx_pinmux>;");
					Output.Add("        __overlay__ {");
					if (PWMDev0.Count > 0)
					{
						Output.Add("            scarlet_pwm0_pins: pinmux_scarlet_pwm0_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in PWMDev0.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (PWMDev1.Count > 0)
					{
						Output.Add("            scarlet_pwm1_pins: pinmux_scarlet_pwm1_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in PWMDev1.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (PWMDev2.Count > 0)
					{
						Output.Add("            scarlet_pwm2_pins: pinmux_scarlet_pwm2_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in PWMDev2.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");

					if (PWMDev0.Count > 0)
					{
						Output.Add("    fragment@10 {");
						Output.Add("        target = <&epwmss0>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    fragment@11 {");
						Output.Add("        target = <&ehrpwm0>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_pwm0_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (PWMDev1.Count > 0)
					{
						Output.Add("    fragment@12 {");
						Output.Add("        target = <&epwmss1>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    fragment@13 {");
						Output.Add("        target = <&ehrpwm1>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_pwm1_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (PWMDev2.Count > 0)
					{
						Output.Add("    fragment@14 {");
						Output.Add("        target = <&epwmss2>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    fragment@15 {");
						Output.Add("        target = <&ehrpwm2>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_pwm2_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
				}
			}

			// Output I2C device fragments
			if (I2CMappings != null)
			{
				lock (I2CMappings)
				{
					Output.Add("    fragment@3 {");
					Output.Add("        target = <&am33xx_pinmux>;");
					Output.Add("        __overlay__ {");
					if (I2CDev1.Count > 0)
					{
						Output.Add("            bbb_i2c1_pins: pinmux_bbb_i2c1_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in I2CDev1.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (I2CDev2.Count > 0)
					{
						Output.Add("            bbb_i2c2_pins: pinmux_bbb_i2c2_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in I2CDev2.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");

					if (I2CDev1.Count > 0)
					{
						Output.Add("    fragment@20 {");
						Output.Add("        target = <&i2c1>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&bbb_i2c1_pins>;");
						Output.Add("            clock-frequency = <100000>;"); // TODO: Make this adjustable?
						Output.Add("            #address-cells = <1>;");
						Output.Add("            #size-cells = <0>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (I2CDev2.Count > 0)
					{
						Output.Add("    fragment@21 {");
						Output.Add("        target = <&i2c2>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&bbb_i2c2_pins>;");
						Output.Add("            clock-frequency = <100000>;"); // TODO: Make this adjustable?
						Output.Add("            #address-cells = <1>;");
						Output.Add("            #size-cells = <0>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
				}
			}

			// Output SPI device fragments
			if (SPIMappings != null)
			{
				lock (SPIMappings)
				{
					Output.Add("    fragment@4 {");
					Output.Add("        target = <&am33xx_pinmux>;");
					Output.Add("        __overlay__ {");
					if (SPIDev0.Count > 0)
					{
						Output.Add("            scarlet_spi0_pins: pinmux_scarlet_spi0_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in SPIDev0.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (SPIDev1.Count > 0)
					{
						Output.Add("            scarlet_spi1_pins: pinmux_scarlet_spi1_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in SPIDev1.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");

					if (SPIDev0.Count > 0)
					{
						Output.Add("    fragment@30 {");
						Output.Add("        target = <&spi0>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_spi0_pins>;");
						Output.Add("            ti,pio-mode;");
						Output.Add("            #address-cells = <1>;");
						Output.Add("            #size-cells = <0>;");
						Output.Add("            spidev@0 {");
						Output.Add("                spi-max-frequency = <24000000>;");
						Output.Add("                reg = <0>;");
						Output.Add("                compatible = \"spidev\";");
						Output.Add("                spi-cpha;");
						Output.Add("            };");
						Output.Add("            spidev@1 {");
						Output.Add("                spi-max-frequency = <24000000>;");
						Output.Add("                reg = <1>;");
						Output.Add("                compatible = \"spidev\";");
						Output.Add("            };");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (SPIDev1.Count > 0)
					{
						Output.Add("    fragment@31 {");
						Output.Add("        target = <&spi1>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_spi1_pins>;");
						Output.Add("            ti,pio-mode;");
						Output.Add("            #address-cells = <1>;");
						Output.Add("            #size-cells = <0>;");
						Output.Add("            spidev@2 {");
						Output.Add("                spi-max-frequency = <24000000>;");
						Output.Add("                reg = <0>;");
						Output.Add("                compatible = \"spidev\";");
						Output.Add("                spi-cpha;");
						Output.Add("            };");
						Output.Add("            spidev@3 {");
						Output.Add("                spi-max-frequency = <24000000>;");
						Output.Add("                reg = <1>;");
						Output.Add("                compatible = \"spidev\";");
						Output.Add("            };");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
				}
			}

			// Output ADC device fragment
			if (ADCMappings != null)
			{
				lock (ADCMappings)
				{
					SortedList Channels = new SortedList(7);
					ADCMappings.Values.ToList().ForEach(x => Channels.Add(x, x));

					string ChannelsOut = "<";
					for (int i = 0; i < Channels.Count; i++)
					{
						ChannelsOut += Channels.GetByIndex(i);
						if (i + 1 < Channels.Count) { ChannelsOut += " "; }
					}
					ChannelsOut += ">";

					Output.Add("    fragment@5 {");
					Output.Add("        target = <&tscadc>;");
					Output.Add("        __overlay__ {");
					Output.Add("            status = \"okay\";");
					Output.Add("            adc {");
					Output.Add("                ti,adc-channels = " + ChannelsOut + ";");
					Output.Add("                ti,chan-step-avg = <" + UtilMain.RepeatWithSeperator("0x16", " ", Channels.Count) + ">;");
					Output.Add("                ti,chan-step-opendelay = <" + UtilMain.RepeatWithSeperator("0x98", " ", Channels.Count) + ">;");
					Output.Add("                ti,chan-step-sampledelay = <" + UtilMain.RepeatWithSeperator("0x0", " ", Channels.Count) + ">;");
					Output.Add("            };");
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");
				}
			}

			// Output CAN device fragments
			if (CANMappings != null)
			{
				lock (CANMappings)
				{
					Output.Add("    fragment@6 {");
					Output.Add("        target = <&am33xx_pinmux>;");
					Output.Add("        __overlay__ {");
					if (CANDev0.Count > 0)
					{
						Output.Add("            scarlet_dcan0: pinmux_scarlet_dcan0_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in CANDev0.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (CANDev1.Count > 0)
					{
						Output.Add("            scarlet_dcan1: pinmux_scarlet_dcan1_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in CANDev1.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");

					if (CANDev0.Count > 0)
					{
						Output.Add("    fragment@40 {");
						Output.Add("        target = <&dcan0>;");
						Output.Add("        __overlay__ {");
						Output.Add("            #address-cells = <1>;");
						Output.Add("            #size-cells = <0>;");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_dcan0>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (CANDev1.Count > 0)
					{
						Output.Add("    fragment@41 {");
						Output.Add("        target = <&dcan1>;");
						Output.Add("        __overlay__ {");
						Output.Add("            #address-cells = <1>;");
						Output.Add("            #size-cells = <0>;");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_dcan1>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
				}
			}

			// Output UART device fragments
			if (UARTMappings != null)
			{
				lock (UARTMappings)
				{
					Output.Add("    fragment@7 {");
					Output.Add("        target = <&am33xx_pinmux>;");
					Output.Add("        __overlay__ {");
					if (UARTDev1.Count > 0)
					{
						Output.Add("            scarlet_uart1_pins: pinmux_scarlet_uart1_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in UARTDev1.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (UARTDev2.Count > 0)
					{
						Output.Add("            scarlet_uart2_pins: pinmux_scarlet_uart2_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in UARTDev2.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (UARTDev3.Count > 0)
					{
						Output.Add("            scarlet_uart3_pins: pinmux_scarlet_uart3_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in UARTDev3.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					if (UARTDev4.Count > 0)
					{
						Output.Add("            scarlet_uart4_pins: pinmux_scarlet_uart4_pins {");
						Output.Add("                pinctrl-single,pins = <");
						foreach (PinAssignment PinAss in UARTDev4.Values)
						{
							string Offset = String.Format("0x{0:X3}", (Pin.GetOffset(PinAss.Pin) - 0x800));
							string Mode = String.Format("0x{0:X2}", PinAss.Mode);
							Output.Add("                    " + Offset + " " + Mode);
						}
						Output.Add("                >;");
						Output.Add("            };");
					}
					Output.Add("        };");
					Output.Add("    };");
					Output.Add("    ");

					if (UARTDev1.Count > 0)
					{
						Output.Add("    fragment@50 {");
						Output.Add("        target = <&uart1>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_uart1_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (UARTDev2.Count > 0)
					{
						Output.Add("    fragment@51 {");
						Output.Add("        target = <&uart2>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_uart2_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (UARTDev3.Count > 0)
					{
						Output.Add("    fragment@52 {");
						Output.Add("        target = <&uart3>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_uart3_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
					if (UARTDev4.Count > 0)
					{
						Output.Add("    fragment@53 {");
						Output.Add("        target = <&uart4>;");
						Output.Add("        __overlay__ {");
						Output.Add("            status = \"okay\";");
						Output.Add("            pinctrl-names = \"default\";");
						Output.Add("            pinctrl-0 = <&scarlet_uart4_pins>;");
						Output.Add("        };");
						Output.Add("    };");
						Output.Add("    ");
					}
				}
			}

			Output.Add("};");
			return Output;
		}
	}