using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cosmos.TestingFramework;
using Cosmos.TestingFramework.Attributes;

namespace TestKernel
{
    [TestClass]
    public class Test
    {
        [TestMethod]
        public void TestTer()
        {
            Thread.Sleep(1000);
            Console.WriteLine("TestTer executed");
        }
        [TestMethod]
        public void TestTer2()
        {
            Thread.Sleep(1000);
            Console.WriteLine("TestTer2 executed");
        }
        [TestMethod]
        public void TestTer3()
        {
            Thread.Sleep(1000);
            Console.WriteLine("TestTer3 executed");
        }
        [TestMethod]
        public static void TestTer5()
        {
            //Cosmos.TestRunner.Framework.Assert.Fail("This test is expected to fail");
        }
        [TestMethod]
        [Skip("Too much aura")]
        public void TestTer4()
        {
            Thread.Sleep(5000);
        }
    }
}
