# EasyNet limit probe v2 (ASCII only). Read-only except one no-op write-back.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\oneoff\easynet_limit_probe.ps1

param(
    [string]$TargetHost = '127.0.0.1',
    [int]$TargetPort = 12939
)

$ErrorActionPreference = 'Stop'

function New-Frame {
    param([int]$Address, [int]$Bits, [int]$Command)
    $f = New-Object byte[] 22
    $tpl = [byte[]](0x16,0x00,0x01,0x03,0x01,0x00,0x00,0x00,0x01,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
    [Array]::Copy($tpl, $f, 22)
    $f[8] = [byte]$Command
    $a = [BitConverter]::GetBytes([int]$Address)
    [Array]::Copy($a, 0, $f, 14, 4)
    $b = [BitConverter]::GetBytes([int]$Bits)
    $f[18] = $b[0]; $f[19] = $b[1]; $f[20] = $b[2]
    return $f
}

function Send-Frame {
    param($Stream, [byte[]]$Frame)
    $Stream.Write($Frame, 0, $Frame.Length)
    $Stream.Flush()

    $head = New-Object byte[] 2
    $got = 0
    while ($got -lt 2) {
        $n = $Stream.Read($head, $got, 2 - $got)
        if ($n -le 0) { throw 'remote closed while reading length' }
        $got += $n
    }
    $total = [BitConverter]::ToUInt16($head, 0)
    $rest = New-Object byte[] ($total - 2)
    $got = 0
    while ($got -lt $rest.Length) {
        $n = $Stream.Read($rest, $got, $rest.Length - $got)
        if ($n -le 0) { throw 'remote closed while reading body' }
        $got += $n
    }
    $frame = New-Object byte[] $total
    [Array]::Copy($head, 0, $frame, 0, 2)
    [Array]::Copy($rest, 0, $frame, 2, $rest.Length)
    return $frame
}

function Hex([byte[]]$Data) { return (($Data | ForEach-Object { $_.ToString('X2') }) -join ' ') }

function Show-Result {
    param([string]$Label, [byte[]]$Resp)
    $cmd = if ($Resp.Length -ge 9) { $Resp[8] } else { -1 }
    $err = if ($Resp.Length -ge 12) { [BitConverter]::ToUInt16($Resp, 10) } else { -1 }
    $note = if ($cmd -eq 0x0F) { 'ERROR' } else { 'OK' }
    Write-Host ("  {0,-28} len={1,5} [8]=0x{2:X2} err={3,4} {4}" -f $Label, $Resp.Length, $cmd, $err, $note)
}

$client = New-Object System.Net.Sockets.TcpClient
$client.NoDelay = $true
$client.ReceiveTimeout = 5000
$client.Connect($TargetHost, $TargetPort)
$stream = $client.GetStream()
Write-Host ("connected {0}:{1}" -f $TargetHost, $TargetPort)

# D800 -> bit address 800*16 = 12800 = 0x3200, type nibble 4 -> bytes 00 32 40 00
$dAddr = [BitConverter]::ToInt32([byte[]](0x00,0x32,0x40,0x00), 0)

Write-Host ''
Write-Host '=== word-area read limit (D800) ==='
foreach ($words in 70, 400, 500, 501, 512, 600, 800, 1000) {
    try { Show-Result -Label ("words={0} bits={1}" -f $words, ($words*16)) -Resp (Send-Frame -Stream $stream -Frame (New-Frame -Address $dAddr -Bits ($words*16) -Command 1)) }
    catch { Write-Host ("  words={0} FAILED {1}" -f $words, $_.Exception.Message); break }
}

Write-Host ''
Write-Host '=== bit-area read limit (M0) ==='
$mAddr = [int]0x00100000
foreach ($bits in 1, 8000, 8001, 8192) {
    try { Show-Result -Label ("bits={0}" -f $bits) -Resp (Send-Frame -Stream $stream -Frame (New-Frame -Address $mAddr -Bits $bits -Command 1)) }
    catch { Write-Host ("  bits={0} FAILED {1}" -f $bits, $_.Exception.Message); break }
}

Write-Host ''
Write-Host '=== U-area read (raw 0x2100070, the user variable) ==='
try { Show-Result -Label 'U2100070 bits=16' -Resp (Send-Frame -Stream $stream -Frame (New-Frame -Address ([int]0x02100070) -Bits 16 -Command 1)) }
catch { Write-Host ("  U FAILED {0}" -f $_.Exception.Message) }

Write-Host ''
Write-Host '=== write ack format (write back the SAME 1 word to D800, no semantic change) ==='
$readFrame = New-Frame -Address $dAddr -Bits 16 -Command 1
$resp = Send-Frame -Stream $stream -Frame $readFrame
$dataLen = $resp.Length - 22
if ($dataLen -le 0) {
    Write-Host ("  read before failed, len={0} - skip write test" -f $resp.Length)
} else {
    $data = New-Object byte[] $dataLen
    [Array]::Copy($resp, 22, $data, 0, $dataLen)
    Write-Host ("  read-back before: {0}" -f (Hex $data))

    $w = New-Object byte[] (22 + $data.Length)
    [byte[]](0x16,0x00,0x01,0x03,0x01,0x00,0x00,0x00,0x02,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00) | ForEach-Object -Begin { $i = 0 } -Process { $w[$i] = $_; $i++ }
    $ab = [BitConverter]::GetBytes($dAddr)
    [Array]::Copy($ab, 0, $w, 14, 4)
    [Array]::Copy($data, 0, $w, 22, $data.Length)
    $total = $w.Length
    $w[0] = [byte]$total; $w[1] = [byte]($total -shr 8)
    $bitCount = $data.Length * 8
    $w[18] = [byte]$bitCount; $w[19] = [byte]($bitCount -shr 8); $w[20] = [byte]($bitCount -shr 16)
    Write-Host ("  write req : {0}" -f (Hex $w))
    $wresp = Send-Frame -Stream $stream -Frame $w
    Write-Host ("  write resp(len={0}, [8]=0x{1:X2}): {2}" -f $wresp.Length, $wresp[8], (Hex $wresp))

    $resp2 = Send-Frame -Stream $stream -Frame $readFrame
    if (($resp2.Length - 22) -gt 0) {
        $data2 = New-Object byte[] ($resp2.Length - 22)
        [Array]::Copy($resp2, 22, $data2, 0, $data2.Length)
        Write-Host ("  read-back after : {0}" -f (Hex $data2))
    }
}

$stream.Close()
$client.Close()
Write-Host 'done'
