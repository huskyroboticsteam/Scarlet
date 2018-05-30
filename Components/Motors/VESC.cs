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
        private readonly int CANForwardID;
        private readonly float MaxSpeed;

        private bool OngoingSpeedThread; // Whether or not a thread is running to set the speed
        private bool Stopped; // Whether or not the motor is stopped
        public float TargetSpeed { get; private set; } // Target speed (-1.0 to 1.0) of the motor

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
            else { this.SetSpeedDirectly(0); }
        }

        /// <summary> Sets the speed on a thread for filtering. </summary>
        private void SetSpeedThread()
        {
            float Output = this.Filter.GetOutput();
            while (!this.Filter.IsSteadyState())
            {
                if (Stopped) { SetSpeedDirectly(0); }
                else
                {
                    this.Filter.Feed(this.TargetSpeed);
                    SetSpeedDirectly(this.Filter.GetOutput());
                }
                Thread.Sleep(Constants.DEFAULT_MIN_THREAD_SLEEP);
            }
            OngoingSpeedThread = false;
        }

        /// <summary> Creates a new thread for setting speed during motor filtering output </summary>
        /// <returns> A new thread for changing the motor speed. </returns>
        private Thread SetSpeedThreadFactory() { return new Thread(new ThreadStart(SetSpeedThread)); }

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
            if (this.Filter != null && !this.Filter.IsSteadyState() && !OngoingSpeedThread)
            {
                this.Filter.Feed(Speed);
                SetSpeedThreadFactory().Start();
                OngoingSpeedThread = true;
            }
            else { SetSpeedDirectly(Speed); }
            this.TargetSpeed = Speed;
        }

        /// <summary>
        /// Sets the speed directly given an input from -1.0 to 1.0
        /// Takes into consideration motor stop signal and max speed restriction.
        /// </summary>
        /// <param name="Speed"> Speed from -1.0 to 1.0 </param>
        private void SetSpeedDirectly(float Speed)
        {
            if (Speed > this.MaxSpeed) { Speed = this.MaxSpeed; }
            if (-Speed > this.MaxSpeed) { Speed = -this.MaxSpeed; }
            if (this.Stopped) { Speed = 0; }
            this.SendSpeed(Speed);
        }

        /// <summary>
        /// Sends the speed between -1.0 and 1.0 to the motor controller
        /// </summary>
        /// <param name="Speed"> Speed from -1.0 to 1.0 </param>
        private void SendSpeed(float Speed)
        {
            List<byte> payload = new List<byte>();
            payload.Add((byte)PacketID.SET_DUTY);
            // Duty Cycle (100000.0 mysterious magic number from https://github.com/VTAstrobotics/VESC_BBB_UART/blob/master/bldc_interface.c)
            payload.AddRange(UtilData.ToBytes((Int32)(Speed * 100000.0)));
            this.UARTBus.Write(ConstructPacket(payload));
        }

        /// <summary> Tell the motor controller that there is a listener on the other end. </summary>
        /// <remarks> Makes the motor stop. </remarks>
        public void SendAlive()
        {
            List<byte> payload = new List<byte>();
            payload.Add((byte)PacketID.ALIVE);
            this.UARTBus.Write(ConstructPacket(payload));
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
                Payload.Add((byte)PacketID.FORWARD_CAN);
                Payload.Add((byte)CANForwardID);
            }

            Packet.Add((byte)Payload.Count); // Length of payload
            Packet.AddRange(Payload); // Payload

            ushort Checksum = UtilData.CRC16(Payload.ToArray());
            Packet.AddRange(UtilData.ToBytes(Checksum)); // Checksum

            Packet.Add(3); // Stop byte

            return Packet.ToArray();
        }

        #region enums
        private enum PacketID : byte
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
            FORWARD_CAN = 33
        }
        #endregion
    }

}