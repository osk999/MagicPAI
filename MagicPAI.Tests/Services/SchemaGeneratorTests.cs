using System.Text.Json;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Services;

public class SchemaGeneratorTests
{
    [Fact]
    public void FromType_Includes_All_Properties_In_Required_List()
    {
        var schemaJson = SchemaGenerator.FromType<TriageResult>();
        using var doc = JsonDocument.Parse(schemaJson);

        var required = doc.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["complexity", "category", "recommended_model_power", "needs_decomposition"],
            required);
    }
}
