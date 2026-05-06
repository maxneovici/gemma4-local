using NJsonSchema;
using NJsonSchema.Generation;
using System.Text.Json;

namespace LLLMax.Api.Tools;

public static class ToolJsonSchema
{
    public static string From<TArguments>()
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull,
            SchemaType = SchemaType.JsonSchema,
            SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        };

        var schema = JsonSchema.FromType<TArguments>(settings);
        schema.AllowAdditionalProperties = false;

        return schema.ToJson();
    }
}
