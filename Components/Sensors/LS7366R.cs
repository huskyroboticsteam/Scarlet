﻿using System;
using Scarlet.IO;
using Scarlet.Utilities;

namespace Scarlet.Components.Sensors
{
    /// <summary>
    /// Implements the LS7366R encoder reader. 
    /// Datasheet here:
    /// https://cdn.usdigital.com/assets/general/LS7366R.pdf
    /// </summary>
    public class LS7366R : ISensor
    {
        public bool CountEnabled { get; private set; }
        public int Count { get; private set; }
        public string System { get; set; }
        public bool TraceLogging { get; set; }

        public event EventHandler<OverflowEvent> OverflowOccured;

        private const float LOAD_READ_DELAY = 0.02f; // seconds

        private IDigitalOut ChipSelect;
        private IDigitalOut CountEnable;
        private ISPIBus SPIBus;
        private volatile bool HadEvent;

        public static readonly Configuration DefaultConfig = new Configuration()
        {
            // MDR0
            QuadMode = QuadMode.NON_QUAD,
            CountMode = CountMode.FREE_RUNNING,
            IndexConfig = IndexConfig.DISABLE,

            SynchronousIndex = false,
            DivideClkBy2 = false,

            // MDR1
            CounterMode = CounterMode.BYTE_4,

            CountEnable = true,

            FlagOnIDX = false,
            FlagOnCMP = false,
            FlagOnBW = false,
            FlagOnCY = false,
        };

        /// <summary> Initializes the LS7366R SPI encoder counter chip. </summary>
        /// <param name="SPIBus"> SPI Bus to communicate with </param>
        /// <param name="ChipSelect"> Chip Select to use with SPI </param>
        /// <param name="CountEnable"> Digital Out to enable the counters </param>
        public LS7366R(ISPIBus SPIBus, IDigitalOut ChipSelect, IDigitalOut CountEnable = null)
        {
            this.ChipSelect = ChipSelect;
            this.CountEnable = CountEnable;
            this.SPIBus = SPIBus;
        }

        /// <summary> Configures the device given a configuration </summary>
        /// <param name="Configuration"> Configuration to use. </param>
        public void Configure(Configuration Configuration)
        {
            // Bools to bytes
            byte SynIndex = Configuration.SynchronousIndex ? (byte)1 : (byte)0;
            byte FltrClk = Configuration.DivideClkBy2 ? (byte)1 : (byte)0;

            byte CntEnable = Configuration.CountEnable ? (byte)0 : (byte)1;

            byte IDXFlg = Configuration.FlagOnIDX ? (byte)1 : (byte)0;
            byte CMPFlg = Configuration.FlagOnCMP ? (byte)1 : (byte)0;
            byte BWFlg = Configuration.FlagOnBW ? (byte)1 : (byte)0;
            byte CYFlg = Configuration.FlagOnCY ? (byte)1 : (byte)0;

            // Setup MDR0 byte
            byte MDR0 = (byte)Configuration.QuadMode;
            MDR0 |= (byte)((int)Configuration.CountMode << 2);
            MDR0 |= (byte)((int)Configuration.IndexConfig << 4);
            MDR0 |= (byte)(SynIndex << 6);
            MDR0 |= (byte)(FltrClk << 7);

            // Setup MDR1 byte
            byte MDR1 = (byte)Configuration.CounterMode;
            MDR1 |= (byte)(CntEnable << 2);
            MDR1 |= (byte)(IDXFlg << 4);
            MDR1 |= (byte)(CMPFlg << 5);
            MDR1 |= (byte)(BWFlg << 6);
            MDR1 |= (byte)(CYFlg << 7);

            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b00_001_000 }, 1); // Clear MDR0 to zero
            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b00_010_000 }, 1); // Clear MDR1 to zero
            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b10_001_000, MDR0 }, 2); // Write MDR0
            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b10_010_000, MDR1 }, 2); // Write MDR1
            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b00_100_000 }, 1); // Clear CNT to zero
        }

        /// <summary> Configures device with a default configuration </summary>
        public void Configure() => Configure(DefaultConfig);

        /// <summary>
        /// Sets the output of the given count enable output
        /// (if given) to the Enable state. 
        /// Sets CountEnabled to the given Enable state. 
        /// Triggers the internal Enable/Disable counting functionality
        /// </summary>
        /// <param name="Enable"> Whether or not to enable counting. </param>
        public void EnableCount(bool Enable)
        {
            this.CountEnable?.SetOutput(Enable);

            // Read MDR1
            byte MDR1 = this.SPIBus.Write(this.ChipSelect, new byte[] { 0b01_010_000, 0 }, 2)[1];

            // If the count enable on the chip is different, then change it
            if (((MDR1 & 0b00000100) != 0b00000100) != Enable)
            {
                // Flip the CEN bit in MDR1
                if (!Enable) { MDR1 |= 0b00000100; }
                else { MDR1 &= 0b11111011; }

                // Write the new MDR1 to the chip
                this.SPIBus.Write(this.ChipSelect, new byte[] { 0b10_010_000, MDR1 }, 2);
            }
            this.CountEnabled = Enable;
        }

        /// <summary> Receives external events. </summary>
        /// <param name="Sender"> Sender of the event </param>
        /// <param name="Event"> Sent event </param>
        public void EventTriggered(object Sender, EventArgs Event) { if (Event is OverflowEvent) { this.HadEvent = true; } }

        /// <summary> Tests the LS7366R device. </summary>
        /// <returns> Whether or not the test passed </returns>
        public bool Test() { return true; }

        /// <summary>
        /// Loads the buffer data into the chip's
        /// read buffer in parallel, then read
        /// from the read buffer and update Count.
        /// </summary>
        public void UpdateState()
        {
            this.HadEvent = false;

            // LOAD OTR
            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b11_101_000 }, 1);
            
            // READ OTR
            byte[] Output = this.SPIBus.Write(this.ChipSelect, new byte[] { 0b01_101_000, 0, 0, 0, 0 }, 5);

            // Convert output to int
            int IntOut = Output[4];
            IntOut |= (Output[3] << 8);
            IntOut |= (Output[2] << 16);
            IntOut |= (Output[1] << 24);

            // Check for over/under-flows
            byte[] STR = this.SPIBus.Write(this.ChipSelect, new byte[] { 0b01_110_000, 0 }, 2);
            byte STRO = STR[1];
            if ((STRO >> 7) == 1) { OnOverflow(new OverflowEvent() { Overflow = true, Underflow = false }); }
            if ((STRO >> 6) == 1) { OnOverflow(new OverflowEvent() { Overflow = false, Underflow = true }); }

            // Reset over/under-flow bits
            STRO = (byte)(0b00111111 & STRO);
            this.SPIBus.Write(this.ChipSelect, new byte[] { 0b10_110_000, STRO }, 2);

            this.Count = IntOut; // Take over/under-flows into consideration here?
        }

        public DataUnit GetData() // TODO: Make sure this has all necessary data.
        {
            return new DataUnit("LS7366R")
            {
                { "Count", this.Count }
            }.SetSystem(this.System);
        }

        /// <summary> Event called on an overflow </summary>
        /// <param name="Event"> Event to be passed in to the invoke upon overflow. </param>
        protected virtual void OnOverflow(OverflowEvent Event) { OverflowOccured?.Invoke(this, Event); }

        /// <summary> Structure for the configuration of the device. </summary>
        public struct Configuration
        {
            public QuadMode QuadMode;
            public CountMode CountMode;
            public IndexConfig IndexConfig;
            public CounterMode CounterMode;

            public bool CountEnable;

            public bool DivideClkBy2;
            public bool SynchronousIndex;

            public bool FlagOnIDX;
            public bool FlagOnCMP;
            public bool FlagOnBW;
            public bool FlagOnCY;
        }

        /// <summary> Quadrature mode for encoder counting </summary>
        public enum QuadMode
        {
            NON_QUAD,
            X1_QUAD,
            X2_QUAD,
            X4_QUAD
        }

        /// <summary> Encoder counter count mode </summary>
        public enum CountMode
        {
            FREE_RUNNING,
            SINGLE_CYCLE,
            RANGE_LIMIT,
            MOD_N
        }

        /// <summary> Index setup </summary>
        public enum IndexConfig
        {
            DISABLE,
            LOAD_CTR,
            RESET_CTR,
            LOAD_OTR
        }

        /// <summary> Number of bytes to count </summary>
        public enum CounterMode
        {
            BYTE_4,
            BYTE_3,
            BYTE_2,
            BYTE_1
        }
    }

    /// <summary>
    /// Event to store overflow events.
    /// Includes overflow and underflow fields.
    /// </summary>
    public class OverflowEvent : EventArgs
    {
        public bool Overflow { get; set; }
        public bool Underflow { get; set; }
    }
}
