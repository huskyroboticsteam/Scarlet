using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Scarlet.Components.Motors;
using Scarlet.Filters;
using Scarlet.IO;
using System.Diagnostics;
using Scarlet.Utilities;
using System.Threading;

namespace UnitTest
{
    [TestClass]
    public class TestTalonMC
    {
        private TestContext TestContextInstance;

        public TestContext TestContext
        {
            get { return TestContextInstance; }
            set { TestContextInstance = value; }
        }


        [TestMethod]
        public void TestBasicOutput()
        {
            // Given a test PWM Output
            TestablePWMOutput TestPWMOutput = new TestablePWMOutput();
            TestablePWMOutput TestPWMOutput2 = new TestablePWMOutput();

            TestPWMOutput.SetOutput(0.3f);      // Arbitrary choices
            TestPWMOutput2.SetOutput(0.1f);     // to aid in debugging

            // And a null filter and arbitrary max speed (0.2) for example
            // See how the output of the motor responds.
            TalonMC PositiveMaxSpeedTalon = new TalonMC(TestPWMOutput, 0.2f, null);
            TalonMC NegativeMaxSpeedTalon = new TalonMC(TestPWMOutput2, -0.2f, null);

            Assert.AreEqual(NegativeMaxSpeedTalon.TargetSpeed, 0.0);
            Assert.AreEqual(PositiveMaxSpeedTalon.TargetSpeed, 0.0);

            Assert.AreEqual(TestPWMOutput.DutyCycle, 0.5f);
            Assert.AreEqual(TestPWMOutput2.DutyCycle, 0.5f);

            // Tested these conditions by setting OngoingSpeedThread to public
            // then set it back.
            // Assert.IsFalse(PositiveMaxSpeedTalon.OngoingSpeedThread);
            // Assert.IsFalse(NegativeMaxSpeedTalon.OngoingSpeedThread);

            PositiveMaxSpeedTalon.SetSpeed(-1.0f);
            NegativeMaxSpeedTalon.SetSpeed(1.0f);
            
            // Tested these conditions by setting OngoingSpeedThread to public
            // then set it back.
            // Assert.IsFalse(PositiveMaxSpeedTalon.OngoingSpeedThread);
            // Assert.IsFalse(NegativeMaxSpeedTalon.OngoingSpeedThread);

            Assert.AreEqual(-1 * PositiveMaxSpeedTalon.TargetSpeed, NegativeMaxSpeedTalon.TargetSpeed);
            Assert.AreEqual(PositiveMaxSpeedTalon.TargetSpeed, -1.0f);
            Assert.AreEqual(NegativeMaxSpeedTalon.TargetSpeed, 1.0f);

            Assert.AreEqual(TestPWMOutput.Frequency, 333);
            Assert.AreEqual(TestPWMOutput.DutyCycle, 7 / 15.0f);

            Assert.AreEqual(TestPWMOutput2.Frequency, 333);
            Assert.AreEqual(TestPWMOutput2.DutyCycle, 8 / 15.0f);

            NegativeMaxSpeedTalon = new TalonMC(TestPWMOutput, -0.2f, null);

            NegativeMaxSpeedTalon.SetSpeed(0.2f);

            Assert.AreEqual(TestPWMOutput.DutyCycle, 8/15.0f);
        }

        [TestMethod]
        public void TestEnableDisable()
        {
            TestablePWMOutput TestPWMOutput = new TestablePWMOutput();
            float SteadyStateEpsilon = 0.001f;
            LowPass<float> TestLPF = new LowPass<float>(SteadyStateEpsilon: SteadyStateEpsilon);
            TestPWMOutput.SetOutput(0.3f);
            TalonMC TestMotor = new TalonMC(TestPWMOutput, 0.4f, TestLPF);

            TestMotor.SetSpeed(0.5f);

            Thread.Sleep(500);

            Assert.AreEqual(0.5f, TestMotor.TargetSpeed);
            // Assert.AreNotEqual(TestMotor.OngoingSpeedThread, TestLPF.IsSteadyState());
            Assert.AreNotEqual(SteadyStateEpsilon == 0.0f, TestLPF.IsSteadyState());

            TestMotor.SetEnabled(false);

            Assert.AreEqual(0.5f, TestMotor.TargetSpeed);
            Assert.AreEqual(0.5f, TestPWMOutput.DutyCycle);

            TestMotor.SetEnabled(true);

            Assert.AreEqual(0.5f, TestMotor.TargetSpeed);
            Thread.Sleep(500);
            Assert.AreNotEqual(0.5f, TestPWMOutput.DutyCycle);
        }

        [TestMethod]
        public void TestThreadValidity()
        {
            TestablePWMOutput TestPWMOutput = new TestablePWMOutput();
            LowPass<float> TestFilter = new LowPass<float>(SteadyStateEpsilon: 0.1);
            TalonMC TestTalon = new TalonMC(TestPWMOutput, 0.5f, TestFilter);

            // Low Pass should run continuously because it was not given an epsilon above
            // Here we will make sure it doesn't block
            Stopwatch Watch = new Stopwatch();
            Watch.Reset();
            Watch.Start();
            TestTalon.SetSpeed(1.0f);
            Watch.Stop();
            // Allow 150ms delay for setting talon speed
            Assert.IsTrue(Watch.ElapsedMilliseconds < 150);
            // Test to ensure that output duty cycle rises
            // (Which is controlled on another thread)
            float LastDC = TestPWMOutput.DutyCycle;
            Watch.Reset();
            Watch.Start();
            while (Watch.ElapsedMilliseconds < 2000)
            {
                float DC = TestPWMOutput.DutyCycle;
                if (TestFilter.IsSteadyState())
                {
                    Assert.AreEqual(DC, LastDC);

                    // Tested these conditions by setting OngoingSpeedThread to public
                    // then set it back.
                    // Assert.IsFalse(TestTalon.OngoingSpeedThread);
                }
                else
                {

                    // Tested these conditions by setting OngoingSpeedThread to public
                    // then set it back.
                    // Assert.IsTrue(TestTalon.OngoingSpeedThread);
                    Assert.IsTrue(DC > LastDC);
                }
                LastDC = DC;
            }
        }
    }
}
