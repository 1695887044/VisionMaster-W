$ErrorActionPreference = 'Stop'

# R1/R2: this script must stay pure ASCII. The Chinese directory name is read
# from a UTF-8 manifest instead of being embedded in the script text.
$manifest = 'D:\C#\VM\tools\_uidesign_root.txt'
$root = [System.IO.File]::ReadAllLines($manifest, [System.Text.Encoding]::UTF8)[0].Trim()

if (-not (Test-Path -LiteralPath $root)) {
    [System.IO.File]::WriteAllText('D:\C#\VM\tools\_uidesign_server.err', "ROOT NOT FOUND: $root", [System.Text.Encoding]::UTF8)
    exit 1
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add('http://127.0.0.1:8917/')
$listener.Start()

[System.IO.File]::WriteAllText('D:\C#\VM\tools\_uidesign_server.err', "STARTED $root", [System.Text.Encoding]::UTF8)

while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
        $rel = [System.Uri]::UnescapeDataString($ctx.Request.Url.LocalPath).TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'index.html' }
        $rel = $rel -replace '/', '\'
        $full = Join-Path $root $rel
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $bytes = [System.IO.File]::ReadAllBytes($full)
            $ext = [System.IO.Path]::GetExtension($full).ToLowerInvariant()
            switch ($ext) {
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
        $err = $_.Exception.Message
        [System.IO.File]::AppendAllText('D:\C#\VM\tools\_uidesign_server.err', "`nLOOP: $err", [System.Text.Encoding]::UTF8)
    }
}
