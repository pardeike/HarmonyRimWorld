#!/usr/bin/env python3
"""Validate/package tracked Harmony Mod payloads or install them locally. No uploads."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def payload():
    names = ['LoadFolders.xml', 'LICENSE', 'README.md']
    names += [f'About/{name}' for name in ('About.xml', 'Manifest.xml', 'ModIcon.png', 'Preview.png')]
    if (ROOT / 'About/PublishedFileId.txt').exists():
        names.append('About/PublishedFileId.txt')
    names += [f'{version}/Assemblies/{name}' for version in ('1.4', '1.5', '1.6', '1.7')
              for name in ('0Harmony.dll', 'HarmonyMod.dll')]
    for name in names:
        path = ROOT / name
        if not path.is_file() or path.is_symlink():
            raise SystemExit(f'Missing file or symlink in release payload: {name}')
    return names


def validate_legacy():
    record = json.loads((ROOT / 'Legacy-provenance.json').read_text())
    for name, expected in record['preservedLegacyFiles'].items():
        if digest(ROOT / name) != expected:
            raise SystemExit(f'Frozen legacy binary changed: {name}')


def validate(expected_version=None, manifest=None):
    validate_legacy()
    library = json.loads((ROOT / 'Harmony-provenance.json').read_text())
    if library['path'] != '1.7/Assemblies/0Harmony.dll' or digest(ROOT / library['path']) != library['sha256']:
        raise SystemExit('Harmony library provenance mismatch.')
    props = ET.parse(ROOT / 'Directory.Build.props').getroot()
    version = props.findtext('.//ModVersion')
    if (ET.parse(ROOT / 'About/About.xml').getroot().findtext('modVersion') != version
            or ET.parse(ROOT / 'About/Manifest.xml').getroot().findtext('version') != version):
        raise SystemExit('Release versions differ between project and mod metadata.')
    if expected_version and expected_version.removeprefix('v') != version:
        raise SystemExit('Requested release tag differs from the mod version.')
    if (ROOT / 'Current').exists():
        raise SystemExit('The obsolete Current directory must be removed.')
    folders = ET.parse(ROOT / 'LoadFolders.xml').getroot()
    for line in ('1.6', '1.7'):
        if [node.text for node in folders.findall(f'v{line}/li')] != ['/', line]:
            raise SystemExit(f'Incorrect load folder for {line}.')
    hashes = {name: digest(ROOT / name) for name in payload()}
    if manifest:
        expected = json.loads(Path(manifest).read_text())
        if version != expected['version'] or hashes != expected['payloadSha256']:
            raise SystemExit('Release payload differs from its frozen manifest.')
    return version, hashes


def write_zip(root, names, destination, prefix=''):
    destination = Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix('.zip.tmp')
    try:
        temporary.touch(mode=0o600)
        with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
            for name in sorted(names):
                entry = zipfile.ZipInfo(prefix + name, date_time=(1980, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                entry.create_system = 3
                entry.external_attr = 0o100644 << 16
                bundle.writestr(entry, (Path(root) / name).read_bytes(), compresslevel=9)
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


def deployment_target(mods_dir):
    mods = Path(mods_dir).expanduser().absolute()
    if not mods.is_dir() or mods.resolve() != mods:
        raise SystemExit('RIMWORLD_MOD_DIR must be an existing physical directory.')
    for name in ('RimWorld', 'RimWorldMac'):
        result = subprocess.run(['pgrep', '-x', name], capture_output=True)
        if result.returncode != 1:
            raise SystemExit('Quit RimWorld before deployment; process status must be known.')
    target = mods / 'HarmonyMod'
    if target.is_symlink() or target.with_suffix('.zip').is_symlink():
        raise SystemExit('Refusing a deployment symlink.')
    return target


def deploy(target):
    _, hashes = validate()
    with tempfile.TemporaryDirectory(prefix='.harmony-stage-', dir=target.parent) as temporary:
        stage = Path(temporary) / target.name
        stage.mkdir(mode=0o700)
        if sys.platform == 'darwin':
            subprocess.run(['/usr/bin/tmutil', 'addexclusion', str(stage)], check=True)
        for name in hashes:
            destination = stage / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, destination)
            if digest(destination) != hashes[name]:
                raise SystemExit(f'Deployment copy mismatch: {name}')
        archive = Path(temporary) / 'HarmonyMod.zip'
        write_zip(stage, hashes, archive)
        if sys.platform == 'darwin':
            subprocess.run(['/usr/bin/tmutil', 'addexclusion', str(archive)], check=True)
        deployment_target(str(target.parent))
        backup = Path(tempfile.mkdtemp(prefix='previous-', dir=ROOT / '.build'))
        installed = []
        saved = []
        try:
            for new, destination in ((stage, target), (archive, target.with_suffix('.zip'))):
                if destination.exists():
                    old = backup / destination.name
                    shutil.move(str(destination), old)
                    saved.append((old, destination))
                shutil.move(str(new), destination)
                installed.append(destination)
        except BaseException:
            for path in reversed(installed):
                shutil.rmtree(path) if path.is_dir() else path.unlink()
            for old, destination in reversed(saved):
                shutil.move(str(old), destination)
            raise
    print(f'Installed {len(hashes)} files and sibling ZIP into {target}.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-legacy', action='store_true')
    parser.add_argument('--mods-dir')
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--expected-version')
    parser.add_argument('--write-manifest', type=Path)
    args = parser.parse_args()
    (ROOT / '.build').mkdir(exist_ok=True)
    if args.validate_legacy:
        validate_legacy()
        return
    if args.mods_dir and args.check:
        deployment_target(args.mods_dir)
        return
    version, hashes = validate(args.expected_version, args.manifest)
    if args.write_manifest:
        args.write_manifest.write_text(json.dumps({'version': version, 'payloadSha256': hashes}, indent=2) + '\n')
    if args.mods_dir:
        deploy(deployment_target(args.mods_dir))
    if args.output:
        write_zip(ROOT, hashes, args.output, 'HarmonyMod/')
        print(f'Packaged version {version}; {len(hashes)} allow-listed files; SHA-256 {digest(args.output)}')


if __name__ == '__main__':
    main()
