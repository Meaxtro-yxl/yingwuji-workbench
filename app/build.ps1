# 构建 exe 到工作台根目录，文件名跟 配置.js 里的 name 走。只用系统自带的 .NET Framework 4.8 编译器。
# 本文件必须保存为 UTF-8 带 BOM，否则 Windows PowerShell 5 会把中文路径读乱。
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ico  = Join-Path $here 'icon\app.ico'

$name = '内容爆款工作台'
$cfgPath = Join-Path $root '配置.js'
if (Test-Path -LiteralPath $cfgPath) {
  $raw = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8)
  $m = [regex]::Match($raw, '"name"\s*:\s*"([^"]+)"')
  if ($m.Success -and $m.Groups[1].Value.Trim()) { $name = $m.Groups[1].Value.Trim() }
}
$out = Join-Path $root ($name + '.exe')

if (-not (Test-Path -LiteralPath $ico)) { & (Join-Path $here 'make_icon.ps1') -Out $ico }

$running = Get-Process | Where-Object { $_.Path -eq $out }
if ($running) { Write-Host '应用正在运行，先关掉它再编译。'; exit 1 }

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
  "/win32icon:$ico" "/out:$out" `
  "/r:$here\lib\Microsoft.Web.WebView2.Core.dll" "/r:$here\lib\Microsoft.Web.WebView2.WinForms.dll" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Xml.Linq.dll /r:System.Xml.dll /r:System.Core.dll `
  "$here\src\Program.cs"
if ($LASTEXITCODE -ne 0) { Write-Host '编译失败'; exit 1 }

# WebView2 运行库要和 exe 放一起
Copy-Item "$here\lib\*.dll" $root -Force
Write-Host "built $out"
