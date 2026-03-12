using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public class AzureCloudRegionTests
{
    [Test]
    public void DefaultRegionIsWestEurope()
    {
        var azure = new AzureCloud();
        Assert.That(azure.Location, Is.EqualTo(AzureLocation.WestEurope));
    }

    [Test]
    public void RegionCanBeConfigured()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        Assert.That(azure.Location, Is.EqualTo(AzureLocation.EastUS));
    }

    [Test]
    public void RegionIsOptional()
    {
        Assert.DoesNotThrow(() => new AzureCloud());
    }
}
