const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Source-contract checks deliberately avoid building the Unity plugin.
const source = fs.readFileSync(path.join(__dirname, '../../overlay-hud/Plugin.Native.cs'), 'utf8');
const sections = Object.fromEntries([...source.matchAll(/private const string (Native\w+Section) = "([^"]+)";/g)]
  .map((match) => [match[1], match[2]]));
const bindings = [...source.matchAll(/BindNative(?:Setting|Toggle)\((Native\w+Section), "([^"]+)", ([^,]+),\s*"([^"]*)", (\d+)/g)]
  .map((match) => ({ section: sections[match[1]], key: match[2], value: match[3], description: match[4], order: Number(match[5]) }));
const expectedGroups = {
  '03 Native - General': ['Enabled', 'Language', 'SaveTimestamps'],
  '04 Native - Layout': ['Alignment', 'OffsetX', 'OffsetY', 'Scale', 'HorizontalLayout', 'HorizontalColumns', 'Columns', 'MonsterSize', 'UpgradeSize', 'UpgradeRows', 'WrapValueLabels'],
  '05 Native - Information': ['ShowLevel', 'ShowTimer', 'ShowMapValue', 'ShowLostValue', 'ShowUpgrades'],
  '06 Native - Monsters': ['ShowMonsters', 'ShowHealth', 'ShowRespawnTimer', 'ShowRespawnIndicator', 'ShowStrength', 'ShowMonsterCounts', 'OnlyAliveMonsters', 'FilterUndetectedMonsters'],
  '07 Native - Effects': ['Animations', 'ShowProximityWaves', 'ShowDamageNumbers', 'ShowDestroyedItemAlerts', 'UseGameUIEffects', 'ShowTooltips'],
  '08 Native - Upgrades': [],
};

function compiledSource(nativeOnly) {
  const stack = [];
  let enabled = true;
  return source.split(/\r?\n/).filter((line) => {
    const directive = line.trim();
    if (directive.startsWith('#if ')) {
      assert.ok(['#if NATIVE_ONLY', '#if !NATIVE_ONLY'].includes(directive), `Unhandled directive: ${directive}`);
      const condition = directive === '#if NATIVE_ONLY' ? nativeOnly : !nativeOnly;
      stack.push({ parent: enabled, condition });
      enabled = enabled && condition;
    } else if (directive === '#else') {
      const frame = stack[stack.length - 1];
      enabled = frame.parent && !frame.condition;
    } else if (directive === '#endif') {
      enabled = stack.pop().parent;
    } else {
      return enabled;
    }
    return false;
  }).join('\n');
}

function initializationFailure(text) {
  return text.slice(text.indexOf('catch (Exception error)'), text.indexOf('// ConfigurationManager'));
}

test('native settings use exactly the requested sections and intuitive display order', () => {
  assert.deepEqual(Object.values(sections), Object.keys(expectedGroups));
  assert.equal(bindings.length, Object.values(expectedGroups).flat().length);
  assert.equal(new Set(bindings.map((binding) => binding.key)).size, bindings.length);
  for (const [section, keys] of Object.entries(expectedGroups)) {
    const group = bindings.filter((binding) => binding.section === section);
    assert.deepEqual(group.map((binding) => binding.key), keys, section);
    assert.deepEqual([...group].sort((a, b) => b.order - a.order).map((binding) => binding.key), keys, section);
    assert.equal(new Set(group.map((binding) => binding.order)).size, group.length, section);
  }
  assert.doesNotMatch(source, /"NativeHUD(?:\.Upgrades)?"|"Overlay"|Electron-style|\(old\)|compatibility/i);
  assert.match(source, /public int Order;/);
  assert.match(source, /new ConfigDescription\(description, acceptableValues,\s*new NativeConfigurationManagerAttributes \{ Order = order \}\)/);
});

test('native defaults and acceptable value constraints are preserved', () => {
  const specialDefaults = {
    Language: '"ru"', Alignment: '"Center"', OffsetX: '0f', OffsetY: '20f', Scale: '1f',
    HorizontalColumns: '11', Columns: '4', MonsterSize: '70f', UpgradeSize: '38f', UpgradeRows: '2',
    OnlyAliveMonsters: 'false', FilterUndetectedMonsters: 'false', ShowDestroyedItemAlerts: 'false', ShowTooltips: 'false',
  };
  for (const binding of bindings) {
    assert.equal(binding.value, specialDefaults[binding.key] ?? 'true', binding.key);
  }
  const constraints = [
    'new AcceptableValueList<string>("ru", "en")',
    'new AcceptableValueList<string>("Left", "Center", "Right")',
    'new AcceptableValueRange<float>(-1920f, 1920f)',
    'new AcceptableValueRange<float>(0f, 1080f)',
    'new AcceptableValueRange<float>(0.5f, 2f)',
    'new AcceptableValueRange<float>(32f, 128f)',
    'new AcceptableValueRange<float>(20f, 64f)',
    'new AcceptableValueRange<int>(1, 2)',
  ];
  for (const constraint of constraints) assert.ok(source.includes(constraint), constraint);
  assert.equal(source.split('new AcceptableValueRange<int>(1, 24)').length - 1, 2);
});

test('all twelve per-upgrade toggles use the upgrades section with descending order', () => {
  assert.deepEqual([...source.matchAll(/BindNativeUpgrade\("([^"]+)",/g)].map((match) => match[1]), [
    'strength', 'tumbleLaunch', 'range', 'sprintSpeed', 'tumbleWings', 'crouchRest',
    'extraJump', 'tumbleClimb', 'health', 'stamina', 'mapPlayerCount', 'deathHeadBattery',
  ]);
  assert.match(source, /nativeUpgradeVisibility\[key\] = BindNativeToggle\(NativeUpgradesSection, key, true,[\s\S]*?100 - nativeUpgradeVisibility.Count\)/);
});

test('live updates cover every native section and exclude restart-only renderer selection', () => {
  const handler = source.slice(source.indexOf('private void OnNativeSettingChanged('), source.indexOf('private NativeHudOptions GetNativeOptions('));
  assert.deepEqual([...handler.matchAll(/section == (Native\w+Section)/g)].map((match) => sections[match[1]]), Object.keys(expectedGroups));
  assert.match(handler, /nativeOptionsDirty = true;/);
  assert.doesNotMatch(handler, /01 General|Renderer/);
  assert.match(source, /Config.SettingChanged -= OnNativeSettingChanged;\s*Config.SettingChanged \+= OnNativeSettingChanged;/);
});

test('combined build defaults to Electron and retains initialization fallback', () => {
  const combined = compiledSource(false);
  assert.match(combined, /public enum RendererMode\s*\{\s*Electron,\s*Native\s*\}/);
  assert.match(combined, /BindNativeSetting\("01 General", "Renderer", RendererMode.Electron,/);
  assert.match(combined, /Restart the game after changing/);
  assert.match(initializationFailure(combined), /nativeMode = false;/);
  assert.match(initializationFailure(combined), /Logger.LogError\("Native HUD initialization failed; using Electron for this session:/);
});

test('native-only build has no renderer selector, desktop wording or fallback', () => {
  const nativeOnly = compiledSource(true);
  assert.match(nativeOnly, /private bool nativeMode;/);
  assert.match(nativeOnly, /nativeMode = true;/);
  assert.doesNotMatch(nativeOnly, /RendererMode|"Renderer"|"01 General"|Electron|desktop|HTTP bridge/i);
  const failure = initializationFailure(nativeOnly);
  assert.match(failure, /nativeHud\?\.Dispose\(\);\s*nativeHud = null;/);
  assert.match(failure, /Logger.LogError\("Native HUD initialization failed: " \+ error\)/);
  assert.doesNotMatch(failure, /nativeMode = false/);
});
