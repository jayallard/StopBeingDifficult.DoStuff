using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sbd.DoStuff.Domain.Serialization;

internal static class YamlSerializerFactory
{
    public static ISerializer Create() =>
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithTypeConverter(new DateTimeOffsetYamlConverter())
            .Build();
}
