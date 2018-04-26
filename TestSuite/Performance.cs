using Scarlet.Filters;
using Scarlet.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Scarlet.TestSuite
{
    public class Performance
    {

        public static void Start(string[] args)
        {
            if (args.Length < 2) { TestMain.ErrorExit("perf command requires functionality to test."); }
            switch(args[1].ToLower())
            {
                case "dataunit":
                    {
                        if (args.Length < 3) { TestMain.ErrorExit("perf dataunit command requires # of elements to test."); }
                        int TestLength = int.Parse(args[2]);
                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Testing DataUnit performance, " + TestLength.ToString("N0", CultureInfo.InvariantCulture) + " iterations.");
                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Generating keyset...");
                        string[] Values = { "Erza", "Homura", "Reika", "Jibril", "Aqua", "Kurisu" };
                        byte[] IDs = new byte[TestLength];
                        Random Random = new Random();
                        for(int i = 0; i < IDs.Length; i++)
                        {
                            IDs[i] = (byte)Random.Next(Values.Length);
                        }
                        DataUnit DUT = new DataUnit("Testing Structure"); // DataUnit under Test

                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Writing to DataUnit...");
                        Stopwatch Stopwatch = Stopwatch.StartNew();
                        for (int i = 0; i < IDs.Length; i++)
                        {
                            DUT.Add(i.ToString(), Values[IDs[i]]);
                        }
                        Stopwatch.Stop();
                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "  Took " + Math.Round(Stopwatch.ElapsedTicks * 1000F / Stopwatch.Frequency, 5) + "ms.");

                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Reading from DataUnit...");
                        Stopwatch = Stopwatch.StartNew();
                        for (int i = 0; i < IDs.Length; i++)
                        {
                            DUT.GetValue<string>(i.ToString());
                        }
                        Stopwatch.Stop();
                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "  Took " + Math.Round(Stopwatch.ElapsedTicks * 1000F / Stopwatch.Frequency, 5) + "ms.");
                        break;
                    }
                case "filter":
                    {
                        if (args.Length < 3) { TestMain.ErrorExit("perf filter command requires filter type to test."); }
                        if (args[2] != "lowpass" && args[2] != "average") { TestMain.ErrorExit("Invalid filter type supplied."); }
                        if (args.Length < 4) { TestMain.ErrorExit("perf filter command requires # of cycles to test."); }

                        IFilter<double> FUT = null;
                        switch(args[2].ToLower())
                        {
                            case "lowpass": { FUT = new LowPass<double>(); break; }
                            case "average": { FUT = new Average<double>(); break; }
                        }
                        int TestLength = int.Parse(args[3]);

                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Testing Filter performance, " + TestLength.ToString("N0", CultureInfo.InvariantCulture) + " iterations.");
                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Generating inputs...");
                        double[] Inputs = new double[TestLength];
                        Random Random = new Random();
                        for (int i = 0; i < Inputs.Length; i++) { Inputs[i] = Random.NextDouble(); }

                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "Cycling Filter...");
                        Stopwatch Stopwatch = Stopwatch.StartNew();
                        for (int i = 0; i < Inputs.Length; i++)
                        {
                            FUT.Feed(Inputs[i]);
                            double Output = FUT.GetOutput();
                        }
                        Stopwatch.Stop();
                        Log.Output(Log.Severity.INFO, Log.Source.OTHER, "  Took " + Math.Round(Stopwatch.ElapsedTicks * 1000F / Stopwatch.Frequency, 5) + "ms.");

                        break;
                    }
            }
        }

    }
}
