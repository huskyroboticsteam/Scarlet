using System;
using Scarlet.Utilities;

namespace Scarlet.Filters
{
    /// <summary> The Average filter is intended for use as an average-gathering system, using a rolling average with "roll-length" <c>FilterCount</c>.</summary>
    /// <remarks>
    /// Implementation Details:
    /// 
    /// *Construct Average filter given a rolling
    ///  filter length, <c>FilterCount</c>
    /// 
    /// *Iteratively add values into the filter
    ///  using <c>Feed(T Input)</c>
    /// 
    /// *Get the filter output by calling
    ///  <c>YourFilterInstance.Output</c>
    /// </remarks>
    /// <typeparam name="T"> A type, which must be a numeric. </typeparam>
    public class Average<T> : IFilter<T> where T : IComparable
    {

        private T Output; // Filter output
        private dynamic[] AverageArray; // Stored average array
        private dynamic CurSum;         // Current sum of the average array 
        private int FilterCount,        // Size of the average array
                    Index,              // Current index of the average array
                    Iterations;         // Number of iterations in the filter
        private int NumCyclesAverageSame; // Number of cycles that the average has stayed the same

        /// <summary> Construct an average filter with given roll-length. </summary>
        /// <param name="FilterCount"> Roll length for the average filter. </param>
        public Average(int FilterCount = 10)
        {
            if (!UtilData.IsNumericType(typeof(T)))
            {
                Log.Output(Log.Severity.ERROR, Log.Source.OTHER, "Average filter cannot be instantiated with non-numeric type.");
                throw new ArgumentException("Cannot create filter of non-numeric type: " + typeof(T).ToString());
            } // We can now assert that T is a numeric type

            this.Output = default(T);
            this.CurSum = 0;
            this.Index = 0;
            this.Iterations = 0;
            this.FilterCount = FilterCount;
            this.AverageArray = new dynamic[this.FilterCount];
            this.InitializeArray(); // Initialize average array to defaults
            this.NumCyclesAverageSame = 0;
            this.Feed(default(T));
        }

        /// <summary> Feeds a value into the filter. </summary>
        /// <param name="Input"> Value to feed into the filter. </param>
        public void Feed(T Input)
        {
            // Increase number of iterations by 1
            this.Iterations++;
            // Store input as a dynamic type since we know T is a numeric
            dynamic dynamicInput = Input;
            // Subtract current array index value from sum
            this.CurSum -= this.AverageArray[this.Index];
            // Add current value to sum
            this.CurSum += dynamicInput;
            // Store curent value in old spot
            this.AverageArray[this.Index] = dynamicInput;
            // Increment index. Go back to zero if index + 1 == filterCount
            this.Index = (this.Index + 1) % this.FilterCount; // Increment index. Go back to zero if index + 1 == filterCount
            // Keep temporary track of the last output
            T LastOutput = this.Output;
            // Divide output by either number of iterations or filter length
            this.Output = this.CurSum / (Math.Min(this.Iterations, this.FilterCount));
            // Add one to the count if the average was the same after this cycle, otherwise reset it
            if ((dynamic)LastOutput == (dynamic)this.Output) { NumCyclesAverageSame++; } else { NumCyclesAverageSame = 0; }
        }

        /// <summary> Rate is irrelevant to average filter, so this is no different than using Feed(Input). </summary>
        /// <param name="Input"> Value to feed into the filer. </param>
        /// <param name="Rate"> Ignored </param>
        public void Feed(T Input, T Rate) { this.Feed(Input); }

        /// <summary> Initializes dynamic number array to all zeros. </summary>
        private void InitializeArray()
        {
            for (int i = 0; i < this.AverageArray.Length; i++)
            {
                this.AverageArray[i] = 0;
            }
        }

        /// <summary> Computes whether or not the average filter is in steady state </summary>
        /// <returns> Whether or not filter is in steady state </returns>
        public bool IsSteadyState() { return NumCyclesAverageSame >= FilterCount; }

        public T GetOutput() { return this.Output; }
    }
}
