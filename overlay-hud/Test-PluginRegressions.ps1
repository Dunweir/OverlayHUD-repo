$ErrorActionPreference = 'Stop'
# Run the actual plugin methods with small runtime doubles; no Unity player required.
$source = Get-Content (Join-Path $PSScriptRoot 'Plugin.cs') -Raw
function Get-PluginMethod([string] $name) {
    $match = [regex]::Match($source, '(?m)^        private [^\r\n]*\b' + $name + '\(')
    if (-not $match.Success) { throw "Method not found: $name" }
    $end = $source.IndexOf("`n        private ", $match.Index + $match.Length)
    if ($end -lt 0) { throw "Method end not found: $name" }
    return $source.Substring($match.Index, $end - $match.Index)
}
$methods = @('PhysGrabObjectBreakPostfix', 'PhysGrabObjectDestroyedPostfix', 'SyncMonsterRoster') |
    ForEach-Object { Get-PluginMethod $_ }
$harness = @'
using System;
using System.Collections.Generic;
using System.Text;
public class PluginRegressionTests
{
    class Component
    {
        public int Id;
        public bool Dead, InHaul;
        public float Current = 80, Original = 100;
        public int GetInstanceID() { return Id; }
    }
    class EnemyCandidate { public Component Root; }
    class ResolvedEnemyCandidate { public EnemyCandidate Candidate; public string MonsterName; }
    static PluginRegressionTests instance;
    bool rosterPublished;
    int rosterStableScans, reports;
    string pendingRosterFingerprint = "", lastRosterFingerprint = "";
    float mapValue = 1000, lostValue;
    readonly List<string> posts = new List<string>();
    static bool IsRunLevel() { return true; }
    static Component GetValuableComponent(object source) { return source as Component; }
    static float ReadValuableCurrentValue(Component value) { return value.Current; }
    static float ReadValuableOriginalValue(Component value) { return value.Original; }
    static bool IsValuableInDollarHaul(Component value) { return value.InHaul; }
    static object ReadMember(object source, string name) { return ((Component)source).Dead; }
    void AddMapValue(float value, string reason) { mapValue += value; }
    void AddLostValue(float value, string reason) { lostValue += value; }
    void ReportDestroyedItem(object source) { reports++; }
    static string EscapeJson(string value) { return value; }
    string PostMonsterRoster(string json) { return json; }
    void StartCoroutine(string json) { posts.Add(json); }
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    static PluginRegressionTests Reset()
    {
        instance = new PluginRegressionTests();
        return instance;
    }
    public static void Run()
    {
        var p = Reset();
        PhysGrabObjectBreakPostfix(new Component { InHaul = true }, 20, true);
        Check(p.lostValue == 20 && p.mapValue == 980, "Damage during extraction counts");
        PhysGrabObjectBreakPostfix(new Component(), 20, false);
        Check(p.lostValue == 20 && p.mapValue == 980, "Cosmetic break does not count");

        p = Reset();
        PhysGrabObjectDestroyedPostfix(new Component { InHaul = true });
        Check(p.lostValue == 0 && p.mapValue == 920 && p.reports == 0,
            "Normal submitted valuable leaves map without loss or alert");

        p = Reset();
        PhysGrabObjectDestroyedPostfix(new Component { InHaul = true, Dead = true });
        Check(p.lostValue == 80 && p.mapValue == 920 && p.reports == 1,
            "Destroyed valuable in haul is a loss, not a submission");

        p = Reset();
        PhysGrabObjectDestroyedPostfix(new Component { Dead = true });
        Check(p.lostValue == 80 && p.mapValue == 920 && p.reports == 1,
            "Destroyed valuable outside haul counts");

        p = Reset();
        // BreakRPC replaces valueLost with the full pre-break remainder below 15%.
        PhysGrabObjectBreakPostfix(new Component { InHaul = true }, 80, true);
        PhysGrabObjectDestroyedPostfix(new Component { InHaul = true, Dead = true, Current = 10 });
        Check(p.lostValue == 80 && p.mapValue == 920 && p.reports == 1,
            "Threshold break is not counted twice on removal");

        p = Reset();
        var empty = new List<ResolvedEnemyCandidate>();
        Check(!p.SyncMonsterRoster(empty) && p.posts.Count == 0, "Empty roster waits for stability");
        Check(p.SyncMonsterRoster(empty) && p.posts.Count == 1 && p.posts[0] == "{\"monsters\":[]}",
            "First stable empty roster is published");
        p.SyncMonsterRoster(empty);
        Check(p.posts.Count == 1, "Unchanged empty roster is not duplicated");
        var enemies = new List<ResolvedEnemyCandidate> {
            new ResolvedEnemyCandidate { Candidate = new EnemyCandidate { Root = new Component { Id = 7 } }, MonsterName = "Spewer" }
        };
        p.SyncMonsterRoster(enemies);
        Check(p.posts.Count == 2 && p.posts[1].Contains("Spewer"), "Late enemy replaces empty roster");
        p.SyncMonsterRoster(empty);
        Check(p.posts.Count == 3 && p.posts[2] == "{\"monsters\":[]}", "Nonempty roster can become empty");

        p = Reset();
        Check(!p.SyncMonsterRoster(enemies) && !p.SyncMonsterRoster(enemies)
            && p.SyncMonsterRoster(enemies) && p.posts.Count == 1,
            "Nonempty roster keeps existing stabilization");
    }
    // METHODS
}
'@
$testDirectory = Join-Path $PSScriptRoot 'obj/PluginRegressionTests'
New-Item -ItemType Directory -Force $testDirectory | Out-Null
$project = Join-Path $testDirectory 'PluginRegressionTests.csproj'
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <OutputType>Exe</OutputType>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
'@ | Set-Content $project -Encoding UTF8
$code = $harness.Replace('// METHODS', ($methods -join "`n"))
$code += "`npublic static class Program { public static void Main() { PluginRegressionTests.Run(); } }"
$code | Set-Content (Join-Path $testDirectory 'Program.cs') -Encoding UTF8
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Regression harness build failed' }
& (Join-Path $testDirectory 'bin/Release/net472/PluginRegressionTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Plugin regression tests failed' }
