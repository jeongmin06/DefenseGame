"""Focused contract tests for unit retreat and redeployment fields."""

import copy
import json
from pathlib import Path
import subprocess
import tempfile


client = Path(__file__).resolve().parents[3]
project = client / "tools/DefenseGame.DataImporter/DefenseGame.DataImporter.csproj"
source_data = client / "balance-json"


def write_inputs(target: Path, units: dict) -> None:
    target.mkdir(exist_ok=True)
    (target / "units.json").write_text(json.dumps(units), encoding="utf-8")
    for name in ("stages.json", "skills.json", "player_defaults.json"):
        (target / name).write_bytes((source_data / name).read_bytes())


def run_importer(source: Path, output: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            "dotnet",
            "run",
            "--project",
            str(project),
            "--no-build",
            "--",
            "--input",
            str(source),
            "--output",
            str(output),
            "--project-root",
            str(client),
        ],
        capture_output=True,
        text=True,
        check=False,
    )


units = json.loads((source_data / "units.json").read_text(encoding="utf-8"))
with tempfile.TemporaryDirectory(prefix="redeploy-importer-") as temp:
    root = Path(temp)
    source = root / "input"
    output = root / "output"

    write_inputs(source, units)
    result = run_importer(source, output)
    assert result.returncode == 0, result.stderr
    for unit in units["units"]:
        resource = (output / f"units/{unit['id']}.tres").read_text(encoding="utf-8")
        if unit["role"] == "enemy":
            assert "RedeployDelay = 0\n" in resource
            assert "RetreatRefundRate = 0\n" in resource
        else:
            assert "RedeployDelay = 5\n" in resource
            assert "RetreatRefundRate = 0.5\n" in resource

    cases = [
        (lambda data: data["units"][0].pop("redeployDelay"), ".redeployDelay"),
        (lambda data: data["units"][0].pop("retreatRefundRate"), ".retreatRefundRate"),
        (lambda data: data["units"][0].update(redeployDelay=0), ".redeployDelay"),
        (lambda data: data["units"][0].update(retreatRefundRate=-0.1), ".retreatRefundRate"),
        (lambda data: data["units"][0].update(retreatRefundRate=1.1), ".retreatRefundRate"),
        (lambda data: data["units"][3].update(redeployDelay=5), ".redeployDelay"),
        (lambda data: data["units"][3].update(retreatRefundRate=0.5), ".retreatRefundRate"),
    ]
    for mutate, expected_error in cases:
        invalid = copy.deepcopy(units)
        mutate(invalid)
        write_inputs(source, invalid)
        result = run_importer(source, output)
        assert result.returncode == 1, result.stdout
        assert expected_error in result.stderr, result.stderr

print(f"REDEPLOY IMPORTER QA PASS: {len(cases)} invalid cases")
