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

# Microsoft Edge WebView2 SDK (NuGet, redistributable): show_web uses it as modern browser. The DLLs are embedded
# gzip-compressed into the exe (see src/WebEngine.cs); the WebView2 runtime itself is part of Windows 11/10.
WV2_VER=1.0.4191.47
WV2_SHA=f492bbf547d0da329553b6727435b677579b1e9f91cc9e4a1ad029366d5f23d0
WV2=".refs/webview2"
if [ ! -f "$WV2/$WV2_VER.ok" ]; then
  mkdir -p .refs
  curl -sSL -o .refs/webview2.nupkg "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$WV2_VER/microsoft.web.webview2.$WV2_VER.nupkg"
  echo "$WV2_SHA  .refs/webview2.nupkg" | sha256sum -c --quiet
  rm -rf "$WV2" && mkdir -p "$WV2/pkg" "$WV2/gz"
  unzip -q .refs/webview2.nupkg 'lib/net462/*' 'runtimes/*/native/WebView2Loader.dll' -d "$WV2/pkg"
  gzip -9 -n -c "$WV2/pkg/lib/net462/Microsoft.Web.WebView2.Core.dll" > "$WV2/gz/wv2.core.dll.gz"
  gzip -9 -n -c "$WV2/pkg/lib/net462/Microsoft.Web.WebView2.WinForms.dll" > "$WV2/gz/wv2.winforms.dll.gz"
  for a in x64 x86 arm64; do gzip -9 -n -c "$WV2/pkg/runtimes/win-$a/native/WebView2Loader.dll" > "$WV2/gz/wv2.loader.$a.dll.gz"; done
  touch "$WV2/$WV2_VER.ok"
fi
WV2RES=""
for f in "$WV2"/gz/*.gz; do WV2RES="$WV2RES -resource:$f,$(basename "$f")"; done

ICON=""
[ -f assets/winctl.ico ] && ICON="-win32icon:assets/winctl.ico"
mcs -target:winexe -out:dist/WinCtl.exe -optimize+ -debug- $ICON -resource:assets/tray.png,tray.png $WV2RES -win32manifest:assets/winctl.manifest \
  -nostdlib -noconfig -lib:"$LIB" -r:mscorlib.dll \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -r:System.Web.dll -r:System.Web.Extensions.dll -r:System.Management.dll \
  -r:"$WV2/pkg/lib/net462/Microsoft.Web.WebView2.Core.dll" -r:"$WV2/pkg/lib/net462/Microsoft.Web.WebView2.WinForms.dll" \
  src/*.cs
echo "OK -> dist/WinCtl.exe ($(stat -c%s dist/WinCtl.exe) Bytes)"
