"""Run pure data regressions from the window source without starting Unity.
Requires Python 3, .NET SDK 10, and the project's cached Newtonsoft.Json DLL.
GUI/Editor lifecycle behavior still requires a Unity smoke test.
"""
from pathlib import Path
import subprocess
import tempfile
import html

package = Path(__file__).resolve().parents[1]
project = package.parents[1]
source = (package / 'Editor/Data/DreamyDataDebuggerWindow.cs').read_text()


def method(name):
    # Extract the complete declaration up to the following method declaration.
    start = source.index('        private ' + name)
    end = source.find('\n        private ', start + 1)
    return source[start:end].rstrip()


methods = '\n'.join(method(name) for name in (
    'bool TryReadPayload(', 'string BuildSaveOutput(', 'JArray FindArrayByPath(',
    'List<string> GetColumns(', 'bool IsObjectTable(', 'List<int> GetVisibleRows(', 'void OnTokenChanged(',
    'JToken ExpandedContent(', 'int InlinePageStart(', 'float ExpandedHeight(', 'float InlineHeight(', 'float TableRowHeight(',
    'static string GetPrimarySavePath(', 'static int DeleteSaveFilesForTesting('
))
reference = next((project / 'Library/PackageCache').glob('com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll'))
code = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
class Regression
{
    const float RowHeight = 28;
    const int InlinePageSize = 12;
    readonly HashSet<JToken> expandedValues = new HashSet<JToken>();
    readonly Dictionary<JArray,int> inlinePages = new Dictionary<JArray,int>();
    string originalRawFile;
    bool originalPayloadWasString;
    enum MessageType { None }
    void MarkDirty() { }
    void SetStatus(string s, MessageType m) { }
    sealed class InspectionFrame { public JToken Token; public JValue EncodedSource; }
    readonly List<InspectionFrame> inspection = new List<InspectionFrame>();
    readonly Dictionary<string,JArray> drawnArrays = new Dictionary<string,JArray>();
    readonly Dictionary<JArray,List<string>> cachedColumns = new Dictionary<JArray,List<string>>();
    readonly Dictionary<JArray,List<int>> cachedRows = new Dictionary<JArray,List<int>>();
    readonly Dictionary<JArray,string> cachedFilters = new Dictionary<JArray,string>();
    readonly Dictionary<string,string> tableFilter = new Dictionary<string,string>();
    static void Check(bool condition, string test) { if (!condition) throw new Exception(test); Console.WriteLine("PASS " + test); }
    static void Main() { new Regression().Run(); }
    void Run()
    {
        foreach (bool encoded in new[] { false, true })
        {
            var payload = JObject.Parse("{\"items\":[{\"id\":\"one\",\"list\":[1,2]}]}");
            var envelope = new JObject { ["Version"] = 7, ["Payload"] = encoded ? new JValue(payload.ToString(Formatting.None)) : (JToken)payload };
            originalRawFile = envelope.ToString();
            Check(TryReadPayload(originalRawFile, out JToken loaded, out string error), "Read envelope " + encoded);
            ((JArray)loaded["items"][0]["list"]).Add(3);
            var output = JObject.Parse(BuildSaveOutput(loaded));
            Check(output["Version"].Value<int>() == 7, "Preserve metadata " + encoded);
            Check(output["Payload"].Type == (encoded ? JTokenType.String : JTokenType.Object), "Preserve payload type " + encoded);
            var saved = encoded ? JToken.Parse(output["Payload"].Value<string>()) : output["Payload"];
            Check(saved["items"][0]["list"].Count() == 3, "Persist nested list " + encoded);
        }
        Check(!TryReadPayload("broken", out _, out _), "Reject invalid save JSON");
        var root = JObject.Parse("{\"a.b\":{\"list\":[1,2]}}");
        var nested = (JArray)root["a.b"]["list"];
        drawnArrays["root['a.b'].list"] = nested;
        Check(ReferenceEquals(FindArrayByPath(root, "root['a.b'].list"), nested), "Resolve list under dotted key");
        drawnArrays["root"] = nested;
        Check(ReferenceEquals(FindArrayByPath(root, "root"), nested), "Resolve inspected encoded root list");
        var rows = JArray.Parse("[{\"id\":\"one\"},{\"id\":\"two\"}]");
        tableFilter["rows"] = "one";
        Check(GetVisibleRows(rows, "rows").SequenceEqual(new[] { 0 }), "Filter rows");
        GetColumns(rows);
        rows[1]["newField"] = true;
        rows[1]["id"] = "one";
        OnTokenChanged();
        Check(GetColumns(rows).Contains("newField"), "Invalidate column cache on mutation");
        Check(GetVisibleRows(rows, "rows").Count == 2, "Invalidate filter cache on mutation");
        var encodedOuter = new JValue("{}");
        var decodedOuter = JObject.Parse("{\"inner\":\"[1]\"}");
        var encodedInner = (JValue)decodedOuter["inner"];
        var decodedInner = JArray.Parse("[1]");
        inspection.Add(new InspectionFrame { Token = decodedOuter, EncodedSource = encodedOuter });
        inspection.Add(new InspectionFrame { Token = decodedInner, EncodedSource = encodedInner });
        decodedInner.Add(2);
        OnTokenChanged();
        var persistedInner = JToken.Parse(JToken.Parse(encodedOuter.Value<string>())["inner"].Value<string>());
        Check(persistedInner.Count() == 2, "Write nested encoded JSON from inner to outer");
        var inlineList = new JArray(Enumerable.Range(0, 40).Select(x => new JValue(x)));
        Check(ExpandedHeight(inlineList) == 0, "Collapsed list reserves no space");
        expandedValues.Add(inlineList);
        Check(ExpandedHeight(inlineList) == 14 * RowHeight + 8, "Inline list height includes title, headers and one page");
        inlinePages[inlineList] = 99;
        Check(InlinePageStart(inlineList) == 36, "Clamp inline page after list size changes");
        Check(ExpandedHeight(inlineList) == 6 * RowHeight + 8, "Last inline page reserves exact height");
        var inlineRow = new JObject { ["list"] = inlineList };
        Check(TableRowHeight(inlineRow, true) == RowHeight + 1 + ExpandedHeight(inlineList), "Parent row reserves inline details height");
        var wide = JArray.Parse("[{\"a\":1,\"b\":2,\"c\":3,\"d\":4,\"e\":5}]");
        Check(InlineHeight(wide, 0) == 8 * RowHeight, "Wide items reserve vertical field rows");
        Check(InlineHeight(inlineRow, 10) == RowHeight, "Bound inline nesting depth");
        expandedValues.Remove(inlineList);
        Check(TableRowHeight(inlineRow, true) == RowHeight + 1, "Collapsing restores parent row height");
        string testDirectory = Path.Combine(Path.GetTempPath(), "dreamy-save-reset-" + Guid.NewGuid());
        Directory.CreateDirectory(testDirectory);
        try
        {
            string save = Path.Combine(testDirectory, "player[1].json");
            string other = Path.Combine(testDirectory, "player[1].json-other");
            foreach (string target in new[] { save, save + ".bak", save + ".tmp", save + ".bak-20261006", save + ".bak-20261007", other, other + ".bak" })
                File.WriteAllText(target, "saved data");
            Check(DeleteSaveFilesForTesting(save) == 5, "Reset removes primary, runtime backup, temp and Editor backups");
            Check(!File.Exists(save) && !File.Exists(save + ".bak"), "Reset leaves no recovery source for the selected save");
            Check(Directory.GetFiles(testDirectory).Length == 2 && File.Exists(other) && File.Exists(other + ".bak"), "Reset preserves neighboring save and its production backup");
            Check(DeleteSaveFilesForTesting(save) == 0, "Repeated reset creates no backup");
            File.WriteAllText(save + ".bak", "orphan runtime backup");
            File.WriteAllText(save + ".bak-20261008", "orphan Editor backup");
            Check(GetPrimarySavePath(save + ".bak") == save && GetPrimarySavePath(save + ".bak-20261008") == save && GetPrimarySavePath(save + ".tmp") == save, "Discover backup-only saves by primary path");
            Check(DeleteSaveFilesForTesting(save) == 2, "Reset removes orphan backups without a primary file");
        }
        finally { Directory.Delete(testDirectory, true); }
        Console.WriteLine("All data debugger regressions passed.");
    }
'''
with tempfile.TemporaryDirectory(prefix='dreamy-editor-tests-') as temp:
    folder = Path(temp)
    (folder / 'Regression.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>' + html.escape(str(reference)) + '</HintPath></Reference></ItemGroup></Project>')
    (folder / 'Program.cs').write_text(code + methods + '\n}\n')
    subprocess.run(['dotnet', 'run', '--project', str(folder / 'Regression.csproj'), '--verbosity', 'quiet'], check=True)
