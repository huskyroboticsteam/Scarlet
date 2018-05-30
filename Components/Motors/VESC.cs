using System;
using Scarlet.Filters;
using Scarlet.IO;
using Scarlet.Utilities;
using System.Threading;
using System.Collections.Generic;

namespace Scarlet.Components.Motors
{
    public class VESC : IMotor
    {
        #region enums
        private enum UARTPacketID : byte
        {
            // Full enum list here: https://github.com/vedderb/bldc_uart_comm_stm32f4_discovery/blob/master/datatypes.h
            FW_VERSION = 0,
            GET_VALUES = 4,
            SET_DUTY = 5,
            SET_CURRENT = 6,
            SET_CURRENT_BRAKE = 7,
            SET_RPM = 8,
            SET_POS = 9,
            SET_DETECT = 10,
            REBOOT = 28,
            ALIVE = 29,
            FORWARD_CAN = 33,
        }

        private enum CANPacketID : byte
        {
            CAN_PACKET_SET_DUTY = 0,
            CAN_PACKET_SET_CURRENT = 1,
            CAN_PACKET_SET_CURRENT_BRAKE = 2,
            CAN_PACKET_SET_RPM = 3,
            CAN_PACKET_SET_POS = 4,
            CAN_PACKET_FILL_RX_BUFFER = 5,
            CAN_PACKET_FILL_RX_BUFFER_LONG = 6,
            CAN_PACKET_PROCESS_RX_BUFFER = 7,
            CAN_PACKET_PROCESS_SHORT_BUFFER = 8,
            CAN_PACKET_STATUS = 9,
        }
        #endregion

        private const sbyte MOTOR_MAX_RPM = 60;
        private int ERPM_PER_RPM = 518;

        private IFilter<sbyte> RPMFilter; // Filter for speed output
        private readonly IUARTBus UARTBus;
        private readonly ICANBus CANBus;
        private readonly int CANForwardID;
        private readonly uint CANID;
        private readonly sbyte MaxRPM;
        private readonly bool IsCAN;

        private bool OngoingSpeedThread; // Whether or not a thread is running to set the speed
        private bool Stopped; // Whether or not the motor is stopped
        public float TargetSpeed { get; private set; } // Target speed (-1.0 to 1.0) of the motor
        public sbyte TargetRPM { get; private set; } // Target RPM of the motor

        /// <summary> Initializes a VESC Motor controller </summary>
        /// <param name="UARTBus"> UART output to control the motor controller </param>
        /// <param name="MaxSpeed"> Limiting factor for speed (should never exceed + or - this val) </param>
        /// <param name="CANForwardID"> CAN ID of the motor controller (-1 to disable CAN forwarding) </param>
        /// <param name="RPMFilter"> Filter to use with MC. Good for ramp-up protection and other applications </param>
        public VESC(IUARTBus UARTBus, float MaxSpeed, int CANForwardID = -1, IFilter<sbyte> RPMFilter = null, int ERPMPerRPM = 518)
            : this(UARTBus, (sbyte)(MaxSpeed * MOTOR_MAX_RPM), CANForwardID, RPMFilter, ERPMPerRPM) { }

        public VESC(IUARTBus UARTBus, sbyte MaxRPM, int CANForwardID = -1, IFilter<sbyte> RPMFilter = null, int ERPMPerRPM = 518)
        {
            IsCAN = false;
            this.ERPM_PER_RPM = ERPMPerRPM;
            this.UARTBus = UARTBus;
            this.UARTBus.BaudRate = UARTRate.BAUD_115200;
            this.UARTBus.BitLength = UARTBitCount.BITS_8;
            this.UARTBus.StopBits = UARTStopBits.STOPBITS_1;
            this.UARTBus.Parity = UARTParity.PARITY_NONE;
            this.CANForwardID = CANForwardID;
            this.MaxRPM = Math.Abs(MaxRPM);
            this.RPMFilter = RPMFilter;
            this.SetRPMDirectly(0);
            SetSpeedThreadFactory().Start();
        }

        public VESC(ICANBus CANBus, float MaxSpeed, uint CANID, IFilter<sbyte> RPMFilter = null, int ERPMPerRPM = 518)
            : this(CANBus, (sbyte)(MaxSpeed * MOTOR_MAX_RPM), CANID, RPMFilter, ERPMPerRPM) { }

        /// <summary> Initializes a VESC Motor controller </summary>
        /// <param name="CANBus"> CAN output to control the motor controller </param>
        /// <param name="MaxRPM"> Limiting factor for speed (should never exceed + or - this val) </param>
        /// <param name="RPMFilter"> Filter to use with MC. Good for ramp-up protection and other applications </param>
        public VESC(ICANBus CANBus, sbyte MaxRPM, uint CANID, IFilter<sbyte> RPMFilter = null, int ERPMPerRPM = 518)
        {
            IsCAN = true;
            this.ERPM_PER_RPM = ERPMPerRPM;
            this.CANBus = CANBus;
            this.MaxRPM = Math.Abs(MaxRPM);
            this.CANID = CANID;
            this.RPMFilter = RPMFilter;
            this.SetRPMDirectly(0);
            SetSpeedThreadFactory().Start();
        }

        public void EventTriggered(object Sender, EventArgs Event) { }

        /// <summary> 
        /// Immediately sets the enabled status of the motor.
        /// Stops the motor if given parameter is false.
        /// Does not reset the target speed to zero, so beware
        /// of resetting this to enabled.
        /// </summary>
        public void SetEnabled(bool Enabled)
        {
            this.Stopped = !Enabled;
            if (Enabled) { this.SetSpeed(this.TargetSpeed); }
            else { this.SetRPMDirectly(0); }
        }

        /// <summary> Sets the speed on a thread for filtering. </summary>
        private void SetSpeedThread()
        {
            float Output = this.RPMFilter.GetOutput();
            while (true)
            {
                if (Stopped) { SetRPMDirectly(0); }
                else if (!this.RPMFilter.IsSteadyState())
                {
                    this.RPMFilter.Feed(this.TargetRPM);
                    SetRPMDirectly(this.RPMFilter.GetOutput());
                }
                else { this.SetRPMDirectly(this.TargetRPM); }
                Thread.Sleep(Constants.DEFAULT_MIN_THREAD_SLEEP);
            }
        }

        /// <summary> Creates a new thread for setting speed during motor filtering output </summary>
        /// <returns> A new thread for changing the motor speed. </returns>
        private Thread SetSpeedThreadFactory()
        {
            Thread T = new Thread(new ThreadStart(SetSpeedThread));
            T.IsBackground = true;
            return T;
        }

        /// <summary>
        /// Sets the motor speed. Output may vary from the given value under the following conditions:
        /// - Input exceeds maximum speed. Capped to given maximum.
        /// - Filter changes value. Filter's output used instead.
        ///     (If filter is null, this does not occur)
        /// - The motor is disabled. You must first re-enable the motor.
        /// </summary>
        /// <param name="Speed"> The new speed to set the motor at. From -1.0 to 1.0 </param>
        public void SetSpeed(float Speed)
        {
            sbyte RPM = (sbyte)(Speed * MOTOR_MAX_RPM);
            SetRPM(RPM);
        }

        public void SetRPM(sbyte RPM)
        {
            this.TargetRPM = RPM;
            this.TargetSpeed = (float)RPM / (float)MOTOR_MAX_RPM;
        }

        /// <summary>
        /// Sets the speed directly given an input from -1.0 to 1.0
        /// Takes into consideration motor stop signal and max speed restriction.
        /// </summary>
        /// <param name="Speed">  </param>
        private void SetRPMDirectly(sbyte Speed)
        {
            if (Speed > this.MaxRPM) { Speed = this.MaxRPM; }
            if (-Speed > this.MaxRPM) { Speed = (sbyte)-this.MaxRPM; }
            if (this.Stopped) { Speed = 0; }
            this.SendRPM(Speed);
        }

        /// <summary> Sends the speed between -1.0 and 1.0 to the motor controller </summary>
        /// <param name="Speed"> Speed from -1.0 to 1.0 </param>
        private void SendSpeed(float Speed)
        {
            byte[] SpeedArray = UtilData.ToBytes((int)(Speed * 100000.0f));
            if (this.IsCAN) { this.CANBus.Write(this.CANID, SpeedArray); }
            else
            {
                List<byte> payload = new List<byte>();
                payload.Add((byte)UARTPacketID.SET_DUTY);
                payload.AddRange(SpeedArray);
                // Duty Cycle (100000.0 mysterious magic number from https://github.com/VTAstrobotics/VESC_BBB_UART/blob/master/bldc_interface.c)
                this.UARTBus.Write(ConstructPacket(payload));
            }
        }

        /// <summary> Sends the speed to the motor controller </summary>
        /// <param name="RPM"> RPM for the motor to spin at. RPM is capped at 36500 </param>
        private void SendRPM(int RPM)
        {
            RPM = Math.Min(RPM, MOTOR_MAX_RPM) * ERPM_PER_RPM;
            byte[] SpeedArray = UtilData.ToBytes(RPM);
            if (this.IsCAN) { this.CANBus.Write(((byte)CANPacketID.CAN_PACKET_SET_RPM << 8) | this.CANID, SpeedArray); }
            else
            {
                List<byte> payload = new List<byte>();
                payload.Add((byte)UARTPacketID.SET_RPM);
                payload.AddRange(SpeedArray);
                this.UARTBus.Write(ConstructPacket(payload));
            }
        }

        /// <summary> Generates the packet for the motor controller: </summary>
        /// <remarks>
        /// One Start byte (value 2 for short packets and 3 for long packets)
        /// One or two bytes specifying the packet length
        /// The payload of the packet
        /// Two bytes with a CRC checksum on the payload
        /// One stop byte (value 3)
        /// </remarks>
        /// <param name="Speed"> Speed from -1.0 to 1.0 </param>
        private byte[] ConstructPacket(List<byte> Payload)
        {
            List<byte> Packet = new List<byte>();

            Packet.Add(2); // Start byte (short packet - payload <= 256 bytes)

            if (this.CANForwardID >= 0)
            {
                Payload.Add((byte)UARTPacketID.FORWARD_CAN);
                Payload.Add((byte)CANForwardID);
            }

            Packet.Add((byte)Payload.Count); // Length of payload
            Packet.AddRange(Payload); // Payload

            ushort Checksum = UtilData.CRC16(Payload.ToArray());
            Packet.AddRange(UtilData.ToBytes(Checksum)); // Checksum

            Packet.Add(3); // Stop byte

            return Packet.ToArray();
        }

    }

}