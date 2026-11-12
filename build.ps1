$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll "/out:$PSScriptRoot\QuickPaste123.exe" "$PSScriptRoot\QuickPaste123.cs"
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }


