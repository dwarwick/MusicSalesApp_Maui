using System.Reflection;

namespace MusicSalesApp.Maui.Services;

internal static class AppSettingsEnvironmentResolver
{
	private const string MetadataKey = "AppSettingsEnvironment";
	internal const string DebugEnvironment = "Development";
	internal const string ReleaseEnvironment = "Production";

	internal static string GetEnvironmentName(Assembly assembly, bool isReleaseBuild)
	{
		var configuredEnvironment = assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal))
			?.Value;

		return ResolveEnvironmentName(configuredEnvironment, isReleaseBuild);
	}

	internal static string ResolveEnvironmentName(string? configuredEnvironment, bool isReleaseBuild)
	{
		if (!string.IsNullOrWhiteSpace(configuredEnvironment))
		{
			return configuredEnvironment.Trim();
		}

		return isReleaseBuild ? ReleaseEnvironment : DebugEnvironment;
	}

	internal static string GetResourceName(string environmentName)
	{
		return $"MusicSalesApp.Maui.appsettings.{environmentName}.json";
	}
}