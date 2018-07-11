﻿using Scarlet.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.TestSuite
{
    public class Utils
    {
        public static void Start(string[] args)
        {
            if (args.Length < 2) { TestMain.ErrorExit("utils command requires functionality to test."); }
            switch (args[1].ToLower())
            {
                case "datalog":
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            Random Random = new Random();
                            DataLog Logger = new DataLog("ScarletTestSuite");
                            Logger.Output(new DataUnit("TestData")
                            {
                                { "Index", i },
                                { "RandomNumber", Random.Next(100) }
                            }.SetSystem("Test"));
                            Thread.Sleep(50);
                        }
                        break;
                    }
                case "platform":
                    {
                        Log.ForceOutput(Log.Severity.INFO, Log.Source.OTHER, "PLATFORM:\t\t" + Enum.GetName(typeof(PlatformInfo.PlatformType), PlatformInfo.Platform));
                        Log.ForceOutput(Log.Severity.INFO, Log.Source.OTHER, "HARDWARE:\t\t" + PlatformInfo.Hardware);
                        Log.ForceOutput(Log.Severity.INFO, Log.Source.OTHER, "OS:\t\t\t" + PlatformInfo.OS);
                        Log.ForceOutput(Log.Severity.INFO, Log.Source.OTHER, "OS REV:\t\t" + PlatformInfo.OSRevision);
                        Log.ForceOutput(Log.Severity.INFO, Log.Source.OTHER, "OS NAME:\t\t" + Enum.GetName(typeof(PlatformInfo.OperatingSystems), PlatformInfo.OSName));
                        Log.ForceOutput(Log.Severity.INFO, Log.Source.OTHER, "OS SUPPORT:\t\t" + PlatformInfo.OSSupport.ToString());
                        break;
                    }
            }
        }
    }
}
