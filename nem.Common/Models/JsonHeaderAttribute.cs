using System;

namespace nem.Common.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class JsonHeaderAttribute : Attribute
{
    public string Header { get; }

    public JsonHeaderAttribute(string header)
    {
        Header = header;
    }
}
