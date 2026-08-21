using System.Runtime.CompilerServices;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeDeserializers;
using YamlDotNet.Serialization.ObjectFactories;

namespace Sbd.DoStuff.Domain.Serialization;

internal static class YamlDeserializerFactory
{
    public static IDeserializer Create() =>
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithObjectFactory(new RecordFriendlyObjectFactory())
            .WithNodeDeserializer(new ReadOnlyCollectionNodeDeserializer(), s => s.Before<ObjectNodeDeserializer>())
            .WithTypeConverter(new DateTimeOffsetYamlConverter())
            .Build();

    // Our domain records expose IReadOnlyList<T>/IReadOnlyDictionary<K,V> properties (by
    // design — see Architecture notes in CLAUDE.md). YamlDotNet's built-in collection
    // deserializers only populate types with an Add method (List<T>, Dictionary<K,V>, etc.),
    // so a bare read-only interface is otherwise left for the generic ObjectNodeDeserializer,
    // which fails on it. This redirects those specific interfaces to their mutable
    // counterpart, which is assignment-compatible with the read-only interface.
    private sealed class ReadOnlyCollectionNodeDeserializer : INodeDeserializer
    {
        public bool Deserialize(
            IParser parser, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer,
            out object? value, ObjectDeserializer rootDeserializer)
        {
            if (!TryGetConcreteType(expectedType, out var concreteType))
            {
                value = null;
                return false;
            }

            value = nestedObjectDeserializer(parser, concreteType);
            return true;
        }

        private static bool TryGetConcreteType(Type type, out Type concreteType)
        {
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();

                if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
                {
                    concreteType = typeof(List<>).MakeGenericType(args);
                    return true;
                }

                if (definition == typeof(IReadOnlyDictionary<,>))
                {
                    concreteType = typeof(Dictionary<,>).MakeGenericType(args);
                    return true;
                }
            }

            concreteType = null!;
            return false;
        }
    }

    // Our domain types are records with no parameterless constructor — only a primary
    // constructor and init-only properties. YamlDotNet's DefaultObjectFactory requires a
    // parameterless constructor to create the instance before populating properties by
    // reflection, so for those types we invoke the primary constructor ourselves, passing each
    // parameter's own C# default (e.g. TaskParameterDefinition's `CanOverride = true`) where the
    // parameter wasn't required. Blindly zero-allocating via GetUninitializedObject would silently
    // turn every such default into false/null instead. YamlDotNet's normal property-setting then
    // overwrites whichever properties the YAML document actually specifies.
    private sealed class RecordFriendlyObjectFactory : DefaultObjectFactory
    {
        public override object Create(Type type)
        {
            if (type is not { IsClass: true, IsAbstract: false } || type.GetConstructor(Type.EmptyTypes) is not null)
            {
                return base.Create(type);
            }

            var constructor = type.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                return RuntimeHelpers.GetUninitializedObject(type);
            }

            var arguments = constructor.GetParameters()
                .Select(p => p.HasDefaultValue ? p.DefaultValue : GetDefault(p.ParameterType))
                .ToArray();

            return constructor.Invoke(arguments);
        }

        private static object? GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
