using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AppSettingsEnvironmentResolverTests
{
	[Test]
	public void ResolveEnvironmentName_ReturnsConfiguredEnvironment_WhenProvided()
	{
		var result = AppSettingsEnvironmentResolver.ResolveEnvironmentName("Test", isReleaseBuild: true);

		Assert.That(result, Is.EqualTo("Test"));
	}

	[Test]
	public void ResolveEnvironmentName_ReturnsProduction_WhenReleaseAndMissing()
	{
		var result = AppSettingsEnvironmentResolver.ResolveEnvironmentName(null, isReleaseBuild: true);

		Assert.That(result, Is.EqualTo(AppSettingsEnvironmentResolver.ReleaseEnvironment));
	}

	[Test]
	public void ResolveEnvironmentName_ReturnsDevelopment_WhenDebugAndMissing()
	{
		var result = AppSettingsEnvironmentResolver.ResolveEnvironmentName(null, isReleaseBuild: false);

		Assert.That(result, Is.EqualTo(AppSettingsEnvironmentResolver.DebugEnvironment));
	}

	[Test]
	public void GetResourceName_ReturnsEmbeddedResourceName()
	{
		var result = AppSettingsEnvironmentResolver.GetResourceName("Test");

		Assert.That(result, Is.EqualTo("MusicSalesApp.Maui.appsettings.Test.json"));
	}

	[Test]
	public void HasResource_ReturnsTrue_WhenEnvironmentResourceExists()
	{
		var result = AppSettingsEnvironmentResolver.HasResource(
			[
				"MusicSalesApp.Maui.appsettings.json",
				"MusicSalesApp.Maui.appsettings.Development.json"
			],
			"Development");

		Assert.That(result, Is.True);
	}

	[Test]
	public void HasResource_ReturnsFalse_WhenEnvironmentResourceDoesNotExist()
	{
		var result = AppSettingsEnvironmentResolver.HasResource(
			[
				"MusicSalesApp.Maui.appsettings.json"
			],
			"Development");

		Assert.That(result, Is.False);
	}
}