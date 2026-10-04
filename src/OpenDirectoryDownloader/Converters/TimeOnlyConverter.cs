using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Converters;

/// <summary>
/// Shared by the quicktype-generated site result models. Not <see
/// cref="System.Text.Json.Serialization.Metadata.JsonMetadataServices.TimeOnlyConverter"/> because that one
/// serializes with up to 7 fractional-second digits and omits them entirely when zero (e.g. "01:02:03" or
/// "01:02:03.0040000"), which doesn't match quicktype's fixed "HH:mm:ss.fff" template format.
/// </summary>
public class TimeOnlyConverter : JsonConverter<TimeOnly>
{
	private readonly string serializationFormat;

	public TimeOnlyConverter() : this(null) { }

	public TimeOnlyConverter(string serializationFormat)
	{
		this.serializationFormat = serializationFormat ?? "HH:mm:ss.fff";
	}

	public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var value = reader.GetString();
		return TimeOnly.Parse(value!);
	}

	public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
			=> writer.WriteStringValue(value.ToString(serializationFormat));
}
