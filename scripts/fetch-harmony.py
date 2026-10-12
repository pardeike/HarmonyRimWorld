#!/usr/bin/env python3
"""Download only public Lib.Harmony packages; restore and build happen separately offline."""
import argparse
import json
from pathlib import Path
import re
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FEED = ROOT / '.build/nuget-feed'
BASE = 'https://api.nuget.org/v3-flatcontainer/lib.harmony'


def version_key(version):
    match = re.fullmatch(r'(\d+)\.(\d+)\.(\d+)(?:-(\w+(?:[.-]\w+)*))?', version)
    if not match:
        raise ValueError(f'Unsupported NuGet version: {version}')
    major, minor, patch, prerelease = match.groups()
    # SemVer: releases sort after prereleases; numeric identifiers sort before text.
    suffix = tuple((0, int(part)) if part.isdigit() else (1, part.lower())
                   for part in (prerelease or '').split('.') if part)
    return (int(major), int(minor), int(patch), prerelease is None, suffix)


def inspect_package(path, version, asset):
    with zipfile.ZipFile(path) as package:
        if package.testzip() is not None:
            raise SystemExit(f'Corrupt NuGet package: {path.name}')
        nuspec = ET.fromstring(package.read('Lib.Harmony.nuspec'))
        namespace = {'n': nuspec.tag.split('}')[0].removeprefix('{')}
        metadata = nuspec.find('n:metadata', namespace)
        if (metadata.findtext('n:id', namespaces=namespace) != 'Lib.Harmony'
                or metadata.findtext('n:version', namespaces=namespace) != version
                or asset not in package.namelist()):
            raise SystemExit(f'Unexpected package identity or missing {asset}: {path.name}')


def download(version, asset):
    destination = FEED / f'lib.harmony.{version}.nupkg'
    if not destination.is_file():
        temporary = destination.with_suffix('.nupkg.tmp')
        try:
            # URL and default request headers contain no project path, name or content.
            with urllib.request.urlopen(f'{BASE}/{version}/{destination.name}', timeout=60) as response:
                with temporary.open('wb') as output:
                    while chunk := response.read(1024 * 1024):
                        output.write(chunk)
            inspect_package(temporary, version, asset)
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
    inspect_package(destination, version, asset)
    print(f'Available locally: Lib.Harmony {version}, {asset}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='latest', help='Latest Harmony 3 including prereleases, or an exact NuGet version.')
    args = parser.parse_args()
    version = args.version
    if version == 'latest':
        with urllib.request.urlopen(f'{BASE}/index.json', timeout=30) as response:
            versions = json.load(response)['versions']
        compatible = [item for item in versions if item.startswith('3.')]
        if not compatible:
            raise SystemExit('NuGet does not list any Harmony 3 packages.')
        version = max(compatible, key=version_key)
    if version_key(version)[0] != 3:
        parser.error('The active build requires a Harmony 3 package.')
    FEED.mkdir(parents=True, exist_ok=True, mode=0o700)
    download(version, 'lib/net10.0/0Harmony.dll')
    # Legacy remains pinned even when the active package floats forward.
    download('2.4.3', 'lib/net472/0Harmony.dll')
    print('Downloads complete. Restore/build remains offline.', flush=True)


if __name__ == '__main__':
    main()
