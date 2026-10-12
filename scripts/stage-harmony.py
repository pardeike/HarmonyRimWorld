#!/usr/bin/env python3
"""Stage the NuGet-selected net10.0 Harmony DLL and its exact provenance locally."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package-dir', type=Path, required=True)
    parser.add_argument('--built-library', type=Path, required=True)
    parser.add_argument('--metadata-tool', type=Path, required=True)
    args = parser.parse_args()
    package_dir = args.package_dir.resolve()
    library = package_dir / 'lib/net10.0/0Harmony.dll'
    nuspec = ET.parse(package_dir / 'lib.harmony.nuspec').getroot()
    ns = {'n': nuspec.tag.split('}')[0].removeprefix('{')}
    info = nuspec.find('n:metadata', ns)
    version = info.findtext('n:version', namespaces=ns)
    if info.findtext('n:id', namespaces=ns) != 'Lib.Harmony' or not version.startswith('3.'):
        raise SystemExit('Expected the official Lib.Harmony 3 package.')
    if digest(library) != digest(args.built_library):
        raise SystemExit('NuGet did not select the package\'s native net10.0 Harmony asset.')
    metadata = json.loads(subprocess.check_output(
        ['dotnet', str(args.metadata_tool), str(library)], text=True))
    if (metadata['TargetFramework'] != '.NETCoreApp,Version=v10.0'
            or not metadata['AnyCPU'] or not metadata['AssemblyVersion'].startswith('3.')):
        raise SystemExit(f'Unexpected Harmony runtime metadata: {metadata}')
    repository = info.find('n:repository', ns)
    provenance = {
        'path': '1.7/Assemblies/0Harmony.dll', 'sha256': digest(library),
        'assemblyVersion': metadata['AssemblyVersion'],
        'informationalVersion': metadata['InformationalVersion'],
        'targetFramework': 'net10.0', 'anyCPU': metadata['AnyCPU'],
        'packageId': 'Lib.Harmony', 'packageVersion': version,
        'packageAsset': 'lib/net10.0/0Harmony.dll',
        'packageSha256': digest(package_dir / f'lib.harmony.{version}.nupkg'),
        'reportedSourceCommit': repository.get('commit') if repository is not None else None,
        'origin': f'Official NuGet package Lib.Harmony {version}.',
        'updatePolicy': 'NuGet selects the newest downloaded Harmony 3 package. scripts/build-release.sh fetches the latest before the offline build.',
    }
    destination = ROOT / provenance['path']
    record = ROOT / 'Harmony-provenance.json'
    if destination.is_file() and digest(destination) == provenance['sha256'] and record.is_file():
        if json.loads(record.read_text()) == provenance:
            print(f'Using official Lib.Harmony {version} net10.0 (unchanged).')
            return
    backup = ROOT / '.build/library-backups' / str(time.time_ns())
    backup.mkdir(parents=True, mode=0o700)
    for old in (destination, record):
        if old.exists():
            shutil.copy2(old, backup / old.name)
    staged_library = backup / 'new-0Harmony.dll'
    staged_record = backup / 'new-provenance.json'
    shutil.copy2(library, staged_library)
    staged_record.write_text(json.dumps(provenance, indent=2) + '\n')
    try:
        staged_library.replace(destination)
        staged_record.replace(record)
    except BaseException:
        for old in (destination, record):
            saved = backup / old.name
            if saved.exists():
                shutil.copy2(saved, old)
            else:
                old.unlink(missing_ok=True)
        raise
    print(f'Staged official Lib.Harmony {version} net10.0, SHA-256 {provenance["sha256"]}.')


if __name__ == '__main__':
    main()
