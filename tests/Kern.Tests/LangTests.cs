using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Kern.Tests;

public class LangTests
{
    private static string Dir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "cod", "app", "src", "Resources", "Lang")))
            d = d.Parent;
        return Path.Combine(d!.FullName, "cod", "app", "src", "Resources", "Lang");
    }

    [Theory]
    [InlineData("Ar")]
    [InlineData("Fr")]
    [InlineData("Tr")]
    public void EnterpriseKeys_ShouldExistInAllLanguages(string lang)
    {
        string Keys(string l)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir(), l + ".json")));
            return string.Join(",", doc.RootElement.GetProperty("Enterprise").EnumerateObject().Select(p => p.Name));
        }
        Assert.Equal(Keys("En"), Keys(lang));
    }
}
