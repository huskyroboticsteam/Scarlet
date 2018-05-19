using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Scarlet.IO;
using Scarlet.Utilities;

namespace Scarlet.Components.Sensors
{
    /// <summary>
    /// Analogue Soil Moisture Sensor
    /// Data: http://vegetronix.com/Products/VH400/
    /// </summary>
    public class VH400 : ISensor
    {
        public string System { get; set; }
        private IAnalogueIn Input;
        private double LastReading;
        public double VoltageMultiplier;

        /// <summary> Prepares a VH400 soil moisture sensor for use. </summary>
        /// <param name="Input"> The analogue input where the sensor is connected. </param>
        /// <param name="VoltageMultiplier">
        /// If a resistor divider or other scaling is applied to the sensor's output, specify this parameter in order to scale the readings to the appropriate levels.
        /// For example, if there is a 3:1 divider, bringing the sensor's 0-3V output to a range of 0-1V on the IAnalogeIn device, then specify a multiplier of 3.
        /// This would be decided by the hardware configuration of the sensor and analogue input systems, and varies by implementation.
        /// </param>
        public VH400(IAnalogueIn Input, double VoltageMultiplier = 1)
        {
            this.Input = Input;
            this.VoltageMultiplier = VoltageMultiplier;
        }

        /// <summary> Gets the sensor's data in a standard format. </summary>
        public DataUnit GetData()
        {
            return new DataUnit("VH400")
            {
                { "RawReading", this.LastReading },
                { "Multiplier", this.VoltageMultiplier },
                { "VWC%", ToMoisture(this.LastReading) }
            }
            .SetSystem(this.System);
        }

        /// <summary> Checks to see if the current analogue input is within the output range of the sensor. </summary>
        public bool Test() => (this.LastReading >= 0 && this.LastReading <= 3);

        /// <summary> Gets a new reading from the sensor. </summary>
        public void UpdateState() { this.LastReading = this.Input.GetInput() * this.VoltageMultiplier; }

        /// <summary> Gets the volumetric water content in %, between 0 and 1. </summary>
        /// <returns> VWC %, or -1 if the sensor is not functioning correctly. </returns>
        public float GetReading() { return ToMoisture(this.LastReading); }

        /// <summary> Gets the sensor's output voltage (should be between 0 and 3V). </summary>
        public float GetVoltage() { return (float)this.LastReading; }

        /// <summary> Converts the sensor's voltage output to Volumetric water content (%) between 0 and 1. </summary>
        /// <remarks> Uses the piecewise curve taken from https://vegetronix.com/Products/VH400/VH400-Piecewise-Curve.phtml </remarks>
        /// <param name="RawReading"> The sensor's output voltage (0-3V) </param>
        /// <returns> Volumetric water content % (0-1) if valid input, -1 if invalid input. </returns>
        public static float ToMoisture(double RawReading)
        {
            if (RawReading < 0 || RawReading > 3) { return -1; } // Invalid input.
            if (RawReading < 1.1) { return (float)((0.1 * RawReading) - 0.01); }
            if (RawReading < 1.3) { return (float)((0.15 * RawReading) - 0.175); }
            if (RawReading < 1.82) { return (float)((0.4808 * RawReading) - 0.475); }
            if (RawReading < 2.2) { return (float)((0.2632 * RawReading) - 0.0789); }
            return (float)((0.625 * RawReading) - 0.875);
        }
    }
}
