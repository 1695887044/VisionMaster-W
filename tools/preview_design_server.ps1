$ErrorActionPreference = 'Stop'

# R1/R2: this script is pure ASCII. The Chinese folder name is built from code points
# so the file stays 7-bit clean under Windows PowerShell 5.1 (ANSI) parsing.
# UI 设计基线  =  U I 8BBE 8BA1 57FA 7EBF
$root = 'D:\C#\VM\docs\' +
        [string][char]0x0055 + [string][char]0x0049 +
        [string][char]0x8BBE + [string][char]0x8BA1 +
        [string][char]0x57FA + [string][char]0x7EBF

if (-not (Test-Path -LiteralPath $root)) {
    Write-Output "ROOT NOT FOUND: $root"
    exit 1
}

$port = 8917
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$port/")
$listener.Start()
Write-Output "Serving $root at http://127.0.0.1:$port/"
Write-Output "Open http://127.0.0.1:$port/log-console/design.html  (Ctrl+C to stop)"

while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
        $rel = [System.Uri]::UnescapeDataString($ctx.Request.Url.LocalPath).TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'index.html' }
        $rel = $rel -replace '/', '\'
        $full = Join-Path $root $rel
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $bytes = [System.IO.File]::ReadAllBytes($full)
            switch ([System.IO.Path]::GetExtension($full).ToLowerInvariant()) {
                '.html' { $ctx.Response.ContentType = 'text/html; charset=utf-8' }
                '.css'  { $ctx.Response.ContentType = 'text/css; charset=utf-8' }
                '.js'   { $ctx.Response.ContentType = 'application/javascript; charset=utf-8' }
                '.png'  { $ctx.Response.ContentType = 'image/png' }
                default { $ctx.Response.ContentType = 'application/octet-stream' }
            }
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        else {
            $ctx.Response.StatusCode = 404
            $msg = [System.Text.Encoding]::UTF8.GetBytes("404 $rel")
            $ctx.Response.OutputStream.Write($msg, 0, $msg.Length)
        }
        $ctx.Response.OutputStream.Close()
    }
    catch {
        # keep serving; a client disconnect must not kill the preview server
    }
}
