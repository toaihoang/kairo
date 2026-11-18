using System.Reflection;

namespace Kairo.Core.Tests;

// Guards the repo-wide build settings: every library must stay trim/AOT compatible.
public class BuildSettingsTests
{
    [Theory]
    [InlineData("Kairo.Abstractions")]
    [InlineData("Kairo.Core")]
    public void LibraryIsMarkedTrimmable(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);

        bool isTrimmable = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "IsTrimmable" && a.Value == "True");

        Assert.True(isTrimmable, $"{assemblyName} must set IsAotCompatible=true (see src/Directory.Build.props).");
    }
}
