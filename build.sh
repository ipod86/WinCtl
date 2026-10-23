#!/usr/bin/env bash
# Baut dist/WinCtl.exe (Windows-GUI-Anwendung, läuft auf .NET Framework 4.x = jedes Windows 10/11).
# Benötigt: Mono-Compiler, curl, unzip (Debian/Ubuntu: apt install mono-mcs unzip curl)
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p dist

# Compile against the real .NET Framework 4.6.2 reference assemblies (NuGet, MIT), not against Mono's own
# libraries: Mono has extra APIs (e.g. string.Trim(char)) that do not exist on Windows and fail at runtime.
REF_VER=1.0.3
REF_SHA=ee692a845743500910855d4c330ca6e9ef87c16e16f740e4474d187185d66e21
REFS=".refs/net462"
if [ ! -f "$REFS/build/.NETFramework/v4.6.2/mscorlib.dll" ]; then
  mkdir -p .refs
  curl -sSL -o .refs/net462.nupkg "https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net462/$REF_VER/microsoft.netframework.referenceassemblies.net462.$REF_VER.nupkg"
  echo "$REF_SHA  .refs/net462.nupkg" | sha256sum -c --quiet
  rm -rf "$REFS" && mkdir -p "$REFS" && unzip -q .refs/net462.nupkg -d "$REFS"
fi
LIB="$REFS/build/.NETFramework/v4.6.2"

ICON=""
[ -f assets/winctl.ico ] && ICON="-win32icon:assets/winctl.ico"
mcs -target:winexe -out:dist/WinCtl.exe -optimize+ -debug- $ICON -win32manifest:assets/winctl.manifest \
  -nostdlib -noconfig -lib:"$LIB" -r:mscorlib.dll \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -r:System.Web.dll -r:System.Web.Extensions.dll -r:System.Management.dll \
  src/*.cs
echo "OK -> dist/WinCtl.exe ($(stat -c%s dist/WinCtl.exe) Bytes)"
