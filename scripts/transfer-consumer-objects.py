"""Restore the already-reviewed consumer commits with unchanged object identities."""
import base64
import hashlib
import json
import pathlib
import subprocess

root = pathlib.Path('docs/wp07-2026-09-27/transfer')
recipe = json.loads((root / 'zip-recipe.json').read_text())
for entry in recipe['entries']:
    path = pathlib.Path('transfer-stage') / entry['relative']
    assert hashlib.sha256(path.read_bytes()).hexdigest() == entry['file_sha256']
    subprocess.run(['git', 'hash-object', '-w', '--no-filters', str(path)], check=True, capture_output=True)
for obj in json.loads((root / 'consumer-objects.json').read_text()):
    result = subprocess.run(['git', 'hash-object', '-w', '-t', obj['type'], '--stdin'],
                            input=base64.b64decode(obj['data']), check=True, capture_output=True)
    assert result.stdout.decode().strip() == obj['id']
sha = '6a7f538234e24cc1de4bb9bb6fea737e570d5324'
subprocess.run(['git', 'rev-list', '--objects', '--missing=error', sha], check=True, capture_output=True)
subprocess.run(['git', 'update-ref', 'refs/heads/codex/rocketide-consumer-1.0.0', sha], check=True)
print('Restored exact consumer history:', sha)
