using Scarlet.Utilities;
using System;

namespace Scarlet.TestSuite
{
    public class TestMain
    {
        public static void Main(string[] args)
        {
			Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "Starting Scarlet test suite...");
			if (args == null || args.Length == 0 || CheckArg(args, 0, "help")) { OutputHelp(); }

			StateStore.Start("ScarletTest");

			if (args.Length < 1) { ErrorExit("Must provide system to test."); }
			if (CheckArg(args, 0, "io")) {
				if (args.Length < 2) { ErrorExit("io command requires platform."); }
				if (CheckArg(args, 1, "bbb")) {
					IOBBB.Start(args);
				} else if (CheckArg(args, 1, "pi")) {
					IOPi.Start(args);
				} else { Log.Output(Log.Severity.ERROR, Log.Source.GUI, "io command must be for pi or bbb."); }
			} else if (CheckArg(args, 0, "perf")) {
				Performance.Start(args);
			} else if (CheckArg(args, 0, "util")) {
				Utils.Start(args);
			} else if (CheckArg(args, 0, "net")) {
				NetworkDeviceTest.Start(args);
			}
		}

		private static bool CheckArg(string[] args, int argIndex, string value) {
			return args[argIndex].Equals(value, StringComparison.InvariantCultureIgnoreCase);
		}

        public static void ErrorExit(string error)
        {
            Log.Output(Log.Severity.ERROR, Log.Source.GUI, error);
            Environment.Exit(-1);
        }

        private static void OutputHelp()
        {
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "Scarlet Test Suite: Help");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "Command structure usually is ScarletTest <system> <type> <args>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "Valid Commands:");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "io <bbb/pi> ___");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   digin <pin> [repetitions]");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   digout <pin> <high/low/blink>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   pwm <pin> <freq> ___");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "      per <dutycycle (0-100)>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "      sine");
            //Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   i2c ___");
            //Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "      read <address (hex)> <register (hex)> <length>");
            //Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "      write <address (hex)> <register (hex)> <data>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   adc <pin>");
            //Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   uart ___");
            //Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "      read <byte>");
            //Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "      write <data>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   int <pin> <rise/fall/both>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   NOTES: pins should either be a number for Pi, or in the format P9_21 for BBB.");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "perf ___");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   DataUnit <num>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   filter <average/lowpass> <cycles>");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "utils ___");
            Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "   DataLog");
			Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "net");
			Log.ForceOutput(Log.Severity.INFO, Log.Source.GUI, "More will be added!");
			Console.WriteLine("Press any key to exit...");
			Console.ReadKey();
		}
    }
}
