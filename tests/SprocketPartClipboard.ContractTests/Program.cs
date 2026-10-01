using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SprocketPartClipboard.Clipboard;
using SprocketPartClipboard.Designer;

var failures = 0;

string tempRoot = Path.Combine(Path.GetTempPath(), "SprocketPartClipboard.ContractTests");
if (Directory.Exists(tempRoot))
    Directory.Delete(tempRoot, recursive: true);
Directory.CreateDirectory(tempRoot);

Run("missing library file loads as an empty library", () =>
{
    ClipboardLibraryStore store = NewStore("missing");
    ClipboardLoadResult result = store.Load();

    Require(result.Library.Entries.Count == 0, "a missing file must load as an empty library");
    Require(result.Library.Active == null, "an empty library has no active entry");
    Require(!result.RecoveredFromCorruption, "a missing file is not corruption");
});

Run("entries survive a save and load round trip", () =>
{
    ClipboardLibraryStore store = NewStore("roundtrip");
    ClipboardLibrary library = new ClipboardLibrary();
    library.Add(Entry("a", "turretRing", "turret-payload"));
    library.Add(Entry("b", "crewSeat", "seat-payload"));
    store.Save(library);

    ClipboardLibrary reloaded = store.Load().Library;

    Require(reloaded.Entries.Count == 2, "both entries must survive the round trip");
    Require(reloaded.Entries[0].Id == "b", "entries are ordered newest first");
    Require(reloaded.Active != null && reloaded.Active.Id == "b", "the newest entry is active after a restart");
    Require(reloaded.Active != null && reloaded.Active.Payload == "seat-payload", "the payload must survive byte for byte");
    Require(reloaded.Entries[1].PartCount == 3, "entry numbers must survive the round trip");
});

Run("unknown keys survive a load and save cycle", () =>
{
    ClipboardLibraryStore store = NewStore("unknown-keys");
    File.WriteAllText(
        store.FilePath,
        "{\"SchemaVersion\":1,\"ActiveId\":\"\",\"Entries\":[],\"FutureField\":{\"nested\":[1,2,3]}}");

    ClipboardLibrary library = store.Load().Library;
    library.Add(Entry("a", "turretRing", "payload"));
    store.Save(library);

    string written = File.ReadAllText(store.FilePath);
    Require(written.Contains("FutureField", StringComparison.Ordinal), "an unknown root key must be preserved");
    Require(written.Contains("nested", StringComparison.Ordinal), "an unknown key's nested value must be preserved");
});

Run("a corrupted library is backed up and reset", () =>
{
    ClipboardLibraryStore store = NewStore("corrupt");
    File.WriteAllText(store.FilePath, "{ this is not json");

    ClipboardLoadResult result = store.Load();

    Require(result.RecoveredFromCorruption, "a parse failure must report recovery");
    Require(result.BackupPath != null && File.Exists(result.BackupPath), "the corrupt file must be kept on disk");
    Require(result.Library.Entries.Count == 0, "recovery starts from an empty library");
    Require(!File.Exists(store.FilePath), "the corrupt file is moved aside rather than left in place");
});

Run("a successful save leaves no temporary file behind", () =>
{
    ClipboardLibraryStore store = NewStore("atomic");
    ClipboardLibrary library = new ClipboardLibrary();
    library.Add(Entry("a", "turretRing", "payload"));
    store.Save(library);

    Require(File.Exists(store.FilePath), "the library must exist after a save");
    Require(!File.Exists(store.FilePath + ".tmp"), "no .tmp may remain after a save");
    Require(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!).Length == 1, "the directory holds only the library");
});

Run("a locked library reports the failure without discarding it", () =>
{
    ClipboardLibraryStore store = NewStore("locked");
    ClipboardLibrary library = new ClipboardLibrary();
    library.Add(Entry("a", "turretRing", "payload"));
    store.Save(library);

    using (FileStream exclusive = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        ClipboardLoadResult result = store.Load();

        Require(result.Error != null, "a read failure must be reported");
        Require(!result.RecoveredFromCorruption, "a read failure is not corruption");
        Require(result.Library.Entries.Count == 0, "an unreadable library yields an empty session");
    }

    Require(File.Exists(store.FilePath), "a read failure leaves the library on disk");
    Require(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!).Length == 1, "a read failure leaves no backup");
});

Run("the library keeps only the newest entries", () =>
{
    ClipboardLibrary library = new ClipboardLibrary();
    for (int index = 0; index < ClipboardLibrary.MaxEntries + 5; index++)
        library.Add(Entry($"id-{index}", $"part-{index}", "payload"));

    Require(library.Entries.Count == ClipboardLibrary.MaxEntries, "the library is capped");
    Require(library.Entries[0].Id == $"id-{ClipboardLibrary.MaxEntries + 4}", "the newest entry stays first");
    Require(library.Find("id-0") == null, "the oldest entries are dropped first");
});

Run("selecting and removing entries keeps the active pointer valid", () =>
{
    ClipboardLibrary library = new ClipboardLibrary();
    library.Add(Entry("a", "turretRing", "payload"));
    library.Add(Entry("b", "crewSeat", "payload"));

    Require(library.Select("a"), "an existing entry can be selected");
    Require(library.Active != null && library.Active.Id == "a", "selection changes the active entry");
    Require(!library.Select("missing"), "an unknown id cannot be selected");
    Require(library.Active != null && library.Active.Id == "a", "a failed selection leaves the active entry alone");

    Require(library.Remove("a"), "an existing entry can be removed");
    Require(library.Active != null && library.Active.Id == "b", "removing the active entry falls back to the newest");
    Require(!library.Remove("a"), "removing an unknown entry reports failure");
});

Run("a library whose active id is unknown falls back to the newest entry", () =>
{
    ClipboardLibraryStore store = NewStore("stale-active");
    ClipboardLibrary library = new ClipboardLibrary();
    library.Add(Entry("a", "turretRing", "payload"));
    library.ActiveId = "gone";
    store.Save(library);

    ClipboardLibrary reloaded = store.Load().Library;

    Require(reloaded.ActiveId == "a", "a stale active id is replaced by the newest entry");
    Require(reloaded.Active != null && reloaded.Active.Id == "a", "the newest entry becomes active");
});

Run("the library path matches the documented user-data layout", () =>
{
    string path = ClipboardLibraryStore.DefaultFilePath(@"C:\Users\someone\AppData\LocalLow\HD\Sprocket");
    Require(
        path == @"C:\Users\someone\AppData\LocalLow\HD\Sprocket\SprocketPartClipboard\library.json",
        $"unexpected library path: {path}");
});

Run("the written library uses the documented field names", () =>
{
    ClipboardLibraryStore store = NewStore("schema");
    ClipboardLibrary library = new ClipboardLibrary();
    library.Add(Entry("a", "turretRing", "payload"));
    store.Save(library);

    string written = File.ReadAllText(store.FilePath);
    foreach (string field in new[]
    {
        "\"SchemaVersion\"", "\"ActiveId\"", "\"Entries\"", "\"Id\"", "\"Name\"",
        "\"CapturedUtc\"", "\"GameVersion\"", "\"SourceVehicle\"", "\"RootPartId\"",
        "\"PartCount\"", "\"Payload\"",
    })
    {
        Require(written.Contains(field, StringComparison.Ordinal), $"the library is missing {field}");
    }
});

Run("payload id shift only rewrites references to known definitions", () =>
{
    const string payload =
        "{\"v\":\"2.0\"," +
        "\"blueprints\":[" +
        "{\"id\":41,\"type\":\"turretRing\",\"blueprint\":{\"v\":1.0,\"TraverseMotorVUID\":266}}," +
        "{\"id\":44,\"type\":\"turretBasket\",\"blueprint\":{\"v\":1.0}}," +
        "{\"id\":3,\"type\":\"structure\",\"blueprint\":{\"v\":1.0,\"bodyMeshVuid\":0}}]," +
        "\"objects\":[{" +
        "\"guid\":\"99281776-6b29-4ffb-9d8b-04139ca7b6a2\"," +
        "\"vuid\":260,\"pvuid\":0,\"flags\":41,\"turretRing\":261,\"structureID\":268," +
        "\"ringBlueprintVuid\":41,\"basketBlueprintVuid\":44,\"traverseConstraintsVuid\":266}," +
        "{\"guid\":\"7f8a9d20-eb45-482e-b149-014c964c4e2c\",\"vuid\":268,\"pvuid\":260}]," +
        "\"meshes\":[{\"vuid\":0,\"type\":\"plateStructureMesh\"},{\"vuid\":1,\"type\":\"plateStructureMesh\"}]}";

    IReadOnlyList<int> ids = PayloadIdShift.DefinitionIds(payload);
    IReadOnlyList<int> meshIds = PayloadIdShift.MeshIds(payload);
    Require(ids.Count == 3 && ids[0] == 41 && ids[1] == 44, "definition ids must be read from blueprints[]");
    Require(meshIds.Count == 2 && meshIds[0] == 0 && meshIds[1] == 1, "mesh ids must be read from meshes[]");

    PayloadIdShift.ShiftResult shiftedResult = PayloadIdShift.Shift(
        payload, ids, meshIds, new[] { 261, 269, 350 }, blueprintOffset: 10000, meshOffset: 10500, objectOffset: 100000);
    using JsonDocument shifted = JsonDocument.Parse(shiftedResult.Payload);
    JsonElement root = shifted.RootElement;
    JsonElement item = root.GetProperty("objects")[0];

    Require(item.GetProperty("ringBlueprintVuid").GetInt32() == 10041, "a definition reference must shift");
    Require(item.GetProperty("basketBlueprintVuid").GetInt32() == 10044, "every definition reference must shift");
    Require(item.GetProperty("vuid").GetInt32() == 100260, "the part's own vuid shifts with the part range");
    Require(item.GetProperty("pvuid").GetInt32() == 0, "the no-parent sentinel must never shift");
    Require(item.GetProperty("turretRing").GetInt32() == 100261, "a component vuid shifts with the part range");
    Require(item.GetProperty("structureID").GetInt32() == 100268, "a reference held under an id key shifts");
    Require(item.GetProperty("flags").GetInt32() == 41, "a plain int equal to a definition id must stay untouched");
    Require(
        item.GetProperty("guid").GetString() == "99281776-6b29-4ffb-9d8b-04139ca7b6a2",
        "the asset guid must stay untouched");
    Require(
        item.GetProperty("traverseConstraintsVuid").GetInt32() == 100266,
        "a vuid outside both known sets shifts with the part range");
    Require(
        root.GetProperty("blueprints")[0].GetProperty("blueprint").GetProperty("TraverseMotorVUID").GetInt32() == 100266,
        "nested vuid references shift with the same rule as their siblings");
    Require(
        root.GetProperty("blueprints")[0].GetProperty("id").GetInt32() == 10041,
        "definition ids shift so the objects and the definitions stay consistent");
    Require(root.GetProperty("meshes")[0].GetProperty("vuid").GetInt32() == 10500, "mesh id 0 is a real id, not a sentinel");
    Require(root.GetProperty("meshes")[1].GetProperty("vuid").GetInt32() == 10501, "mesh ids shift with the mesh range");
    Require(
        root.GetProperty("blueprints")[2].GetProperty("blueprint").GetProperty("bodyMeshVuid").GetInt32() == 10500,
        "a mesh reference of 0 must follow the mesh id it points at");
});

// 键名没有号段提示的引用只能指部件或组件；定义编号从 0 起，与部件/组件编号必然重叠。
// 炮塔环载荷里 `mantletBlueprint.trunnions_vuid` 的 300 同时是 blueprints[] 里一条定义的 id，
// 先问定义集合就会把它改到定义号段上，粘贴时 Mantlet 按号取不到 RotationRangeArea 组件，
// 落到抛 NullReferenceException 的尾部、整次粘贴中断。
Run("a component reference that collides with a definition id follows the component range", () =>
{
    const string payload =
        "{\"v\":\"2.0\"," +
        "\"blueprints\":[" +
        "{\"id\":41,\"type\":\"layingDrive\",\"blueprint\":{\"v\":1.0}}," +
        "{\"id\":9,\"type\":\"structure\",\"blueprint\":{\"v\":1.0,\"bodyMeshVuid\":70}}]," +
        "\"objects\":[" +
        "{\"guid\":\"g0\",\"vuid\":260,\"pvuid\":0,\"flags\":2,\"motorBlueprintVuid\":41," +
        "\"traverseConstraintsVuid\":9," +
        "\"mantletBlueprint\":{\"drive_vuid\":41,\"trunnions_vuid\":70,\"shield_vuid\":-1}}," +
        "{\"guid\":\"g1\",\"vuid\":269,\"pvuid\":260,\"flags\":2,\"layingDrive\":41,\"trunnions\":70}]," +
        "\"meshes\":[{\"vuid\":70,\"type\":\"plateStructureMesh\"}]}";

    // 条目没有记录组件编号：组件集合完全由载荷自己的 objects[] 顶层组件键给出。
    PayloadIdShift.ShiftResult shiftedResult = PayloadIdShift.Shift(
        payload,
        PayloadIdShift.DefinitionIds(payload),
        PayloadIdShift.MeshIds(payload),
        Array.Empty<int>(),
        blueprintOffset: 10000, meshOffset: 10500, objectOffset: 100000);

    using JsonDocument shifted = JsonDocument.Parse(shiftedResult.Payload);
    JsonElement root = shifted.RootElement;
    JsonElement mantlet = root.GetProperty("objects")[0];
    JsonElement trunnions = root.GetProperty("objects")[1];
    JsonElement state = mantlet.GetProperty("mantletBlueprint");

    Require(
        state.GetProperty("drive_vuid").GetInt32() == 100041,
        "a component reference must follow the component range even when a blueprint carries the same id");
    Require(
        state.GetProperty("trunnions_vuid").GetInt32() == 100070,
        "a component reference must follow the component range even when a mesh carries the same id");
    Require(
        trunnions.GetProperty("layingDrive").GetInt32() == 100041
        && trunnions.GetProperty("trunnions").GetInt32() == 100070,
        "the components a state block refers to must land on the same ids as the components themselves");
    Require(
        mantlet.GetProperty("motorBlueprintVuid").GetInt32() == 10041,
        "a key that names the blueprint range still shifts with the blueprint range");
    Require(
        mantlet.GetProperty("traverseConstraintsVuid").GetInt32() == 10009,
        "a reference that is not a part, a component, a mesh or an object of this payload keeps the definition range");
    Require(state.GetProperty("shield_vuid").GetInt32() == -1, "the no-shield sentinel must never shift");
    Require(root.GetProperty("blueprints")[1].GetProperty("blueprint").GetProperty("bodyMeshVuid").GetInt32() == 10570,
        "a key that names the mesh range still shifts with the mesh range");
    Require(root.GetProperty("meshes")[0].GetProperty("vuid").GetInt32() == 10570,
        "the mesh a bodyMeshVuid points at must land on the same id as the mesh itself");
    Require(root.GetProperty("blueprints")[0].GetProperty("id").GetInt32() == 10041,
        "definition ids still shift so the objects and the definitions stay consistent");
});

Run("a recorded component number still decides when the payload declares no component key", () =>
{
    const string payload =
        "{\"blueprints\":[{\"id\":350,\"type\":\"rotationRangeArea\",\"blueprint\":{\"v\":1.0}}]," +
        "\"objects\":[{\"guid\":\"g0\",\"vuid\":260,\"pvuid\":0,\"flags\":2," +
        "\"mantletBlueprint\":{\"drive_vuid\":350,\"trunnions_vuid\":-1,\"shield_vuid\":-1}}]," +
        "\"meshes\":[]}";

    PayloadIdShift.ShiftResult shiftedResult = PayloadIdShift.Shift(
        payload,
        PayloadIdShift.DefinitionIds(payload),
        PayloadIdShift.MeshIds(payload),
        new[] { 350 },
        blueprintOffset: 10000, meshOffset: 10500, objectOffset: 100000);

    using JsonDocument shifted = JsonDocument.Parse(shiftedResult.Payload);
    Require(
        shifted.RootElement.GetProperty("objects")[0].GetProperty("mantletBlueprint").GetProperty("drive_vuid").GetInt32()
            == 100350,
        "a component number recorded at copy time must still decide the range");
});

// 引用也会以整数数组的形式出现，键名不带 Vuid 后缀。漏改的后果是复制件"认领"原车的组件：
// `CannonBarrelSegment.SetSegmentCount`（RVA 0x19C6D40）拿 `barrelVuids` 的元素在载具里
// 按号解析出**既有**的炮管段，再把它挂到本炮名下；`operatedBehaviours` 同理
// （`CrewSeatBlueprint.OperatedBehaviourIDs`，`VUID[]`）。
Run("a reference list shifts every element into the component range", () =>
{
    const string payload =
        "{\"v\":\"2.0\"," +
        "\"blueprints\":[" +
        "{\"id\":41,\"type\":\"cannonInstance\",\"blueprint\":{\"v\":\"0.2\",\"sightVuid\":435," +
        "\"linkedCannonVuid\":-1,\"barrelVuids\":[41,70]}}," +
        "{\"id\":9,\"type\":\"crewSeat\",\"blueprint\":{\"v\":\"0.3\",\"operatedBehaviours\":[9,70]}}]," +
        "\"objects\":[{\"guid\":\"g0\",\"vuid\":260,\"pvuid\":0,\"flags\":2," +
        "\"layingDrive\":41,\"trunnions\":70}]," +
        "\"meshes\":[{\"vuid\":70,\"type\":\"plateStructureMesh\"}]}";

    PayloadIdShift.ShiftResult shiftedResult = PayloadIdShift.Shift(
        payload,
        PayloadIdShift.DefinitionIds(payload),
        PayloadIdShift.MeshIds(payload),
        Array.Empty<int>(),
        blueprintOffset: 10000, meshOffset: 10500, objectOffset: 100000);

    using JsonDocument shifted = JsonDocument.Parse(shiftedResult.Payload);
    JsonElement root = shifted.RootElement;
    JsonElement cannonInstance = root.GetProperty("blueprints")[0].GetProperty("blueprint");
    JsonElement crewSeat = root.GetProperty("blueprints")[1].GetProperty("blueprint");

    Require(
        cannonInstance.GetProperty("barrelVuids")[0].GetInt32() == 100041
        && cannonInstance.GetProperty("barrelVuids")[1].GetInt32() == 100070,
        "every barrel segment number must follow the component range, even when a blueprint carries the same id");
    Require(
        crewSeat.GetProperty("operatedBehaviours")[0].GetInt32() == 100009
        && crewSeat.GetProperty("operatedBehaviours")[1].GetInt32() == 100070,
        "every operated behaviour must follow the component range");
    Require(
        cannonInstance.GetProperty("sightVuid").GetInt32() == 100435,
        "a scalar vuid next to a list still shifts with the part range");
    Require(
        cannonInstance.GetProperty("linkedCannonVuid").GetInt32() == -1,
        "a negative list-free sentinel must never shift");
});

Run("mesh topology and paint job lists are not reference lists", () =>
{
    const string payload =
        "{\"blueprints\":[{\"id\":20,\"type\":\"paintJobRegister\",\"blueprint\":{\"v\":\"0.0\",\"paintJobIDs\":[1,2]}}]," +
        "\"objects\":[]," +
        "\"meshes\":[{\"vuid\":0,\"type\":\"plateStructureMesh\",\"meshData\":{\"v\":1.0," +
        "\"mesh\":{\"edges\":[0,1,2],\"edgeFlags\":[0],\"faces\":[{\"v\":[0,1,2],\"t\":[2,1,0]}],\"vertices\":[1,2,3]}}}]}";

    PayloadIdShift.ShiftResult shiftedResult = PayloadIdShift.Shift(
        payload,
        PayloadIdShift.DefinitionIds(payload),
        PayloadIdShift.MeshIds(payload),
        Array.Empty<int>(),
        blueprintOffset: 10000, meshOffset: 10500, objectOffset: 100000);

    using JsonDocument shifted = JsonDocument.Parse(shiftedResult.Payload);
    JsonElement root = shifted.RootElement;
    JsonElement mesh = root.GetProperty("meshes")[0].GetProperty("meshData").GetProperty("mesh");

    Require(
        string.Join(",", mesh.GetProperty("edges").EnumerateArray().Select(item => item.GetInt32())) == "0,1,2",
        "mesh edge indices are topology, not references");
    Require(
        string.Join(",", mesh.GetProperty("faces")[0].GetProperty("v").EnumerateArray().Select(item => item.GetInt32()))
            == "0,1,2",
        "face vertex indices are topology, not references");
    Require(
        string.Join(",", mesh.GetProperty("faces")[0].GetProperty("t").EnumerateArray().Select(item => item.GetInt32()))
            == "2,1,0",
        "face texture indices are topology, not references");
    Require(
        string.Join(",", root.GetProperty("blueprints")[0].GetProperty("blueprint").GetProperty("paintJobIDs")
            .EnumerateArray().Select(item => item.GetInt32())) == "1,2",
        "paint job numbers are registry indices, not references");
});

Run("payload id shift leaves malformed input alone", () =>
{
    const string broken = "{ this is not json";
    Require(PayloadIdShift.DefinitionIds(broken).Count == 0, "a malformed payload has no definition ids");
    Require(
        PayloadIdShift.Shift(broken, new[] { 41 }, new[] { 1 }, Array.Empty<int>(), 10000, 10500, 100000).Payload
            == broken,
        "a malformed payload is returned as-is");
    Require(
        PayloadIdShift.Shift(
            "{\"objects\":[]}", Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), 10000, 10500, 100000).Payload
            != "",
        "an empty definition set still yields a payload");
});

if (failures > 0)
{
    Console.WriteLine($"FAIL {failures} contract check(s) failed.");
    return 1;
}

Console.WriteLine("PASS SprocketPartClipboard contract checks.");
return 0;

ClipboardLibraryStore NewStore(string name)
{
    string directory = Path.Combine(tempRoot, name);
    Directory.CreateDirectory(directory);
    return new ClipboardLibraryStore(Path.Combine(directory, ClipboardLibraryStore.FileName));
}

static ClipboardEntry Entry(string id, string name, string payload)
{
    return new ClipboardEntry
    {
        Id = id,
        Name = name,
        CapturedUtc = "2026-09-29T00:00:00.0000000Z",
        GameVersion = "0.2.53.2",
        SourceVehicle = "untitled",
        RootPartId = name,
        PartCount = 3,
        Payload = payload,
    };
}

void Run(string name, Action check)
{
    try
    {
        check();
        Console.WriteLine($"ok   {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
