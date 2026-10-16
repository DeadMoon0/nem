using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nem.Services;

/// <summary>Why a registry request produced no result.</summary>
public enum RegistryFailure
{
    /// <summary>The registry has no such package (HTTP 404).</summary>
    NotFound,
    /// <summary>The registry could not be reached (DNS, connection, timeout).</summary>
    Unreachable,
    /// <summary>The registry answered with a non-success status other than 404.</summary>
    BadStatus,
    /// <summary>The registry answered with a body that is not the expected document.</summary>
    InvalidResponse,
}

/// <summary>The result of one registry request: a value or a failure with a user-facing message.</summary>
public abstract record RegistryResult<T>
{
    private RegistryResult() { }

    public sealed record Ok(T Value) : RegistryResult<T>;

    public sealed record Failed(RegistryFailure Failure, string Message) : RegistryResult<T>;

    /// <summary>Handles both cases; the compiler ensures neither is forgotten.</summary>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<RegistryFailure, string, TOut> failed) => this switch
    {
        Ok o => ok(o.Value),
        Failed f => failed(f.Failure, f.Message),
        _ => throw new InvalidOperationException("RegistryResult has exactly two cases."),
    };
}

/// <summary>One advisory the registry reports for a package.</summary>
/// <param name="VulnerableVersions">The npm range of affected versions ("" when the registry gave none).</param>
public sealed record RegistryAdvisory(string Severity, string Title, string Url, string VulnerableVersions);

/// <summary>
/// The npm registry endpoints nem uses, over HTTP: package documents for version
/// resolution and the bulk advisory endpoint for audits. Everything that touches
/// the network lives here, so the logic on top of it stays testable.
/// </summary>
public sealed class NpmRegistryClient
{
    public const string DefaultRegistry = "https://registry.npmjs.org/";

    // Abbreviated packuments carry versions, engines and dist-tags at a fraction of
    // the full document's size; registries without them fall back to plain JSON.
    const string PackumentAccept = "application/vnd.npm.install-v1+json; q=1.0, application/json; q=0.8, */*";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    readonly Uri _registry;

    /// <param name="registry">The registry URL; null, blank or not an http(s) URL means the public registry.</param>
    public NpmRegistryClient(string? registry)
    {
        _registry = NormalizeRegistry(registry);
    }

    /// <summary>The registry base URL requests go to, always ending in '/'.</summary>
    public Uri Registry => _registry;

    internal static Uri NormalizeRegistry(string? registry)
    {
        string text = registry?.Trim() ?? "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new Uri(DefaultRegistry);
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    /// <summary>Fetches the package document of <paramref name="packageName"/>.</summary>
    public RegistryResult<Packument> FetchPackument(string packageName)
    {
        // The registry addresses a scoped package as one path segment: @scope%2Fname.
        var url = new Uri(_registry.AbsoluteUri + packageName.Replace("/", "%2F"));
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", PackumentAccept);

        return Send(request, $"package '{packageName}'").Match<RegistryResult<Packument>>(
            ok: body => Packument.Parse(body) is { } packument
                ? new RegistryResult<Packument>.Ok(packument)
                : new RegistryResult<Packument>.Failed(RegistryFailure.InvalidResponse, $"the registry at {_registry} answered with an invalid document for '{packageName}'"),
            failed: (failure, message) => new RegistryResult<Packument>.Failed(failure, message));
    }

    /// <summary>
    /// Asks the bulk advisory endpoint which of the installed versions have known
    /// vulnerabilities. <paramref name="installed"/> maps package name to versions on disk.
    /// </summary>
    public RegistryResult<IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>>> FetchBulkAdvisories(
        IReadOnlyDictionary<string, IReadOnlyList<string>> installed)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_registry, "-/npm/v1/security/advisories/bulk"))
        {
            Content = new StringContent(JsonConvert.SerializeObject(installed), Encoding.UTF8, "application/json"),
        };

        return Send(request, "the advisory endpoint").Match<RegistryResult<IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>>>>(
            ok: body => ParseAdvisories(body) is { } advisories
                ? new RegistryResult<IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>>>.Ok(advisories)
                : new RegistryResult<IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>>>.Failed(
                    RegistryFailure.InvalidResponse, $"the advisory endpoint of {_registry} answered with an invalid document"),
            failed: (failure, message) => new RegistryResult<IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>>>.Failed(failure, message));
    }

    /// <summary>Reads the bulk advisory answer: package name -> advisories. Null when it is not one.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>>? ParseAdvisories(string json)
    {
        JObject root;
        try
        {
            if (JToken.Parse(json) is not JObject parsed)
                return null;
            root = parsed;
        }
        catch (JsonException)
        {
            return null;
        }

        var result = new Dictionary<string, IReadOnlyList<RegistryAdvisory>>(StringComparer.Ordinal);
        foreach (JProperty package in root.Properties())
        {
            var advisories = new List<RegistryAdvisory>();
            foreach (JObject advisory in (package.Value as JArray)?.OfType<JObject>() ?? [])
            {
                advisories.Add(new RegistryAdvisory(
                    Str(advisory["severity"]) ?? "unknown",
                    Str(advisory["title"]) ?? "Unknown advisory",
                    Str(advisory["url"]) ?? "",
                    Str(advisory["vulnerable_versions"]) ?? ""));
            }
            result[package.Name] = advisories;
        }
        return result;
    }

    static string? Str(JToken? token) =>
        token?.Type == JTokenType.String ? token.Value<string>() : null;

    /// <summary>Sends the request and returns the body of a success response.</summary>
    static RegistryResult<string> Send(HttpRequestMessage request, string what)
    {
        try
        {
            using (request)
            using (HttpResponseMessage response = Http.Send(request))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new RegistryResult<string>.Failed(RegistryFailure.NotFound, $"{what} was not found at {request.RequestUri}");
                if (!response.IsSuccessStatusCode)
                    return new RegistryResult<string>.Failed(RegistryFailure.BadStatus, $"{request.RequestUri} answered HTTP {(int)response.StatusCode}");

                using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
                return new RegistryResult<string>.Ok(reader.ReadToEnd());
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return new RegistryResult<string>.Failed(RegistryFailure.Unreachable, $"could not reach {request.RequestUri}: {e.Message}");
        }
    }
}
