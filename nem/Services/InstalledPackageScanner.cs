using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nem.Services;

/// <summary>
/// Collects every package on disk below an env's global modules root, with the
/// version(s) actually installed, for the security audit. The npm and corepack
/// bundles that ship with Node itself are skipped: they are not the user's tools.
/// </summary>
public static class InstalledPackageScanner
{
    static readonly HashSet<string> NodeBundled = new(StringComparer.Ordinal) { "npm", "corepack" };

    // Nested node_modules levels walked below the root; real trees are far
    // shallower after npm's hoisting, the cap only stops pathological ones.
    const int MaxDepth = 8;

    /// <summary>Package name -> installed versions, both sorted ordinally.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Scan(string modulesRoot)
    {
        var found = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        Walk(modulesRoot, 0, found);
        return found.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList(), StringComparer.Ordinal);
    }

    static void Walk(string modulesDir, int depth, SortedDictionary<string, SortedSet<string>> found)
    {
        if (depth > MaxDepth)
            return;

        foreach (DirectoryInfo entry in RealSubdirectories(modulesDir))
        {
            if (entry.Name.StartsWith('@'))
            {
                // A scope folder: its packages live one level deeper.
                foreach (DirectoryInfo scoped in RealSubdirectories(entry.FullName).Where(d => !d.Name.StartsWith('.')))
                    Visit(scoped, depth, found);
            }
            else if (!entry.Name.StartsWith('.') && !NodeBundled.Contains(entry.Name))
            {
                Visit(entry, depth, found);
            }
        }
    }

    static void Visit(DirectoryInfo packageDir, int depth, SortedDictionary<string, SortedSet<string>> found)
    {
        if (ReadPackage(packageDir.FullName) is not { } package)
            return;

        (string name, string version) = package;

        if (!found.TryGetValue(name, out SortedSet<string>? versions))
            found[name] = versions = new SortedSet<string>(StringComparer.Ordinal);
        versions.Add(version);
        Walk(Path.Combine(packageDir.FullName, "node_modules"), depth + 1, found);
    }

    // Linked folders (npm link, junctions) are skipped: they can point back up the
    // tree, and a linked package is the user's own source, not an installed release.
    static IEnumerable<DirectoryInfo> RealSubdirectories(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateDirectories().Where(d => d.LinkTarget == null).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static (string Name, string Version)? ReadPackage(string dir)
    {
        try
        {
            JObject package = JObject.Parse(File.ReadAllText(Path.Combine(dir, "package.json")));
            return package["name"]?.Type == JTokenType.String && package["version"]?.Type == JTokenType.String
                ? (package.Value<string>("name")!, package.Value<string>("version")!)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // not a package folder
        }
    }
}
