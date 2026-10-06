"""Regenerate Windows icons and favicon from ui/public/logo.svg using the installed Tauri CLI."""
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
ui = root / 'ui'
source = ui / 'public/logo.svg'
icons = ui / 'src-tauri/icons'
with tempfile.TemporaryDirectory() as directory:
    subprocess.run([
        'node', str(ui / 'node_modules/@tauri-apps/cli/tauri.js'),
        'icon', str(source), '--output', directory,
    ], check=True, cwd=ui)
    for name, size in [('32x32.png', 32), ('128x128.png', 128), ('128x128@2x.png', 256)]:
        image = Path(directory, name)
        assert image.read_bytes()[:8] == b'\x89PNG\r\n\x1a\n'
        assert struct.unpack('>II', image.read_bytes()[16:24]) == (size, size)
        shutil.copyfile(image, icons / name)
    ico = Path(directory, 'icon.ico')
    assert struct.unpack('<HH', ico.read_bytes()[:4]) == (0, 1)
    shutil.copyfile(ico, icons / 'icon.ico')
shutil.copyfile(source, ui / 'public/favicon.svg')
assert (ui / 'public/favicon.svg').read_bytes() == source.read_bytes()
print('Brand icons regenerated and checked: 32/128/256 px, multi-size ICO and SVG favicon.')
