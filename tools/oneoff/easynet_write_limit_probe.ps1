# EasyNet write-limit probe (ASCII only).
# Measures the max payload per write request. Read-then-write-back: device state is unchanged.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\oneoff\easynet_write_limit_probe.ps1

param(
    [string]$TargetHost = '127.0.0.1',
    [int]$TargetPort = 12939
)

$ErrorActionPreference = 'Stop'

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

function New-ReadFrame {
    param([int]$Address, [int]$Bits)
    $f = New-Object byte[] 22
    $tpl = [byte[]](0x16,0x00,0x01,0x03,0x01,0x00,0x00,0x00,0x01,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
    [Array]::Copy($tpl, $f, 22)
    $a = [BitConverter]::GetBytes([int]$Address)
    [Array]::Copy($a, 0, $f, 14, 4)
    $b = [BitConverter]::GetBytes([int]$Bits)
    $f[18] = $b[0]; $f[19] = $b[1]; $f[20] = $b[2]
    return $f
}

function New-WriteFrame {
    param([int]$Address, [byte[]]$Payload)
    $f = New-Object byte[] (22 + $Payload.Length)
    $tpl = [byte[]](0x16,0x00,0x01,0x03,0x01,0x00,0x00,0x00,0x02,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
    [Array]::Copy($tpl, $f, 22)
    $a = [BitConverter]::GetBytes([int]$Address)
    [Array]::Copy($a, 0, $f, 14, 4)
    [Array]::Copy($Payload, 0, $f, 22, $Payload.Length)
    $total = $f.Length
    $tb = [BitConverter]::GetBytes([int]$total)
    $f[0] = $tb[0]; $f[1] = $tb[1]
    $bits = $Payload.Length * 8
    $bb = [BitConverter]::GetBytes([int]$bits)
    $f[18] = $bb[0]; $f[19] = $bb[1]; $f[20] = $bb[2]
    return $f
}

# D800 -> bit address 800*16 = 12800 = 0x3200, type nibble 4 -> bytes 00 32 40 00
$dAddr = [BitConverter]::ToInt32([byte[]](0x00,0x32,0x40,0x00), 0)

$client = New-Object System.Net.Sockets.TcpClient
$client.NoDelay = $true
$client.ReceiveTimeout = 5000
$client.Connect($TargetHost, $TargetPort)
$stream = $client.GetStream()
Write-Host ("connected {0}:{1}" -f $TargetHost, $TargetPort)

Write-Host ''
Write-Host '=== write payload limit (D800, read-then-write-back so device state is unchanged) ==='

foreach ($words in 100, 250, 400, 500, 501, 512, 600, 1000) {
    $bytes = $words * 2
    try {
        # 1) read the current bytes
        $readResp = Send-Frame -Stream $stream -Frame (New-ReadFrame -Address $dAddr -Bits ($words * 16))
        if ($readResp.Length -lt 22) {
            Write-Host ("  words={0,5} READ-REJECTED len={1}" -f $words, $readResp.Length)
            continue
        }
        $payload = New-Object byte[] ($readResp.Length - 22)
        [Array]::Copy($readResp, 22, $payload, 0, $payload.Length)

        # 2) write the same bytes back (no semantic change)
        $writeResp = Send-Frame -Stream $stream -Frame (New-WriteFrame -Address $dAddr -Payload $payload)
        $cmd = if ($writeResp.Length -ge 9) { $writeResp[8] } else { -1 }
        $err = if ($writeResp.Length -ge 12) { [BitConverter]::ToUInt16($writeResp, 10) } else { -1 }
        $note = if ($cmd -eq 0x06) { 'OK' } elseif ($cmd -eq 0x0F) { 'ERROR' } else { 'UNEXPECTED' }
        Write-Host ("  words={0,5} bytes={1,5} resp(len={2}, [8]=0x{3:X2}, err={4}) {5}" -f $words, $bytes, $writeResp.Length, $cmd, $err, $note)
    }
    catch {
        Write-Host ("  words={0,5} FAILED {1}" -f $words, $_.Exception.Message)
        break
    }
}

$stream.Close()
$client.Close()
Write-Host 'done'
