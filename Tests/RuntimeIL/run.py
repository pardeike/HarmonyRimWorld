#!/usr/bin/env python3
"""Build and run only the isolated runtime-IL prototype. It exits before game initialization."""
import os
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

here = Path(__file__).resolve().parent
output = Path(os.environ['HARMONY_RUNTIME_IL_ARTIFACTS']).resolve()
game = Path(os.environ.get('RIMWORLD_ASSEMBLY_DIR', str(Path.home() / 'Library/Application Support/Steam/steamapps/common/RimWorld/RimWorld.app/Contents/MacOS')))
output.mkdir(parents=True, exist_ok=True)
dotnet = os.environ.get('HARMONY_DOTNET', str(Path.home() / '.dotnet/dotnet'))
modes = os.environ.get('HARMONY_RUNTIME_IL_MODE', 'bridge').split(',')
live = modes == ['live']
if 'live' in modes and not live:
    raise SystemExit('The live baseline build must run separately from executable probes.')
project = here.parent / 'RuntimeILLive/RuntimeILLive.csproj' if live else here / 'RuntimeIL.csproj'
command = [dotnet, 'build', str(project), '-c', 'Release',
           '-p:HarmonyAssemblyPath=' + os.environ['HARMONY_ASSEMBLY_PATH'], '-p:GameDirectory=' + str(game),
           '-o', str(output / 'bin'), '--nologo', '-v:q']
steps = [('build', command, None)]
if not live:
    for mode in modes:
        steps.append(('runtime-' + mode, [str(game / 'RimWorld'), '-Headless', '-savedatafolder=' + str(output / ('savedata-' + mode))],
                     dict(os.environ, DOTNET_STARTUP_HOOKS=str(output / 'bin/RuntimeIL.dll'), HARMONY_RUNTIME_BRIDGE_TEST=mode)))
for name, command, environment in steps:
    log = output / (name + '.log')
    with log.open('w') as writer:
        try:
            result = subprocess.run(command, env=environment, cwd=game if environment else here,
                                    stdout=writer, stderr=subprocess.STDOUT, timeout=120)
        except subprocess.TimeoutExpired:
            print(f'failed: {name}; timed out; log: {log}')
            sys.exit(1)
    text = log.read_text(errors='replace')
    if result.returncode or environment and 'PASS ' + name not in text:
        print(f'failed: {name}; exit={result.returncode}; log: {log}')
        print(text[-3000:])
        sys.exit(1)
if live:
    baseline = output / 'baseline.bin'
    shutil.copy2(output / 'bin/Harmony.RuntimePipeline.LiveProof.dll', baseline)
    (output / 'manifest.json').write_text(json.dumps({'baselineSha256': hashlib.sha256(baseline.read_bytes()).hexdigest(),
        'scope': 'Private native-profiler coexistence and provider-status proof; no metadata deltas.'}, indent=2) + '\n')
print('ok')
