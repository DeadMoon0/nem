using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Threading;
using nem.Common.Models;

namespace nem.Common.Attributes;

public class JsonHeaderConverter : JsonConverter
{
    private static readonly ConcurrentDictionary<Type, string> _headers = new();
    private static readonly ThreadLocal<int?> _depth = new(() => null);

    public override bool CanConvert(Type objectType) => typeof(NemConfig).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        _depth.Value = (_depth.Value ?? 0) + 1;
        try
        {
            if (_depth.Value > 1)
            {
                serializer.Serialize(writer, value);
                return;
            }
            
            var header = GetHeader(value.GetType());
            if (!string.IsNullOrEmpty(header))
            {
                writer.WriteRaw(header + "\n  ");
            }
            
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            
            var tempWriter = new System.IO.StringWriter();
            var tempSerializer = JsonSerializer.Create(settings);
            tempSerializer.Serialize(tempWriter, value);
            writer.WriteRaw(tempWriter.ToString());
        }
        finally
        {
            _depth.Value = (_depth.Value ?? 0) - 1;
        }
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
