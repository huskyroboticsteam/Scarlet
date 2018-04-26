using System;
using Scarlet.Utilities;
using Scarlet.IO;

namespace Scarlet.Components.Sensors
{
    /// <summary>
    /// A class for converting an IAnalogueInput with a linear potentiometer connected into a rotation degrees value.
    /// Can send events when the potentiometer has been turned.
    /// Will only update the current angle when UpdateState is called, and therefore will only send events then as well.
    /// </summary>
    public class Potentiometer : ISensor
    {
        private IAnalogueIn Input;
        public int Angle { get; private set; }
        private readonly int Range;
        private readonly bool Invert;
        public event EventHandler<PotentiometerTurn> Turned;
        public string System { get; set; }

        public Potentiometer(IAnalogueIn Input, int Degrees, bool Invert = false)
        {
            this.Input = Input;
            this.Range = Degrees;
            this.Invert = Invert;
        }

        public bool Test()
        {
            return true; // TODO: What could we check for?
        }

        /// <summary>
        /// Checks the IAnalogueInput for the new angle, and sends events if applicable.
        /// Cannot determine the total amount turned since the last UpdateState call, only the net change.
        /// As such, if the potentiometer is turned one direction, then back to the same place, we have no way of determining any movement occured.
        /// </summary>
        public void UpdateState()
        {
            int NewAngle = this.Range - (int)((((float)this.Input.GetRawInput() / (float)this.Input.GetRawRange()) * this.Range));
            if (this.Invert) { NewAngle = this.Range - NewAngle; }

            int AngleChange = this.Angle - NewAngle;
            this.Angle = NewAngle;

            if (AngleChange != 0)
            {
                PotentiometerTurn Event = new PotentiometerTurn() { TurnAmount = AngleChange, Angle = this.Angle };
                OnTurn(Event);
            }
        }

        protected virtual void OnTurn(PotentiometerTurn Event) { Turned?.Invoke(this, Event); }

        /// <summary> This sensor does not process events. Will do nothing. </summary>
        public void EventTriggered(object Sender, EventArgs Event) { }

        public DataUnit GetData()
        {
            return new DataUnit("Potentiometer")
            {
                { "Angle", this.Angle }
            }
            .SetSystem(this.System);
        }
    }

    public class PotentiometerTurn : EventArgs
    {
        public int TurnAmount;
        public int Angle;
    }
}
