using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace Sbd.DoStuff.Domain.Serialization;

internal static class YamlSerializerFactory
{
    public static ISerializer Create() =>
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithTypeConverter(new DateTimeOffsetYamlConverter())
            .WithEventEmitter(next => new QuoteTabStringsEventEmitter(next))
            .Build();

    // YamlDotNet writes a string with a tab in it as a plain scalar, but a plain scalar can't start with a
    // tab (the reader drops it as indentation, or rejects what follows). Captured process output is often
    // tab-indented, so those strings must be double-quoted to survive a round trip.
    private sealed class QuoteTabStringsEventEmitter(IEventEmitter next) : ChainedEventEmitter(next)
    {
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string text && text.Contains('\t'))
            {
                eventInfo.Style = ScalarStyle.DoubleQuoted;
            }

            base.Emit(eventInfo, emitter);
        }
    }
}
