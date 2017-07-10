using Moq;
using NUnit.Framework;
using Ploeh.AutoFixture;
using Ploeh.AutoFixture.AutoMoq;
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
            var fixture = new Fixture();
            fixture.Customize(new AutoConfiguredMoqCustomization());

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
            securityMock.SetupGet(m => m.IntervalBase).Returns(intervalBase);
            securityMock.SetupGet(m => m.Interval).Returns(interval);
            var result = Simple.GetValidTimeFrame(contextMock.Object, securityMock.Object);

            Assert.AreEqual(expected, result);
        }
    }
}
