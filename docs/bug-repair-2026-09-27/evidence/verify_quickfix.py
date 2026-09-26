"""Real stdio LSP replay of BUG-003; never writes the audit fixture."""
import json, pathlib, subprocess, threading, queue, time, sys

root = pathlib.Path(r'C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\audit-closure\A21-quickfix')
exe = sys.argv[1]
p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
events = queue.Queue()
transcript = []

def read():
    while True:
        headers = {}
        while True:
            line = p.stdout.readline()
            if not line: return
            if line == b'\r\n': break
            k, v = line.decode().split(':', 1)
            headers[k.lower()] = v.strip()
        msg = json.loads(p.stdout.read(int(headers['content-length'])))
        transcript.append({'received': msg})
        events.put(msg)

threading.Thread(target=read, daemon=True).start()

def send(method, params, id=None):
    msg = dict(jsonrpc='2.0', method=method, params=params)
    if id is not None: msg['id'] = id
    transcript.append({'sent': msg})
    body = json.dumps(msg).encode()
    p.stdin.write(f'Content-Length: {len(body)}\r\n\r\n'.encode() + body)
    p.stdin.flush()

def receive(predicate):
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        msg = events.get(timeout=max(.01, deadline - time.monotonic()))
        if predicate(msg): return msg
    raise TimeoutError('LSP response or diagnostics')

def request(id, method, params):
    send(method, params, id)
    msg = receive(lambda m: m.get('id') == id)
    assert 'error' not in msg, msg
    return msg['result']

uri = (root / 'src/main.rocket').as_uri()
original = (root / 'src/main.rocket').read_text()

def diagnostics(version):
    return receive(lambda m: m.get('method') == 'textDocument/publishDiagnostics'
                   and m['params']['uri'] == uri and m['params'].get('version') == version)['params']['diagnostics']

def actions(id, diagnostic):
    return request(id, 'textDocument/codeAction', {'textDocument': {'uri': uri},
        'range': diagnostic['range'], 'context': {'diagnostics': [diagnostic], 'only': ['quickfix']}})

def apply(source, edits):
    def offset(position):
        lines = source.splitlines(keepends=True)
        prefix = sum(len(line) for line in lines[:position['line']])
        segment = lines[position['line']] if position['line'] < len(lines) else ''
        return prefix + len(segment.encode('utf-16-le')[:position['character'] * 2].decode('utf-16-le'))
    for edit in sorted(edits, key=lambda e: offset(e['range']['start']), reverse=True):
        start, end = offset(edit['range']['start']), offset(edit['range']['end'])
        source = source[:start] + edit['newText'] + source[end:]
    return source

try:
    request(1, 'initialize', {'processId': None, 'rootUri': root.as_uri(), 'capabilities': {},
                             'workspaceFolders': [{'uri': root.as_uri(), 'name': root.name}]})
    send('initialized', {})
    for name in ['math', 'main']:
        path = root / 'src' / f'{name}.rocket'
        send('textDocument/didOpen', {'textDocument': {'uri': path.as_uri(), 'languageId': 'rocket',
                                                     'version': 1, 'text': path.read_text()}})
    problem = next(d for d in diagnostics(1) if d.get('code') == 'R4002')
    fix = next(a for a in actions(2, problem) if a.get('kind') == 'quickfix')
    edits = fix['edit']['changes'][uri]
    assert len(edits) == 2, edits
    fixed = apply(original, edits)
    assert fixed.count('import src.math') == 1 and 'return src.math.doubled(21)' in fixed, fixed
    send('textDocument/didChange', {'textDocument': {'uri': uri, 'version': 2}, 'contentChanges': [{'text': fixed}]})
    assert not diagnostics(2), 'Applied fix must clear diagnostics'
    assert not [a for a in actions(3, problem) if a.get('kind') == 'quickfix'], 'Stale range must be rejected'
    imported = 'import src.math\n' + original
    send('textDocument/didChange', {'textDocument': {'uri': uri, 'version': 3}, 'contentChanges': [{'text': imported}]})
    problem = next(d for d in diagnostics(3) if d.get('code') == 'R4002')
    fix = next(a for a in actions(4, problem) if a.get('kind') == 'quickfix')
    edits = fix['edit']['changes'][uri]
    assert len(edits) == 1 and fix['title'] == 'Qualify doubled with src.math', fix
    fixed = apply(imported, edits)
    assert fixed.count('import src.math') == 1 and 'return src.math.doubled(21)' in fixed
    send('textDocument/didChange', {'textDocument': {'uri': uri, 'version': 4}, 'contentChanges': [{'text': fixed}]})
    assert not diagnostics(4), 'Existing import qualification must clear diagnostics'
    request(5, 'shutdown', None)
    send('exit', {})
    assert p.wait(timeout=10) == 0
    print('PASS: import+qualification clears R4002; stale range rejected; existing import gets qualification only; clean shutdown')
finally:
    if p.poll() is None: p.kill(); p.wait()
    pathlib.Path(__file__).with_suffix('.json').write_text(json.dumps(transcript, indent=2), encoding='utf-8')
