"""Package-free importer contract tests. Run after dotnet build of the importer."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

project = Path(__file__).resolve().parents[2]
cli = project / 'tools/DefenseGame.DataImporter/bin/Debug/net9.0/DefenseGame.DataImporter.dll'
units = json.loads((project / 'balance-json/units.json').read_text())
stages = json.loads((project / 'balance-json/stages.json').read_text())
skills = json.loads((project / 'balance-json/skills.json').read_text())
player = json.loads((project / 'balance-json/player_defaults.json').read_text())

def snapshot(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}

with tempfile.TemporaryDirectory(prefix='balance-importer-') as temp:
    base = Path(temp)
    source, output = base / 'input', base / 'data'
    source.mkdir()
    def write(u, s, k, p):
        (source / 'units.json').write_text(json.dumps(u))
        (source / 'stages.json').write_text(json.dumps(s))
        (source / 'skills.json').write_text(json.dumps(k))
        (source / 'player_defaults.json').write_text(json.dumps(p))
    def run(*extra):
        return subprocess.run(['dotnet', str(cli), '--input', str(source), '--output', str(output),
                               '--project-root', str(project), *extra], capture_output=True, text=True)
    write(units, stages, skills, player)
    assert run().returncode == 0
    baseline = snapshot(output)
    assert set(baseline) == {
        'units/cat_archer.tres', 'units/cat_healer.tres', 'units/cat_warrior.tres',
        'units/mutant_mouse.tres', 'stages/stage_01.tres', 'stages/stage_02.tres',
        'stages/stage_03.tres', 'stages/catalog.tres',
        'skills/basic_arrow.tres', 'skills/basic_slash.tres', 'skills/basic_heal.tres',
        'skills/multiple_projectiles.tres',
        'skills/piercing_shot.tres', 'skills/fire_infusion.tres',
        'skills/healing_amplification.tres', 'skills/catalog.tres',
        'player/defaults.tres'
    }
    catalog = (output / 'stages/catalog.tres').read_text()
    assert all(f'stage_0{i}.tres' in catalog for i in range(1, 4))
    skill_catalog = (output / 'skills/catalog.tres').read_text()
    assert all(f'{skill["id"]}.tres' in skill_catalog for skill in skills['skills'])
    defaults = (output / 'player/defaults.tres').read_text()
    assert 'CharacterId = "starter_archer_a"' in defaults
    assert 'CharacterId = "starter_archer_b"' in defaults
    assert 'CharacterId = "starter_warrior_a"' in defaults
    assert 'CharacterId = "starter_healer_a"' in defaults
    assert 'UnlockedPoints = 5' in defaults and 'OwnedSkillIds = Array[String]([' in defaults
    assert 'Dictionary[String, String]({"en":' in defaults and '"ko":' in defaults
    for index, cap in enumerate((3, 4, 5), start=1):
        stage = (output / f'stages/stage_0{index}.tres').read_text()
        assert f'StageCap = {cap}' in stage and 'HasStageCap = true' in stage and 'MaxSquadUnits = 10' in stage
    assert run().returncode == 0 and snapshot(output) == baseline
    assert run('--check').returncode == 0 and snapshot(output) == baseline
    cases = [
        ('schema', 'units', lambda d: d.update(schemaVersion=2), 'schemaVersion'),
        ('duplicate', 'units', lambda d: d['units'].append(copy.deepcopy(d['units'][0])), '.id'),
        ('bad id', 'units', lambda d: d['units'][0].update(id='../bad'), '.id'),
        ('scene missing', 'units', lambda d: d['units'][0].update(scenePath='res://scenes/missing.tscn'), '.scenePath'),
        ('path traversal', 'units', lambda d: d['units'][0].update(scenePath='res://../README.tscn'), '.scenePath'),
        ('zero HP', 'units', lambda d: d['units'][0].update(maxHealth=0), '.maxHealth'),
        ('underflow', 'units', lambda d: d['units'][0].update(maxHealth=1e-100), '.maxHealth'),
        ('negative power', 'units', lambda d: d['units'][0].update(actionPower=-1), '.actionPower'),
        ('role field', 'units', lambda d: d['units'][2].update(projectileSpeed=10), '.projectileSpeed'),
        ('unit description locale', 'units', lambda d: d['units'][0]['description'].pop('en'), '.description.en'),
        ('missing field', 'units', lambda d: d['units'][0].pop('projectileSpeed'), '.projectileSpeed'),
        ('frame', 'units', lambda d: d['units'][0].update(actionFrame=8), '.actionFrame'),
        ('target support', 'units', lambda d: d['units'][2].update(targetLimit=0), '.targetLimit'),
        ('placement', 'units', lambda d: d['units'][0].update(placement='ground_or_path'), '.placement'),
        ('allied deployment cost', 'units', lambda d: d['units'][0].update(deploymentCost=0), '.deploymentCost'),
        ('enemy deployment cost', 'units', lambda d: d['units'][3].update(deploymentCost=1), '.deploymentCost'),
        ('grid', 'stages', lambda d: d['stages'][0]['grid'].update(columns=0), '.grid.columns'),
        ('fractional cell', 'stages', lambda d: d['stages'][0]['pathCorners'][0].__setitem__(0, 1.5), '.pathCorners'),
        ('diagonal', 'stages', lambda d: d['stages'][0]['pathCorners'][1].__setitem__(1, 4), '.pathCorners'),
        ('zero segment', 'stages', lambda d: d['stages'][0]['pathCorners'].__setitem__(1, [-2,3]), '.pathCorners'),
        ('blocked overlap', 'stages', lambda d: d['stages'][0].update(blockedCells=[[1,3]]), '.blockedCells'),
        ('unknown reference', 'stages', lambda d: d['stages'][0]['roster'][0].update(unitId='missing'), '.unitId'),
        ('role duplicate', 'stages', lambda d: d['stages'][0]['roster'][1].update(unitId='cat_archer'), '.unitId'),
        ('roster count', 'stages', lambda d: d['stages'][0]['roster'][0].update(count=0), '.count'),
        ('wave count', 'stages', lambda d: d['stages'][0]['waves'][0].update(count=0), '.count'),
        ('enemy reference', 'stages', lambda d: d['stages'][0]['waves'][0].update(enemyId='cat_healer'), '.enemyId'),
        ('override', 'stages', lambda d: d['stages'][0]['waves'][0].update(speedOverride=-1), '.speedOverride'),
        ('unknown field', 'stages', lambda d: d['stages'][0].update(typo=1), '.typo'),
        ('stage description locale', 'stages', lambda d: d['stages'][0]['description'].update(KO='invalid'), '.description.KO'),
        ('duplicate stage', 'stages', lambda d: d['stages'].append(copy.deepcopy(d['stages'][0])), '.id'),
        ('negative stage cap', 'stages', lambda d: d['stages'][0].update(skillPointCap=-1), '.skillPointCap'),
        ('zero squad max', 'stages', lambda d: d['stages'][0].update(maxSquadUnits=0), '.maxSquadUnits'),
        ('large squad max', 'stages', lambda d: d['stages'][0].update(maxSquadUnits=11), '.maxSquadUnits'),
        ('deployment initial over max', 'stages', lambda d: d['stages'][0].update(initialDeploymentPoints=31), '.initialDeploymentPoints'),
        ('deployment zero regen', 'stages', lambda d: d['stages'][0].update(deploymentPointRegenPerSecond=0), '.deploymentPointRegenPerSecond'),
        ('deployment cost over stage max', 'stages', lambda d: d['stages'][0].update(maxDeploymentPoints=9, initialDeploymentPoints=9), '.unitId'),
        ('skill schema', 'skills', lambda d: d.update(schemaVersion=2), 'schemaVersion'),
        ('skill duplicate', 'skills', lambda d: d['skills'].append(copy.deepcopy(d['skills'][0])), '.id'),
        ('skill role', 'skills', lambda d: d['skills'][0].update(role='spell'), '.role'),
        ('skill empty description', 'skills', lambda d: d['skills'][0]['description'].update(ko=''), '.description.ko'),
        ('skill unknown tag', 'skills', lambda d: d['skills'][0]['tags'].__setitem__(0, 'UNKNOWN'), '.tags[0]'),
        ('skill duplicate tag', 'skills', lambda d: d['skills'][0]['tags'].append('ATTACK'), '.tags[5]'),
        ('skill missing tags', 'skills', lambda d: d['skills'][0].pop('tags'), '.tags'),
        ('active link cost', 'skills', lambda d: d['skills'][0].update(linkCost=1), '.linkCost'),
        ('support link cost', 'skills', lambda d: d['skills'][1].update(linkCost=0), '.linkCost'),
        ('active projectile count', 'skills', lambda d: d['skills'][0].update(baseProjectileCount=0), '.baseProjectileCount'),
        ('active requirement', 'skills', lambda d: d['skills'][0].update(requiredAnyTags=['HIT']), 'compatibility requirements'),
        ('unknown effect', 'skills', lambda d: d['skills'][1]['effects'][0].update(type='explode'), '.type'),
        ('missing effect value', 'skills', lambda d: d['skills'][1]['effects'][0].pop('intValue'), '.intValue'),
        ('wrong effect value', 'skills', lambda d: d['skills'][1]['effects'][0].update(floatValue=2), '.floatValue'),
        ('fractional effect', 'skills', lambda d: d['skills'][1]['effects'][0].update(intValue=1.5), '.intValue'),
        ('zero multiplier', 'skills', lambda d: d['skills'][1]['effects'][1].update(floatValue=0), '.floatValue'),
        ('unknown effect tag', 'skills', lambda d: d['skills'][3]['effects'][1].update(tagValue='ICE'), '.tagValue'),
        ('conflicting tag', 'skills', lambda d: d['skills'][3].update(forbiddenTags=['HIT']), '.forbiddenTags'),
        ('player schema', 'player', lambda d: d.update(schemaVersion=2), 'schemaVersion'),
        ('player level', 'player', lambda d: d['playerSkillProgress'].update(playerLevel=0), '.playerLevel'),
        ('player points', 'player', lambda d: d['playerSkillProgress'].update(unlockedPoints=-1), '.unlockedPoints'),
        ('owned duplicate', 'player', lambda d: d['playerSkillProgress']['ownedSkillIds'].append('basic_arrow'), 'duplicate ID'),
        ('owned missing', 'player', lambda d: d['playerSkillProgress']['ownedSkillIds'].__setitem__(0, 'missing_skill'), 'unknown skill reference'),
        ('profile duplicate', 'player', lambda d: d['catProfiles'].append(copy.deepcopy(d['catProfiles'][0])), 'duplicate character ID'),
        ('profile missing description', 'player', lambda d: d['catProfiles'][0].pop('description'), '.description'),
        ('profile unit', 'player', lambda d: d['catProfiles'][0].update(unitId='missing_unit'), 'allied unit reference'),
        ('preset duplicate', 'player', lambda d: d['catSkillPresets'].append(copy.deepcopy(d['catSkillPresets'][0])), 'duplicate preset'),
        ('preset character', 'player', lambda d: d['catSkillPresets'][0].update(characterId='missing_cat'), 'unknown character reference'),
        ('preset allocated', 'player', lambda d: d['catSkillPresets'][0].update(allocatedPoints=1), '.allocatedPoints'),
        ('preset duplicate support', 'player', lambda d: d['catSkillPresets'][0]['supportSkillIds'].append('multiple_projectiles'), 'duplicate ID'),
        ('preset active missing', 'player', lambda d: d['catSkillPresets'][0].update(activeSkillId='missing_skill'), '.activeSkillId'),
        ('preset active role', 'player', lambda d: d['catSkillPresets'][0].update(activeSkillId='fire_infusion'), 'expected free active skill'),
        ('preset support role', 'player', lambda d: d['catSkillPresets'][0]['supportSkillIds'].__setitem__(0, 'basic_arrow'), 'one-point support'),
        ('preset incompatible', 'player', lambda d: d['catSkillPresets'][0]['supportSkillIds'].__setitem__(0, 'healing_amplification'), 'required-any'),
        ('preset timestamp', 'player', lambda d: d['catSkillPresets'][0].update(updatedAtUtc='not-a-date'), '.updatedAtUtc'),
        ('preset absent', 'player', lambda d: d['catSkillPresets'].pop(), 'exactly one preset'),
        ('preset budget', 'player', lambda d: d['playerSkillProgress'].update(unlockedPoints=2), 'exceed unlocked points'),
    ]
    for name, kind, mutate, field in cases:
        u, s, k, p = copy.deepcopy(units), copy.deepcopy(stages), copy.deepcopy(skills), copy.deepcopy(player)
        mutate({'units': u, 'stages': s, 'skills': k, 'player': p}[kind])
        write(u, s, k, p)
        result = run()
        assert result.returncode == 1 and field in result.stderr, (name, result.stdout, result.stderr)
        assert snapshot(output) == baseline, name + ': partial output'
    write(units, stages, skills, player)
    (source / 'units.json').write_text('{bad json')
    result = run()
    assert result.returncode == 1 and 'units.json' in result.stderr
    assert snapshot(output) == baseline
    write(units, stages, skills, player)
    generated = output / 'units/cat_archer.tres'
    generated.write_text(generated.read_text() + '\n')
    changed = snapshot(output)
    assert run('--check').returncode == 1 and snapshot(output) == changed
    assert run().returncode == 0 and snapshot(output) == baseline
    generated_skill = output / 'skills/basic_arrow.tres'
    generated_skill.write_text(generated_skill.read_text() + '\n')
    skill_changed = snapshot(output)
    assert run('--check').returncode == 1 and snapshot(output) == skill_changed
    assert run().returncode == 0 and snapshot(output) == baseline
    (output / 'units/stale.tres').write_text('stale')
    assert run('--check').returncode == 1
    assert run().returncode == 0 and snapshot(output) == baseline
    u = copy.deepcopy(units)
    u['units'][0]['actionPower'] = 9
    write(u, stages, skills, player)
    assert run().returncode == 0 and 'ActionPower = 9' in generated.read_text()
    # Missing wave overrides are legal and omitted rather than replaced by magic values.
    s = copy.deepcopy(stages)
    s['stages'][0]['waves'][0].pop('healthOverride')
    s['stages'][0]['waves'][0].pop('speedOverride')
    write(units, s, skills, player)
    assert run().returncode == 0
    existing = snapshot(output)
    # A missing cap and an explicit zero cap must remain distinguishable in generated resources.
    s = copy.deepcopy(stages)
    s['stages'][0].pop('skillPointCap')
    s['stages'][0].pop('maxSquadUnits')
    s['stages'][1]['skillPointCap'] = 0
    write(units, s, skills, player)
    assert run().returncode == 0
    assert 'HasStageCap = false' in (output / 'stages/stage_01.tres').read_text()
    assert 'MaxSquadUnits = 10' in (output / 'stages/stage_01.tres').read_text()
    zero_cap_stage = (output / 'stages/stage_02.tres').read_text()
    assert 'HasStageCap = true' in zero_cap_stage and 'StageCap = 0' in zero_cap_stage
    existing = snapshot(output)
    write(units, stages, skills, player)
    (source / 'skills.json').write_text('{bad json')
    result = run()
    assert result.returncode == 1 and 'skills.json' in result.stderr
    assert snapshot(output) == existing
    write(units, stages, skills, player)
    (source / 'player_defaults.json').write_text('{bad json')
    result = run()
    assert result.returncode == 1 and 'player_defaults.json' in result.stderr
    assert snapshot(output) == existing
print(f'IMPORTER QA PASS: {len(cases)} invalid cases, malformed JSON, no partial writes, deterministic output, check drift/stale, overrides, changed power')
