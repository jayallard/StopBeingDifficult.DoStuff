using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Sbd.DoStuff.Domain.Serialization;

// YamlDotNet has no built-in scalar handling for DateTimeOffset — left to the default
// object deserializer/serializer, it would read/write every field of the struct
// (dateTime, offset, ticks, ...) as a mapping instead of a single round-trippable value.
internal sealed class DateTimeOffsetYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        return DateTimeOffset.Parse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var text = value is DateTimeOffset dto ? dto.ToString("o", CultureInfo.InvariantCulture) : string.Empty;
        emitter.Emit(new Scalar(text));
    }
}
