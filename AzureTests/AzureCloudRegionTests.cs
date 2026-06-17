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

    [Test]
    public void WhenSeveralRegionsAreConfigured_TheFirstOneIsUsed()
    {
        var azure = new AzureCloud(locations: new[] { AzureLocation.EastUS, AzureLocation.WestEurope });
        Assert.That(azure.Location, Is.EqualTo(AzureLocation.EastUS));
    }

    [Test]
    public async Task WhenARegionHasNoCapacity_TheResourceGroupIsCreatedInTheNextRegion()
    {
        var arm = new FakeArmClient().ThatHasNoCapacityIn(AzureLocation.EastUS);

        var azure = new AzureCloud(
            arm,
            locations: new[] { AzureLocation.EastUS, AzureLocation.WestEurope });

        await azure.CreateResourceGroup("any-resource-group");

        Assert.That(arm.RegionWhereResourceGroupWasCreated, Is.EqualTo(AzureLocation.WestEurope),
            "When the first region has no capacity, the resource group should be created in the next configured region.");
    }
}
