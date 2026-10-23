#!/usr/bin/env bash
# Baut dist/WinCtl.exe (Windows-GUI-Anwendung, läuft auf .NET Framework 4.x = jedes Windows 10/11).
# Benötigt: Mono-Compiler (Debian/Ubuntu: apt install mono-mcs libmono-system-windows-forms4.0-cil libmono-system-web-extensions4.0-cil libmono-system-management4.0-cil)
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p dist
ICON=""
[ -f assets/winctl.ico ] && ICON="-win32icon:assets/winctl.ico"
mcs -target:winexe -out:dist/WinCtl.exe -optimize+ -debug- $ICON -win32manifest:assets/winctl.manifest \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -r:System.Web.dll -r:System.Web.Extensions.dll -r:System.Management.dll \
  src/*.cs
echo "OK -> dist/WinCtl.exe ($(stat -c%s dist/WinCtl.exe) Bytes)"
