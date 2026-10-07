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

def snapshot(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}

with tempfile.TemporaryDirectory(prefix='balance-importer-') as temp:
    base = Path(temp)
    source, output = base / 'input', base / 'data'
    source.mkdir()
    def write(u, s, k):
        (source / 'units.json').write_text(json.dumps(u))
        (source / 'stages.json').write_text(json.dumps(s))
        (source / 'skills.json').write_text(json.dumps(k))
    def run(*extra):
        return subprocess.run(['dotnet', str(cli), '--input', str(source), '--output', str(output),
                               '--project-root', str(project), *extra], capture_output=True, text=True)
    write(units, stages, skills)
    assert run().returncode == 0
    baseline = snapshot(output)
    assert set(baseline) == {
        'units/cat_archer.tres', 'units/cat_healer.tres', 'units/cat_warrior.tres',
        'units/mutant_mouse.tres', 'stages/stage_01.tres', 'stages/stage_02.tres',
        'stages/stage_03.tres', 'stages/catalog.tres',
        'skills/basic_arrow.tres', 'skills/multiple_projectiles.tres',
        'skills/piercing_shot.tres', 'skills/fire_infusion.tres',
        'skills/healing_amplification.tres', 'skills/catalog.tres'
    }
    catalog = (output / 'stages/catalog.tres').read_text()
    assert all(f'stage_0{i}.tres' in catalog for i in range(1, 4))
    skill_catalog = (output / 'skills/catalog.tres').read_text()
    assert all(f'{skill["id"]}.tres' in skill_catalog for skill in skills['skills'])
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
        ('missing field', 'units', lambda d: d['units'][0].pop('projectileSpeed'), '.projectileSpeed'),
        ('frame', 'units', lambda d: d['units'][0].update(actionFrame=8), '.actionFrame'),
        ('target support', 'units', lambda d: d['units'][2].update(targetLimit=0), '.targetLimit'),
        ('placement', 'units', lambda d: d['units'][0].update(placement='ground_or_path'), '.placement'),
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
        ('duplicate stage', 'stages', lambda d: d['stages'].append(copy.deepcopy(d['stages'][0])), '.id'),
        ('skill schema', 'skills', lambda d: d.update(schemaVersion=2), 'schemaVersion'),
        ('skill duplicate', 'skills', lambda d: d['skills'].append(copy.deepcopy(d['skills'][0])), '.id'),
        ('skill role', 'skills', lambda d: d['skills'][0].update(role='spell'), '.role'),
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
    ]
    for name, kind, mutate, field in cases:
        u, s, k = copy.deepcopy(units), copy.deepcopy(stages), copy.deepcopy(skills)
        mutate({'units': u, 'stages': s, 'skills': k}[kind])
        write(u, s, k)
        result = run()
        assert result.returncode == 1 and field in result.stderr, (name, result.stdout, result.stderr)
        assert snapshot(output) == baseline, name + ': partial output'
    write(units, stages, skills)
    (source / 'units.json').write_text('{bad json')
    result = run()
    assert result.returncode == 1 and 'units.json' in result.stderr
    assert snapshot(output) == baseline
    write(units, stages, skills)
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
    write(u, stages, skills)
    assert run().returncode == 0 and 'ActionPower = 9' in generated.read_text()
    # Missing wave overrides are legal and omitted rather than replaced by magic values.
    s = copy.deepcopy(stages)
    s['stages'][0]['waves'][0].pop('healthOverride')
    s['stages'][0]['waves'][0].pop('speedOverride')
    write(units, s, skills)
    assert run().returncode == 0
    existing = snapshot(output)
    write(units, stages, skills)
    (source / 'skills.json').write_text('{bad json')
    result = run()
    assert result.returncode == 1 and 'skills.json' in result.stderr
    assert snapshot(output) == existing
print(f'IMPORTER QA PASS: {len(cases)} invalid cases, malformed JSON, no partial writes, deterministic output, check drift/stale, overrides, changed power')
