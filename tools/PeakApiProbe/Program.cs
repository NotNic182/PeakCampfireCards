using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

// Reads metadata only: no game assembly is loaded/executed, no card or game state is changed.
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/PeakApiProbe -- <PEAK_Data/Managed> <report.json> [design/hooks.json]");
    return 2;
}
string managed = Path.GetFullPath(args[0]);
string gamePath = Path.Combine(managed, "Assembly-CSharp.dll");
if (!File.Exists(gamePath)) { Console.Error.WriteLine("Missing " + gamePath); return 2; }
using var game = AssemblyDefinition.ReadAssembly(gamePath);
var types = game.MainModule.Types.ToDictionary(t => t.FullName);
TypeDefinition? Find(string name) => types.TryGetValue(name, out var t) ? t : types.Values.FirstOrDefault(t => t.Name == name);
string Short(TypeReference t) => t.IsByReference ? Short(((ByReferenceType)t).ElementType) : t.Name switch
{
    "Single" => "float", "Boolean" => "bool", "Int32" => "int", "UInt16" => "ushort", "Byte" => "byte", "Double" => "double", "String" => "string", _ => t.Name
};
string Signature(MethodDefinition m) => string.Join(",", m.Parameters.Select(p => Short(p.ParameterType)));
bool HasMember(string type, string member)
{
    var t = Find(type);
    return t != null && (t.Fields.Any(f => f.Name == member) || t.Properties.Any(p => p.Name == member) || t.Methods.Any(m => m.Name == member));
}
object Capability(string name, string caveat, params (string type, string member)[] needs)
{
    var checks = needs.Select(n => new { type = n.type, member = n.member, present = HasMember(n.type, n.member) }).ToArray();
    return new { name, membersPresent = checks.All(c => c.present), caveat, checks };
}
var capabilities = new[]
{
    Capability("Living / ghost card eligibility", "Member presence; multiplayer timing is covered by session regression tests, not this probe.", ("Character", "data"), ("CharacterData", "dead")),
    Capability("Resize scout and inspect physics bodies", "Transform scale is a Unity API; camera, collider sizes, joints, climbing reach and remote rendering still need playtesting.", ("Character", "refs"), ("CharacterRagdoll", "colliderList"), ("CharacterRagdoll", "rigidbodies"), ("Bodypart", "Rig")),
    Capability("Increase maximum stamina", "Existing maxStamina is a flat addition. +1 doubles a clean 100-unit bar; it does not double the remaining capacity after afflictions.", ("Character", "GetMaxStamina"), ("CharacterData", "currentStamina"), ("CharacterAfflictions", "statusSum")),
    Capability("Identify physical impact source", "Requires a new hook and explicit object/direction/speed criteria. Generic Injury scaling cannot distinguish a falling object.", ("CollisionModifier", "Collide"), ("CollisionModifier", "onCollide"), ("CollisionModifier", "damage"), ("CharacterRagdoll", "TryGetCharacterFromCollider")),
    Capability("Immediate death / very large Injury", "These are different outcomes: Injury can cause a pass-out; ghost death must use PEAK's owner/network death flow. Not invoked by this probe.", ("Character", "DieInstantlyLocal"), ("Character", "RPCA_Die"), ("CharacterAfflictions", "AddStatus")),
    Capability("Custom gravity / fall control", "Rigid-body changes require live climbing, rope, carry and network checks; no gravity card is installed.", ("CharacterRagdoll", "rigidbodies"), ("Bodypart", "Rig")),
    Capability("Event-driven tradeoffs", "Existing ability handlers can combine these callbacks; every new trigger still needs logic and balancing.", ("Campfire", "Light_Rpc"), ("Character", "AddExtraStamina"), ("Character", "AddStamina"), ("CharacterAfflictions", "SubtractStatus")),
};
var hookChecks = new List<object>();
int missingHooks = 0;
if (args.Length > 2)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(args[2]));
    foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
    {
        if (row.GetProperty("kind").GetString() != "harmony") continue;
        string id = row.GetProperty("id").GetString()!;
        string type = row.GetProperty("targetType").GetString()!;
        string member = row.GetProperty("targetMember").GetString()!;
        string declared = row.GetProperty("signature").GetString()!.Replace(" ", "");
        string expected = string.Join(",", declared.Split(',').Select(p => p.Split('.').Last()));
        var methods = Find(type)?.Methods.Where(m => m.Name == member).ToArray() ?? Array.Empty<MethodDefinition>();
        // A '-' sheet signature generates a name-only Harmony patch, so it permits any overload.
        bool matches = declared is "-" or "" ? methods.Length > 0 : methods.Any(m => Signature(m) == expected);
        if (!matches) missingHooks++;
        hookChecks.Add(new { id, type, member, expected, matches, available = methods.Select(Signature).ToArray() });
    }
}
var report = new
{
    inspectedAtUtc = DateTime.UtcNow.ToString("O"),
    gameAssembly = gamePath,
    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gamePath))).ToLowerInvariant(),
    assemblyVersion = game.Name.Version.ToString(),
    validationLevel = "Metadata only. Neither PEAK nor Unity physics was run. Stripped/publicized references may differ from your installed 2.6.b files.",
    capabilities,
    missingHooks,
    hookChecks
};
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Wrote metadata report to {output}; {missingHooks} hook signature(s) unmatched. No gameplay effects applied.");
return missingHooks == 0 ? 0 : 1;
