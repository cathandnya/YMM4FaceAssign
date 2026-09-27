using System.Reflection;

// Usage: ApiProbe <ymm4dir> <TypeFullName>[:member,member,...] ...
var dir = args[0];
// YMM4 is a self-contained deployment, so its folder already carries the whole BCL.
// Mixing in the host runtime folder would load mscorlib twice.
var paths = Directory.GetFiles(dir, "*.dll").ToList();

var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver);

var loaded = new List<Assembly>();
foreach (var name in new[] { "YukkuriMovieMaker.dll", "YukkuriMovieMaker.Plugin.dll", "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.dll" })
{
    var p = Path.Combine(dir, name);
    if (File.Exists(p)) loaded.Add(mlc.LoadFromAssemblyPath(p));
}

Type Find(string full)
{
    foreach (var a in loaded)
    {
        var t = a.GetType(full, false);
        if (t != null) return t;
    }
    return null;
}

foreach (var spec in args.Skip(1))
{
    if (spec.StartsWith("grep=", StringComparison.Ordinal))
    {
        var needle = spec["grep=".Length..];
        Console.WriteLine($"==== types matching '{needle}' ====");
        foreach (var a in loaded)
            foreach (var ty in a.GetTypes())
                if (ty.IsPublic && ty.FullName != null && ty.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"  {ty.FullName}");
        continue;
    }

    var includeNonPublic = spec.StartsWith("np:", StringComparison.Ordinal);
    var body = includeNonPublic ? spec["np:".Length..] : spec;
    var parts = body.Split(':', 2);
    var t = Find(parts[0]);
    Console.WriteLine($"==== {parts[0]} ====");
    if (t == null) { Console.WriteLine("  TYPE NOT FOUND"); continue; }
    Console.WriteLine($"  attributes: {t.Attributes}");
    Console.WriteLine($"  base: {t.BaseType?.FullName}");
    Console.WriteLine($"  interfaces: {string.Join(", ", t.GetInterfaces().Select(i => i.Name))}");

    var wanted = parts.Length > 1 && parts[1].Length > 0
        ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
        : null;

    var F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    if (includeNonPublic) F |= BindingFlags.NonPublic;

    foreach (var f in t.GetFields(F))
    {
        if (wanted != null && !wanted.Contains(f.Name)) continue;
        Console.WriteLine($"  FIELD {f.FieldType.Name} {f.Name} [{(f.IsPublic ? "public" : "non-public")}]");
    }

    foreach (var e in t.GetEvents(F))
        Console.WriteLine($"  EVENT {e.EventHandlerType?.Name} {e.Name}");

    foreach (var c in t.GetConstructors(F))
        Console.WriteLine($"  ctor({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");

    foreach (var p in t.GetProperties(F))
    {
        if (wanted != null && !wanted.Contains(p.Name)) continue;
        var acc = (p.GetMethod != null ? "get" : "") + (p.SetMethod != null ? ";set" : "");
        Console.WriteLine($"  PROP {p.PropertyType.Name} {p.Name} [{acc}]");
    }

    foreach (var m in t.GetMethods(F))
    {
        if (m.IsSpecialName) continue;
        if (wanted != null && !wanted.Contains(m.Name)) continue;
        var g = m.IsGenericMethodDefinition ? "<" + string.Join(",", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
        Console.WriteLine($"  METH {m.ReturnType.Name} {m.Name}{g}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
    }
}
