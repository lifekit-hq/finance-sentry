using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>Reads a member the cabinet may shape as anything: a string is kept, any other value reads as not reported.</summary>
public sealed class InzhurStringOrNullConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
