using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class FlexibleDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetDouble(out double value) ? value : 0;

        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
