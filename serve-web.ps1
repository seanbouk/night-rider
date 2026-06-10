# Serve the WebGL build locally and open it in the browser.
# WebGL won't run from file:// — it must be served over HTTP.
# The build uses Brotli compression with Unity's decompression fallback enabled,
# so a plain static server (no Content-Encoding headers) works fine.
#
# Usage:  pwsh ./serve-web.ps1            # serves Builds/WebGL on :8080
#         pwsh ./serve-web.ps1 -Port 9000

param(
    [int]$Port = 8080,
    [string]$Dir = "Builds/WebGL"
)

if (-not (Test-Path (Join-Path $Dir "index.html"))) {
    Write-Error "No build at '$Dir'. Build first (Unity menu: Night Rider > Build WebGL)."
    exit 1
}

$url = "http://localhost:$Port/"
Write-Host "Serving '$Dir' at $url  (Ctrl+C to stop)"
Start-Process $url

$python = (Get-Command python -ErrorAction SilentlyContinue) ?? (Get-Command python3 -ErrorAction SilentlyContinue)
if ($python) {
    Push-Location $Dir
    try { & $python.Source -m http.server $Port }
    finally { Pop-Location }
}
else {
    Write-Warning "Python not found. Either install Python, or use Unity's File > Build And Run (it serves + opens automatically)."
}
