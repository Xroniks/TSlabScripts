using System;
using System.Collections;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSlabScripts.Simple.Tests
{
    [TestFixture]
    public class SimpleTests
    {
        private Mock<IContext> contextMock;
        private Mock<ISecurity> securityMock;

        [SetUp]
        public void SetUp()
        {
            contextMock = new Mock<IContext>();
            securityMock = new Mock<ISecurity>();
        }

        [Test]
        [TestCase(DataIntervals.SECONDS, 5, true)]
        [TestCase(DataIntervals.DAYS, 5, false)]
        [TestCase(DataIntervals.MINUTE, 5, false)]
        [TestCase(DataIntervals.TICK, 5, false)]
        [TestCase(DataIntervals.SECONDS, 10, false)]
        public void GetValidTimeFrame(DataIntervals intervalBase, int interval, bool expected)
        {
            var result = Simple.GetValidTimeFrame(intervalBase, interval);

            Assert.AreEqual(expected, result);
        }

        [Test]
        [TestCaseSource(nameof(GetIndexActualCompressBar))]
        public void GetIndexActualCompressBar(DateTime dateActualBar, int indexBeginDayBar, int expected)
        {
            var result = Simple.GetIndexActualCompressBar(dateActualBar, indexBeginDayBar);

            Assert.AreEqual(expected, result);
        }

        private IList<Bar> GetBars()
        {
            var barList = new List<Bar>();
            var date = new DateTime(1, 1, 1, 10, 0, 0);
            for (int i = 0; i < 100; i++)
            {
                barList.Add(new Bar(new Color(), date, 0, 0, 0, 0, 0));
                date = date.AddMinutes(5);
            }
            return barList;
        }

        private static object[] GetIndexActualCompressBar()
        {
            return new object[]
            {
                new object[]
                {
                    new DateTime(1, 1, 1, 10, 30, 0),
                    0,
                    6
                },
                new object[]
                {
                    new DateTime(1, 1, 1, 10, 33, 30),
                    0,
                    6
                },
                new object[]
                {
                    new DateTime(1, 1, 1, 10, 34, 55),
                    0,
                    6
                },
            };
        }
    }
}
