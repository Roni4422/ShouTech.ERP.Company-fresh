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
    [InlineData("Ar", "Enterprise")]
    [InlineData("Fr", "Enterprise")]
    [InlineData("Tr", "Enterprise")]
    [InlineData("Ar", "POS")]
    [InlineData("Fr", "POS")]
    [InlineData("Tr", "POS")]
    [InlineData("Ar", "Lkp")]
    [InlineData("Ar", "Qry")]
    [InlineData("Fr", "Qry")]
    [InlineData("Tr", "Qry")]
    [InlineData("Fr", "Lkp")]
    [InlineData("Tr", "Lkp")]
    public void NewKeys_ShouldExistInAllLanguages(string lang, string section)
    {
        string Keys(string l)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir(), l + ".json")));
            return string.Join(",", doc.RootElement.GetProperty(section).EnumerateObject().Select(p => p.Name));
        }
        Assert.Equal(Keys("En"), Keys(lang));
    }
}
