using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using nem.Common.Models;

namespace nem.Common.Attributes;

public class JsonHeaderConverter : JsonConverter
{
    private static readonly ConcurrentDictionary<Type, string> _headers = new();

    static readonly JsonSerializerSettings _innerSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new JsonHeaderContractResolver()
    };

    public override bool CanConvert(Type objectType) => typeof(NemConfig).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var header = GetHeader(value.GetType());
        if (!string.IsNullOrEmpty(header))
        {
            writer.WriteRaw(header + "\n  ");
        }
        
        var tempWriter = new System.IO.StringWriter();
        var tempSerializer = JsonSerializer.Create(_innerSettings);
        tempSerializer.Serialize(tempWriter, value);
        writer.WriteRaw(tempWriter.ToString());
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        => throw new NotImplementedException();

    static string GetHeader(Type type)
    {
        return _headers.GetOrAdd(type, t =>
        {
            var attr = t.GetCustomAttributes(typeof(JsonHeaderAttribute), false)
                .OfType<JsonHeaderAttribute>()
                .FirstOrDefault();
            return attr?.Header ?? string.Empty;
        });
    }
}

public class JsonHeaderContractResolver : DefaultContractResolver
{
    protected override JsonConverter ResolveContractConverter(Type objectType)
    {
        if (typeof(NemConfig).IsAssignableFrom(objectType))
            return null;
        return base.ResolveContractConverter(objectType);
    }
}
