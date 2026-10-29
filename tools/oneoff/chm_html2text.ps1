# Convert one GBK-encoded CHM topic HTML into plain text.
# Usage: powershell -ExecutionPolicy Bypass -File chm_html2text.ps1 -In <html> -Out <txt>
param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$Out
)

$enc = [Text.Encoding]::GetEncoding(936)
$html = [IO.File]::ReadAllText($In, $enc)

# Drop script/style blocks.
$html = [regex]::Replace($html, '(?is)<script.*?</script>', ' ')
$html = [regex]::Replace($html, '(?is)<style.*?</style>', ' ')

# Table cells and rows become delimited text.
$html = [regex]::Replace($html, '(?i)</t[dh]>', ' | ')
$html = [regex]::Replace($html, '(?i)</tr>', "`r`n")
$html = [regex]::Replace($html, '(?i)<br\s*/?>', "`r`n")
$html = [regex]::Replace($html, '(?i)</p>', "`r`n")
$html = [regex]::Replace($html, '(?i)</div>', "`r`n")
$html = [regex]::Replace($html, '(?i)</h[1-6]>', "`r`n")
$html = [regex]::Replace($html, '(?i)</li>', "`r`n")

# Strip remaining tags.
$html = [regex]::Replace($html, '(?s)<[^>]+>', '')

# Entities.
$html = $html.Replace('&nbsp;', ' ').Replace('&lt;', '<').Replace('&gt;', '>')
$html = $html.Replace('&quot;', '"').Replace('&#39;', "'").Replace('&amp;', '&')

# Collapse whitespace.
$html = [regex]::Replace($html, '[ \t\u00a0]+', ' ')
$html = [regex]::Replace($html, '(\r?\n[ \t]*){2,}', "`r`n")

[IO.File]::WriteAllText($Out, $html.Trim(), (New-Object Text.UTF8Encoding($false)))
Write-Output ('BYTES=' + $html.Length)
