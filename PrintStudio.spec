# -*- mode: python ; coding: utf-8 -*-
import os
from PyInstaller.utils.hooks import collect_all

datas = [
    ('templates', 'templates'),
    ('static', 'static'),
    ('app_icon.ico', '.'),
]
binaries = []
hiddenimports = [
    'win32print',
    'win32gui',
    'win32ui',
    'win32con',
    'pypdfium2',
    'pypdfium2_raw',
    'PIL',
    'PIL.Image',
    'PIL.ImageDraw',
    'PIL.ImageWin',
    'clr',
    'pythonnet',
    'clr_loader',
    'webview',
    'werkzeug',
    'werkzeug.serving',
]

# Ensure raw binaries for pdfium and pythonnet are bundled
for pkg in ['pypdfium2_raw', 'pythonnet', 'clr_loader']:
    try:
        d, b, h = collect_all(pkg)
        datas += d
        binaries += b
        hiddenimports += h
    except Exception as e:
        print(f"Hook warning for {pkg}: {e}")

a = Analysis(
    ['desktop_app.py'],
    pathex=[],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['tkinter', 'matplotlib', 'scipy', 'numpy', 'torch', 'IPython'],
    noarchive=False,
    optimize=1,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='PrintStudio',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon='app_icon.ico',
)
