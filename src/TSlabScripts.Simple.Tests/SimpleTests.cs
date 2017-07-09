using Moq;
using NUnit.Framework;
using Ploeh.AutoFixture;
using Ploeh.AutoFixture.AutoMoq;
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

            fixture.Register(() => contextMock);
            fixture.Register(() => securityMock);
        }

        [Test]
        [TestCase]
        public void GetValidTimeFrame()
        {

            Simple.GetValidTimeFrame(contextMock, securityMock);
        }
    }
}
