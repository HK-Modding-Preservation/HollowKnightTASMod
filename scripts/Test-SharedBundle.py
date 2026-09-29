"""Run the packaged executables with no system .NET/PATH assistance."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('mod_root', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
root = args.mod_root.resolve() / 'Companion' / 'win-x64'
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, PATH='', DOTNET_ROOT=str(output / 'absent-dotnet'),
           DOTNET_ROOT_X64=str(output / 'absent-dotnet'), DOTNET_MULTILEVEL_LOOKUP='0',
           COREHOST_TRACE='1')
results = []


def run(name, executable, arguments=(), stdin=''):
    trace = output / (name + '.hosttrace.txt')
    result = subprocess.run([str(root / executable), *arguments], input=stdin,
                            text=True, encoding='utf-8', capture_output=True,
                            cwd=output, env=dict(env, COREHOST_TRACEFILE=str(trace)),
                            timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    (output / (name + '.stdout.txt')).write_text(result.stdout, encoding='utf-8')
    (output / (name + '.stderr.txt')).write_text(result.stderr, encoding='utf-8')
    evidence = trace.read_text(encoding='utf-8-sig')
    assert str(root / 'hostfxr.dll').lower() in evidence.lower(), name + ': local hostfxr'
    assert str(root / 'coreclr.dll').lower() in evidence.lower(), name + ': local coreclr'
    assert 'C:\\Program Files\\dotnet\\shared'.lower() not in evidence.lower(), name + ': global runtime'
    results.append(dict(name=name, exit_code=result.returncode, local_runtime=True))
    return result


# The caller must close existing Studio instances first. Its own timed exit is
# used so normal App cleanup runs; no game or save is touched by this test.
studio_trace = output / 'studio.hosttrace.txt'
studio = subprocess.Popen([str(root / 'HollowKnightTAS.Companion.exe'),
                           '--headless', '--exit-after-seconds=20'], cwd=output,
                          env=dict(env, COREHOST_TRACEFILE=str(studio_trace)),
                          creationflags=subprocess.CREATE_NO_WINDOW)
try:
    time.sleep(3)
    assert studio.poll() is None, 'Studio must remain running'
    fixture = Path(__file__).resolve().parent.parent / 'fixtures/movie/golden/actions-v1.canonical.hktas'
    cli = run('cli', 'Tools/HollowKnightTAS.Cli.exe', ['movie', 'validate', str(fixture)])
    assert cli.returncode == 0, cli.stderr + cli.stdout
    # Full MCP initialize/tools/list is exercised separately against a live game
    # endpoint. With Studio idle there is intentionally no automation endpoint.
    mcp = run('mcp', 'Tools/HollowKnightTAS.AgentBridge.exe', stdin='not-json\n')
    responses = [json.loads(line) for line in mcp.stdout.splitlines()]
    assert mcp.returncode == 0 and len(responses) == 1, responses
    assert responses[0]['error']['code'] == -32700, responses
    native = run('native-rejected-parent', 'Native/HollowKnightTAS.NativeHost.exe')
    assert native.returncode != 0 and 'verified sibling Companion' in native.stdout, native.stdout
    assert studio.wait(timeout=30) == 0
    trace = studio_trace.read_text(encoding='utf-8-sig').lower()
    assert str(root / 'coreclr.dll').lower() in trace
    results.append(dict(name='studio', exit_code=0, local_runtime=True))
finally:
    # Timed normal shutdown remains in force if an assertion failed.
    if studio.poll() is None:
        studio.wait(timeout=30)
(output / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps(results, indent=2))
