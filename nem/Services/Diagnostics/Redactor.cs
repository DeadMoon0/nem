using System.Text.RegularExpressions;

namespace nem.Services.Diagnostics;

/// <summary>
/// Removes secrets from text before it is logged or put into a report: npm auth
/// settings, passwords and tokens, and credentials inside URLs. Paths and user
/// names stay - they are often exactly what a problem is about.
/// </summary>
public static class Redactor
{
    public const string Mask = "***";

    // key=value, key: value or "key": "value" for keys that hold secrets, as npm
    // and JSON write them ('//registry.example/:_authToken=...', '"password": "..."').
    static readonly Regex SecretSetting = new(
        @"(?<key>_authToken|_auth|_password|password|passwd|token|secret|apikey|api_key)(?<sep>""?\s*[=:]\s*""?)(?<value>[^\s""',;&]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // https://user:password@host - the part before '@'.
    static readonly Regex UrlCredentials = new(
        @"(?<scheme>[a-z][a-z0-9+.-]*://)[^/\s@""']+@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string text) =>
        UrlCredentials.Replace(
            SecretSetting.Replace(text, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask),
            m => m.Groups["scheme"].Value + Mask + "@");
}
