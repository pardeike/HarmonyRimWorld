#!/usr/bin/env python3
"""Build, package, and install locally. No publishing or uploading actions."""
import argparse
import importlib.util
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import socket
import errno
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / '.build'
INPUTS = ROOT / 'Legacy-provenance.json'


def ensure_offline_sandbox():
    # A local bind sends no packets. Outside the sandbox it succeeds; inside,
    # Seatbelt rejects it. Re-exec through the offline profile when necessary.
    with socket.socket() as probe:
        try:
            probe.bind(("127.0.0.1", 0))
        except OSError as error:
            if error.errno == errno.EPERM and os.environ.get("OFFLINE_BUILD") == "1":
                return
            raise SystemExit("Cannot establish the offline build network restriction.") from error
    os.environ["OFFLINE_BUILD"] = "1"
    os.execv("/usr/bin/sandbox-exec", ["/usr/bin/sandbox-exec", "-f", str(ROOT / "scripts/offline.sb"), sys.executable, "-I", str(Path(sys.argv[0]).resolve()), *sys.argv[1:]])


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, cwd, log, env):
    with log.open('w') as output:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        print('\n'.join(log.read_text(errors='replace').splitlines()[-40:]), file=sys.stderr)
        raise SystemExit(f'Command failed ({result.returncode}); see {log}')


def game_runtime_smoke(game, output, env):
    runner = BUILD / 'game-runtime-smoke'
    runner.mkdir(exist_ok=True)
    for path in game.iterdir():
        if path.is_file() and (path.name.startswith(('System.', 'Microsoft.', 'libcoreclr', 'libclrgc', 'libclrjit', 'libhost', 'libSystem.')) or path.name in ('mscorlib.dll', 'netstandard.dll', 'createdump')):
            shutil.copy2(path, runner / path.name)
    shutil.copy2(game / 'RimWorld', runner / 'RimWorld')
    shutil.copy2(game / 'RimWorld.runtimeconfig.json', runner / 'RimWorld.runtimeconfig.json')
    shutil.copy2(output / 'Smoke.dll', runner / 'RimWorld.dll')
    for name in ('0Harmony.dll', 'HarmonyMod.dll'):
        shutil.copy2(output / name, runner / name)
    deps = json.loads((game / 'RimWorld.deps.json').read_text())
    fixture = json.loads((output / 'Smoke.deps.json').read_text())
    target = deps['targets'][deps['runtimeTarget']['name']]
    for key, value in fixture['targets'][fixture['runtimeTarget']['name']].items():
        if not key.startswith('Smoke/'):
            target[key] = value
    for key, value in fixture['libraries'].items():
        if not key.startswith('Smoke/'):
            deps['libraries'][key] = value
    (runner / 'RimWorld.deps.json').write_text(json.dumps(deps))
    run([str(runner / 'RimWorld')], runner, BUILD / 'game-runtime-smoke.log', env)
    for mode in ('custom', 'disabled'):
        run([str(runner / 'RimWorld'), mode], runner, BUILD / f'game-runtime-log-{mode}.log', env)


def validate_library():
    provenance = json.loads((ROOT / 'Harmony-provenance.json').read_text())
    library = ROOT / provenance['path']
    if library.resolve() != ROOT / '1.7/Assemblies/0Harmony.dll' or library.is_symlink():
        raise SystemExit('Expected the staged NuGet Harmony DLL in the 1.7 folder.')
    if digest(library) != provenance['sha256']:
        raise SystemExit('Staged Harmony hash differs from Harmony-provenance.json. Rebuild to stage the NuGet-selected DLL and provenance together.')
    return provenance


def validate_legacy(record):
    for name, expected in record['preservedLegacyFiles'].items():
        if digest(ROOT / name) != expected:
            raise SystemExit(f'Unexpected legacy binary change: {name}')
    if digest(ROOT / '1.6/Assemblies/HarmonyMod.dll') != record['official16WrapperSha256']:
        raise SystemExit('The 1.6 wrapper differs from the official mod release.')
    if digest(ROOT / '1.6/Assemblies/0Harmony.dll') != record['harmony16Sha256']:
        raise SystemExit('The 1.6 library differs from official Harmony 2.4.3 net472.')


def validate(record):
    if (ROOT / 'Current').exists():
        raise SystemExit('Obsolete Current directory is still present.')
    validate_legacy(record)
    folders = ET.parse(ROOT / 'LoadFolders.xml').getroot()
    for version in ('1.6', '1.7'):
        if [node.text for node in folders.findall(f'v{version}/li')] != ['/', version]:
            raise SystemExit(f'Incorrect folder selector for {version}')
    for name in ('0Harmony.dll', 'HarmonyMod.dll'):
        if not (ROOT / '1.7/Assemblies' / name).is_file():
            raise SystemExit(f'Missing 1.7 assembly: {name}')
    validate_library()
    if digest(ROOT / '1.7/Assemblies/HarmonyMod.dll') != digest(ROOT / 'Source/bin/Release/HarmonyMod.dll'):
        raise SystemExit('Staged mod differs from the source build.')


def payload_files():
    spec = importlib.util.spec_from_file_location('mod_package', ROOT / 'scripts/mod-package.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return [ROOT / name for name in module.payload()]


def package(record):
    validate(record)
    files = payload_files()
    dist = ROOT / 'dist'
    dist.mkdir(exist_ok=True)
    mod_version = ET.parse(ROOT / 'About/About.xml').getroot().findtext('modVersion')
    if not mod_version or not re.fullmatch(r'[0-9A-Za-z.+_-]+', mod_version):
        raise SystemExit('Invalid mod build identifier for the archive name.')
    archive = dist / f'HarmonyMod-{mod_version}.zip'
    spec = importlib.util.spec_from_file_location('mod_package', ROOT / 'scripts/mod-package.py')
    packager = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(packager)
    names = [path.relative_to(ROOT).as_posix() for path in files]
    packager.write_zip(ROOT, names, archive, 'HarmonyMod/')
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise SystemExit('ZIP integrity check failed.')
        for path in files:
            if hashlib.sha256(bundle.read('HarmonyMod/' + path.relative_to(ROOT).as_posix())).hexdigest() != digest(path):
                raise SystemExit(f'ZIP content mismatch: {path}')
    sha = digest(archive)
    archive.with_suffix('.zip.sha256').write_text(f'{sha}  {archive.name}\n')
    report = {
        'modInformationalVersion': mod_version,
        'harmonyLibrary': validate_library(),
        'harmony16OfficialVersion': record['harmony16OfficialVersion'],
        'harmony16Sha256': record['harmony16Sha256'],
        'archive': archive.name,
        'archiveSha256': sha,
        'payloadSha256': {path.relative_to(ROOT).as_posix(): digest(path) for path in files},
        'validation': 'ZIP integrity, payload hashes, legacy hashes, selectors; runtime evidence is in local smoke logs',
        'runtimeSmoke': {
            name: {
                'runtime': next((line for line in (BUILD / log).read_text().splitlines() if line.startswith('Runtime:')), 'unknown'),
                'passedChecks': [line.removeprefix('PASS: ') for line in (BUILD / log).read_text().splitlines() if line.startswith('PASS: ')],
            }
            for name, log in [('standardDotNet10', 'smoke.log'), ('installedGameRuntime', 'game-runtime-smoke.log')]
        },
        'loggingSmoke': {
            runtime: {
                mode: [line.removeprefix('PASS: ') for line in (BUILD / f'{prefix}-log-{mode}.log').read_text().splitlines() if line.startswith('PASS: ')]
                for mode in ('custom', 'disabled')
            }
            for runtime, prefix in [('standardDotNet10', 'smoke'), ('installedGameRuntime', 'game-runtime')]
        },
        'inGameAcceptance': 'Not performed',
    }
    (dist / 'build-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'Created {archive}\nSHA-256 {sha}\nPayload: {len(files)} files; explicit allow list; no source, game assemblies or logs.')
    return archive, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package-only', action='store_true', help='Package and validate existing local build outputs.')
    parser.add_argument('--no-deploy', action='store_true', help='Build and package without copying into the local game.')
    args = parser.parse_args()
    ensure_offline_sandbox()
    record = json.loads(INPUTS.read_text())
    game = Path(os.environ.get('RIMWORLD_ASSEMBLY_DIR', str(Path.home() / 'Library/Application Support/Steam/steamapps/common/RimWorld/RimWorld.app/Contents/MacOS'))).resolve()
    validate_legacy(record)
    if not args.package_only and not args.no_deploy and not os.environ.get('RIMWORLD_MOD_DIR'):
        raise SystemExit('Set RIMWORLD_MOD_DIR to the physical RimWorld Mods directory, or use --no-deploy.')
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0', DOTNET_EnableDiagnostics='0', RIMWORLD_ASSEMBLY_DIR=str(game))
    if args.no_deploy:
        env['RIMWORLD_MOD_DIR'] = ''
    if not args.package_only and not args.no_deploy:
        run(['python3', '-I', str(ROOT / 'scripts/mod-package.py'), '--mods-dir', env['RIMWORLD_MOD_DIR'], '--check'],
            ROOT, BUILD / 'deploy-preflight.log', env)
    if not args.package_only:
        print('Restoring from downloaded local packages and building the .NET 10 / 1.7 mod.', flush=True)
        for project in ('Source/HarmonyRimWorld.csproj', 'Tests/Smoke/Smoke.csproj'):
            run(['dotnet', 'restore', project, '--configfile', str(ROOT / 'nuget.config'),
                 '--force-evaluate', '-p:RestoreLockedMode=false', '-p:NuGetAudit=false', '--verbosity', 'quiet'],
                ROOT, BUILD / ('mod-restore.log' if project.startswith('Source/') else 'smoke-restore.log'), env)
        run(['dotnet', 'build', 'Source/HarmonyRimWorld.csproj', '--no-restore', '-c', 'Release', f'-p:RimWorldAssemblyDir={game}', '-p:UseSharedCompilation=false', '--disable-build-servers', '--verbosity', 'quiet'], ROOT, BUILD / 'mod-build.log', env)
        run(['dotnet', 'run', '--project', 'Tests/Smoke/Smoke.csproj', '--no-restore', '-c', 'Release', f'-p:RimWorldAssemblyDir={game}', '-p:UseSharedCompilation=false', '--disable-build-servers', '--verbosity', 'quiet'], ROOT, BUILD / 'smoke.log', env)
        for mode in ('custom', 'disabled'):
            run(['dotnet', str(ROOT / 'Tests/Smoke/bin/Release/net10.0/Smoke.dll'), mode],
                ROOT, BUILD / f'smoke-log-{mode}.log', env)
        game_runtime_smoke(game, ROOT / 'Tests/Smoke/bin/Release/net10.0', env)
        print('Build and both runtime smoke checks passed.', flush=True)
    package(record)
    run(['python3', '-I', str(ROOT / 'scripts/mod-package.py'), '--write-manifest', str(ROOT / 'Release-payload.json')], ROOT, BUILD / 'payload-manifest.log', env)


if __name__ == '__main__':
    main()
