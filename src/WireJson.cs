using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace IronbarkCoop
{
    // Field-only contracts keep Unity Vector/Quaternion computed properties off the wire.
    internal static class WireJson
    {
        sealed class FieldsOnly : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization mode) =>
                base.CreateProperties(type, mode)
                    .Where(p => type.GetField(p.UnderlyingName, BindingFlags.Instance | BindingFlags.Public) != null)
                    .ToList();
        }
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
            ContractResolver = new FieldsOnly(),
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 12,
            FloatParseHandling = FloatParseHandling.Double
        };
        // Network form omits nulls and defaults so a pose fits one UDP datagram; "kind" stays first (prefix routing).
        static readonly JsonSerializerSettings Compact = new JsonSerializerSettings {
            ContractResolver = new FieldsOnly(),
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 12,
            FloatParseHandling = FloatParseHandling.Double,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        public static string Write(Packet packet, bool pretty = false) =>
            pretty ? JsonConvert.SerializeObject(packet, Formatting.Indented, Settings) : JsonConvert.SerializeObject(packet, Formatting.None, Compact);
        public static Packet Read(string json) => JsonConvert.DeserializeObject<Packet>(json, Settings);
    }
}
