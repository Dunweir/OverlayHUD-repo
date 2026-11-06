const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { execFileSync } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const read = (name) => fs.readFileSync(path.join(root, name), 'utf8');
const files = ['Plugin.cs', 'Plugin.Native.cs', 'MonsterLocalization.cs'];

// Deliberately limited to the directives used here; unknown/unbalanced guards
// fail closed instead of silently making a source-contract test pass.
function preprocess(source, nativeOnly) {
  const stack = [];
  let enabled = true;
  const output = [];
  for (const line of source.split(/\r?\n/)) {
    const directive = line.trim();
    if (!directive.startsWith('#')) {
      if (enabled) output.push(line);
      continue;
    }
    if (/^#if !?NATIVE_ONLY$/.test(directive)) {
      const condition = directive === '#if NATIVE_ONLY' ? nativeOnly : !nativeOnly;
      stack.push({ parent: enabled, condition, hadElse: false });
      enabled = enabled && condition;
    } else if (directive === '#else') {
      const frame = stack[stack.length - 1];
      assert.ok(frame && !frame.hadElse, 'Unexpected or duplicate #else');
      frame.hadElse = true;
      enabled = frame.parent && !frame.condition;
    } else if (directive === '#endif') {
      assert.ok(stack.length, 'Unexpected #endif');
      enabled = stack.pop().parent;
    } else {
      assert.fail(`Unhandled preprocessor directive: ${directive}`);
    }
  }
  assert.equal(stack.length, 0, 'Unclosed #if');
  return output.join('\n');
}

const variants = Object.fromEntries([['Full', false], ['Native', true]].map(([name, nativeOnly]) => [
  name, Object.fromEntries(files.map((file) => [file, preprocess(read(`overlay-hud/${file}`), nativeOnly)])),
]));

// These methods have their closing brace at class-member indentation. Avoid
// counting braces inside JSON strings, which are common in the bridge methods.
function method(source, name) {
  const match = source.match(new RegExp(`^        private [^\\r\\n]*\\b${name}\\([^\\r\\n]*\\)\\s*\\{[\\s\\S]*?^        \\}`, 'm'));
  assert.ok(match, `Missing method ${name}`);
  return match[0];
}

const desktopSymbols = [
  'StartOverlayAppIfNeeded', 'StopOverlayAppIfNeeded', 'ResolveOverlayAppPath',
  'EnsureOverlayAppExtracted', 'BundledAppStorage', 'launchedOverlayProcess',
  'autoStartOverlayApp', 'autoCloseOverlayApp', 'overlayAppRelativePath', 'overlayAppArchiveName',
  'RendererMode', 'QueueNetworkRequest', 'networkQueueLock', 'networkQueueTail',
  'latestMonsterStatusRequestVersion', 'latestMapValueRequestVersion',
  'BuildSiblingEndpoint', 'SendStateLevelFallback', 'SendHttpPost', 'SendHttpGet',
  'ReadHttpResponse', 'IsHttpSuccess', 'TrimHttpResult', 'TcpClient', 'NetworkStream',
];

const desktopStrings = [
  '09 Electron - App', '10 Electron - Connection', 'OverlayHUD_app.zip',
  '/api/monster-localization', 'http://127.0.0.1:8787',
];

test('preprocessor handles nested inactive branches and rejects unsupported guards', () => {
  const fixture = '#if NATIVE_ONLY\nnative\n#if !NATIVE_ONLY\nnever\n#else\nnested\n#endif\n#else\nfull\n#endif';
  assert.equal(preprocess(fixture, true), 'native\nnested');
  assert.equal(preprocess(fixture, false), 'full');
  for (const invalid of ['#if DEBUG\nx\n#endif', '#if NATIVE_ONLY', '#endif', '#else', '#if NATIVE_ONLY\n#else\n#else\n#endif']) {
    assert.throws(() => preprocess(invalid, true));
  }
});

test('Native compiles out desktop startup, shutdown, configuration and HTTP client', () => {
  const native = Object.values(variants.Native).join('\n');
  const full = Object.values(variants.Full).join('\n');
  for (const symbol of desktopSymbols) {
    const pattern = new RegExp(`\\b${symbol}\\b`);
    assert.doesNotMatch(native, pattern, `Native leaked ${symbol}`);
    assert.match(full, pattern, `Full lost ${symbol}`);
  }
  for (const text of desktopStrings) {
    assert.ok(!native.includes(text), `Native leaked ${text}`);
    assert.ok(full.includes(text), `Full lost ${text}`);
  }
  assert.doesNotMatch(native, /\b(?:endpoint|levelEndpoint|HttpClient|HttpWebRequest|WebClient)\b|System\.Net|System\.IO\.Compression|Process\.(?:Start|GetCurrentProcess)|ProcessStartInfo/);
  const awake = method(variants.Native['Plugin.cs'], 'Awake');
  assert.match(awake, /InitializeNativeHud\(\);/);
  assert.match(awake, /StartCoroutine\(SyncMonsterLocalization\(\)\);/);
  const destroy = method(variants.Native['Plugin.cs'], 'OnDestroy');
  assert.match(destroy, /DisposeNativeConfig\(\);/);
  assert.match(destroy, /nativeHud\?\.Dispose\(\);/);
});

test('Full defaults to Electron and retains desktop lifecycle; Native cannot fall back', () => {
  const full = method(variants.Full['Plugin.Native.cs'], 'InitializeNativeHud');
  assert.match(full, /BindNativeSetting\("01 General", "Renderer", RendererMode\.Electron,/);
  assert.match(full, /nativeMode = false;/);
  const awake = method(variants.Full['Plugin.cs'], 'Awake');
  assert.match(awake, /InitializeNativeHud\(\);\s*if \(!nativeMode\) StartOverlayAppIfNeeded\(\);/);
  assert.match(method(variants.Full['Plugin.cs'], 'OnDestroy'), /StopOverlayAppIfNeeded\(\);/);
  const native = method(variants.Native['Plugin.Native.cs'], 'InitializeNativeHud');
  assert.match(native, /nativeMode = true;/);
  assert.doesNotMatch(native, /nativeMode = false|"Renderer"|Electron/);
  assert.match(native, /Logger\.LogError\("Native HUD initialization failed:/);
});

test('both variants retain local localization; only Full retains the guarded HTTP retry loop', () => {
  for (const variant of Object.values(variants)) {
    const source = variant['MonsterLocalization.cs'];
    assert.match(source, /LocalizedHudNames\.Clear\(\)/);
    assert.match(source, /LocalizedHudNames\.SetTable\(/);
    assert.match(source, /GetTableAsync\("HUD", locale\)/);
    assert.match(source, /attempt < 6/);
  }
  const native = variants.Native['MonsterLocalization.cs'];
  assert.doesNotMatch(native, /while\s*\(true\)|JsonConvert|Task\b|QueueNetworkRequest|SendHttpPost|\/api\//);
  const full = method(variants.Full['MonsterLocalization.cs'], 'SyncMonsterLocalization');
  assert.match(full, /if \(nativeMode\) yield break;/);
  assert.ok(full.indexOf('if (nativeMode) yield break;') < full.indexOf('JsonConvert.SerializeObject'));
  assert.match(full, /while \(true\)[\s\S]*BuildSiblingEndpoint\(levelEndpoint\.Value, "\/api\/monster-localization"\)/);
  assert.match(full, /QueueNetworkRequest\(\(\) => SendHttpPost\(url, json\)\)/);
  assert.match(full, /WaitForSecondsRealtime\(success \? 60f : 5f\)/);
});

const routes = {
  PostSeenMonster: 'monster-seen', PostMonsterRoster: 'roster',
  PostMonsterStatuses: 'monster-status', PostLevel: 'level',
  PostGameUiVisibility: 'tab-hidden', PostPlayerUpgrades: 'upgrades',
  PostMapValue: 'map-value', PostItemDestroyed: 'item-destroyed',
};

test('native dispatch short-circuits every bridge route before any endpoint use', () => {
  for (const [name, variant] of Object.entries(variants)) {
    for (const [handler, route] of Object.entries(routes)) {
      const body = method(variant['Plugin.cs'], handler);
      const dispatch = `if (DispatchNative("${route}", json)) yield break;`;
      assert.ok(body.includes(dispatch), `${name}: ${handler} lost native dispatch`);
      const beforeDispatch = body.slice(0, body.indexOf(dispatch));
      assert.doesNotMatch(beforeDispatch, /\b\w*endpoint\w*\b|SendHttp\w*|QueueNetworkRequest/i, `${name}: ${handler} uses HTTP before dispatch`);
      if (name === 'Full') assert.match(body.slice(body.indexOf(dispatch) + dispatch.length), /SendHttpPost\(/);
    }
    assert.match(method(variant['Plugin.cs'], 'PostVisibility'), /if \(nativeMode\) \{ nativeState.Visible = visible; yield break; \}/);
    const map = method(variant['Plugin.cs'], 'PostMapValue');
    for (const value of ['value', 'initial', 'lost']) assert.ok(map.includes(`${value}.ToString(CultureInfo.InvariantCulture)`));
    assert.match(map, /if \(goal.HasValue\)[^\n]*goal.Value.ToString\(CultureInfo.InvariantCulture\)/);
    assert.ok(map.indexOf('json += "}";') < map.indexOf('DispatchNative("map-value", json)'));
  }
});

test('project defaults to Full and conditionally excludes desktop source and compression references', () => {
  const project = read('overlay-hud/OverlayHUD.csproj');
  assert.match(project, /<NativeOnly Condition="'\$\(NativeOnly\)' == ''">false<\/NativeOnly>/);
  const group = project.match(/<PropertyGroup Condition="'\$\(NativeOnly\)' == 'true'">([\s\S]*?)<\/PropertyGroup>/);
  assert.ok(group, 'Missing native-only property group');
  assert.match(group[1], /<DefineConstants>\$\(DefineConstants\);NATIVE_ONLY<\/DefineConstants>/);
  assert.equal((project.match(/NATIVE_ONLY/g) || []).length, 1, 'Symbol must not be defined unconditionally');
  assert.match(project, /<Compile Remove="BundledAppStorage.cs" Condition="'\$\(NativeOnly\)' == 'true'"\s*\/>/);
  const compression = [...project.matchAll(/<Reference\b[^>]*Include="System\.IO\.Compression(?:\.FileSystem)?"[^>]*>/g)];
  assert.equal(compression.length, 2);
  for (const [reference] of compression) assert.match(reference, /Condition="'\$\(NativeOnly\)' != 'true'"/);
  const excludes = project.match(/<DefaultItemExcludes>([^<]+)<\/DefaultItemExcludes>/);
  assert.ok(excludes, 'Isolated build trees must not enter default Compile globs');
  for (const directory of ['bin', 'obj', 'dist']) assert.ok(excludes[1].includes(`${directory}\\**`));
});

test('packager uses explicit variants and per-run build, intermediate and staging paths', () => {
  const script = read('tools/Build-Packages.ps1');
  assert.match(script, /Name = 'Full'; NativeOnly = 'false'/);
  assert.match(script, /Name = 'NativeOnly'; NativeOnly = 'true'/);
  assert.match(script, /\$runId = \[Guid\]::NewGuid\(\)/);
  assert.ok(script.includes('dist/packages/$runId'));
  for (const fragment of ['build/$($variant.Name)', 'stage/$($variant.Name)', 'obj/$($variant.Name)/$runId/',
    '-p:NativeOnly=$($variant.NativeOnly)', '-p:BaseIntermediateOutputPath=$intermediate',
    '-p:MSBuildProjectExtensionsPath=$intermediate', '-p:OutputPath=$outputProperty']) {
    assert.ok(script.includes(fragment), `Missing isolated build setting: ${fragment}`);
  }
  assert.match(script, /EgorSalad-OverlayHUD-\$version-Full\.zip/);
  assert.match(script, /EgorSalad-OverlayHUD-\$version-Native\.zip/);
  assert.match(script, /if \(\$variant.NativeOnly -eq 'false'\)\s*\{\s*Copy-Item[^\n]*OverlayHUD_app\.zip/);
  assert.match(script, /New-FlatArchive -Stage \$stage -Destination \$stagedArchive -Names \$variant.Entries/);
});

test('packager rebuilds into staging and rejects missing, stale or wrong-version desktop bundles', () => {
  const script = read('tools/Build-Packages.ps1');
  assert.match(script, /\[switch\]\s*\$RebuildDesktop/);
  assert.match(script, /if \(\$RebuildDesktop -and -not \[string\]::IsNullOrWhiteSpace\(\$BundlePath\)\)\s*\{\s*throw/);
  assert.match(script, /if \(\$RebuildDesktop\)\s*\{\s*Push-Location \(Join-Path \$repoRoot 'game-overlay'\)[\s\S]*?& npm\.cmd run package/);
  assert.match(script, /\$BundlePath = Join-Path \$workRoot 'OverlayHUD_app\.zip'/);
  assert.match(script, /New-FlatArchive -Stage \$desktopDirectory -Destination \$BundlePath -Names \$desktopNames/);
  assert.ok(script.includes("$_.FullName.Substring($desktopDirectory.Length + 1).Replace('\\', '/')"));
  assert.match(script, /if \(\$null -eq \$entry\)\s*\{\s*throw "Desktop bundle is missing/);
  assert.match(script, /\[System\.Security\.Cryptography\.SHA256\]::Create\(\)/);
  assert.match(script, /\$sha\.ComputeHash\(\$stream\)/);
  assert.match(script, /Get-FileHash -LiteralPath \$SourcePath -Algorithm SHA256/);
  assert.match(script, /if \(\$bundledHash -cne \$sourceHash\)\s*\{\s*throw "Desktop bundle contains stale/);
  assert.match(script, /\$bundle\.GetEntry\('resources\/app\/package\.json'\)/);
  assert.match(script, /if \(\$null -eq \$packageEntry\)\s*\{\s*throw/);
  assert.match(script, /if \(\$desktopPackage\.version -cne \$version\)\s*\{\s*throw/);
  assert.match(script, /foreach \(\$name in @\('server\.js', 'overlay\.html', 'overlay-state\.js', 'overlay\.css'\)\)\s*\{\s*Assert-BundleSource \$bundle "resources\/\$name" \(Join-Path \$repoRoot \$name\)/);
  assert.match(script, /foreach \(\$name in @\('main\.js', 'preload\.js', 'game-lifetime\.js', 'overlay-config\.json'\)\)\s*\{\s*Assert-BundleSource \$bundle "resources\/app\/\$name" \(Join-Path \$repoRoot "game-overlay\/\$name"\)/);
  assert.match(script, /\$assetsRoot = Join-Path \$repoRoot 'assets'/);
  assert.match(script, /Get-ChildItem -LiteralPath \$assetsRoot -Recurse -File/);
  assert.match(script, /Assert-BundleSource \$bundle "resources\/assets\/\$relative" \$asset\.FullName/);
});

const desktopSources = [
  'server.js', 'overlay.html', 'overlay-state.js', 'overlay.css',
  'assets/monster-localization.json', 'assets/upgrade-localization.json',
  'game-overlay/main.js', 'game-overlay/preload.js', 'game-overlay/game-lifetime.js',
];
const desktopEntry = (source) => `resources/${source.replace(/^game-overlay\//, 'app/')}`;

// Optional, read-only artifact smoke check (no build, extraction or assembly load):
// OVERLAYHUD_CHECK_PACKAGES=1 node --test tests/package-variants.test.js
// Uses Windows PowerShell (pwsh elsewhere) and versioned ZIPs in the repo root.
// Binary marker checks catch a swapped/stale Full DLL, not arbitrary IL behavior.
function inspectArchive(archivePath, inspectDesktop) {
  const script = String.raw`
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$zip = [IO.Compression.ZipFile]::OpenRead($env:OVERLAYHUD_TEST_ZIP)
try {
    $entry = $zip.GetEntry('OverlayHUD.dll')
    if ($null -eq $entry) { throw 'Missing OverlayHUD.dll' }
    $stream = $entry.Open()
    $memory = New-Object IO.MemoryStream
    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
    $ascii = [Text.Encoding]::UTF8.GetString($bytes)
    $hex = [BitConverter]::ToString($bytes)
    # Direct assignment avoids nesting the JSON array in Windows PowerShell 5.1.
    $markers = ConvertFrom-Json -InputObject $env:OVERLAYHUD_TEST_MARKERS
    $found = @($markers | Where-Object {
        $ascii.Contains($_) -or
        $hex.Contains([BitConverter]::ToString([Text.Encoding]::Unicode.GetBytes($_)))
    })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)) }
    finally { $sha.Dispose() }
    $desktop = $null
    if ($env:OVERLAYHUD_TEST_DESKTOP -eq '1') {
        $entry = $zip.GetEntry('OverlayHUD_app.zip')
        if ($null -eq $entry) { throw 'Missing OverlayHUD_app.zip' }
        $stream = $entry.Open()
        $memory = New-Object IO.MemoryStream
        $bundle = $null
        try {
            $stream.CopyTo($memory)
            $memory.Position = 0
            $bundle = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read)
            $packageEntry = $bundle.GetEntry('resources/app/package.json')
            if ($null -eq $packageEntry) { throw 'Missing desktop package.json' }
            $reader = [IO.StreamReader]::new($packageEntry.Open())
            try { $package = $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $reader.Dispose() }
            $hashes = @{}
            $names = ConvertFrom-Json -InputObject $env:OVERLAYHUD_TEST_DESKTOP_ENTRIES
            foreach ($name in $names) {
                $sourceEntry = $bundle.GetEntry($name)
                if ($null -eq $sourceEntry) { throw "Missing desktop entry: $name" }
                $sourceStream = $sourceEntry.Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $hashes[$name] = [BitConverter]::ToString($sha.ComputeHash($sourceStream)).Replace('-', '').ToLowerInvariant() }
                finally { $sourceStream.Dispose(); $sha.Dispose() }
            }
            $desktop = [pscustomobject]@{ Version = $package.version; Hashes = $hashes }
        } finally {
            if ($null -ne $bundle) { $bundle.Dispose() }
            $stream.Dispose(); $memory.Dispose()
        }
    }
    [pscustomobject]@{
        Entries = @($zip.Entries | ForEach-Object { $_.FullName })
        Empty = @($zip.Entries | Where-Object { $_.Length -eq 0 } | ForEach-Object { $_.FullName })
        Markers = $found; Hash = $hash; Magic = [Text.Encoding]::ASCII.GetString($bytes, 0, 2)
        Desktop = $desktop
    } | ConvertTo-Json -Depth 4 -Compress
} finally { $zip.Dispose() }
`;
  return JSON.parse(execFileSync(process.platform === 'win32' ? 'powershell.exe' : 'pwsh', [
    '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64'),
  ], {
    encoding: 'utf8', timeout: 60000,
    env: { ...process.env, OVERLAYHUD_TEST_ZIP: archivePath,
      OVERLAYHUD_TEST_DESKTOP: inspectDesktop ? '1' : '0',
      OVERLAYHUD_TEST_DESKTOP_ENTRIES: JSON.stringify(desktopSources.map(desktopEntry)),
      OVERLAYHUD_TEST_MARKERS: JSON.stringify([...desktopSymbols, ...desktopStrings, 'NativeHud', 'DispatchNative']) },
  }).replace(/^\uFEFF/, '').trim());
}

test('optional ZIPs contain distinct DLLs, correct markers and a current desktop bundle', {
  skip: process.env.OVERLAYHUD_CHECK_PACKAGES !== '1' && 'Set OVERLAYHUD_CHECK_PACKAGES=1 after packaging',
}, () => {
  const manifest = JSON.parse(read('overlay-hud/package-flat/manifest.json'));
  const common = ['icon.png', 'manifest.json', 'OverlayHUD.dll', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md'];
  const artifacts = {};
  for (const name of ['Full', 'Native']) {
    const archivePath = path.join(root, `EgorSalad-OverlayHUD-${manifest.version_number}-${name}.zip`);
    assert.ok(fs.existsSync(archivePath), `Missing completed package: ${archivePath}`);
    const artifact = artifacts[name] = inspectArchive(archivePath, name === 'Full');
    assert.deepEqual(artifact.Entries.sort(), [...common, ...(name === 'Full' ? ['OverlayHUD_app.zip'] : [])].sort());
    assert.deepEqual(artifact.Empty, [], `${name} has empty entries`);
    assert.equal(artifact.Magic, 'MZ', `${name} DLL is not a PE image`);
    for (const marker of ['NativeHud', 'DispatchNative']) assert.ok(artifact.Markers.includes(marker), `${name} lost ${marker}`);
    for (const marker of [...desktopSymbols, ...desktopStrings]) {
      assert.equal(artifact.Markers.includes(marker), name === 'Full', `${name}: unexpected marker presence for ${marker}`);
    }
  }
  assert.notEqual(artifacts.Full.Hash, artifacts.Native.Hash, 'Both packages contain the same DLL');
  assert.equal(artifacts.Full.Desktop.Version, manifest.version_number, 'Desktop version differs from release');
  for (const source of desktopSources) {
    const expected = createHash('sha256').update(fs.readFileSync(path.join(root, source))).digest('hex');
    assert.equal(artifacts.Full.Desktop.Hashes[desktopEntry(source)], expected, `Stale desktop source: ${source}`);
  }
});
