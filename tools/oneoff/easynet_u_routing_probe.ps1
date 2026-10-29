# EasyNet U-area routing probe (ASCII only).
# Writes 1 to UB 210119B, then checks whether the same cell is visible as M2811.
# Rationale: our U encoding is raw 32-bit LE (0x0210119B -> 9B 11 10 02). Byte[2] high nibble = 1
# means "M/S area" on a reference server, and the low 20 bits = 0x0119B = 2811 -> M2811.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\oneoff\easynet_u_routing_probe.ps1

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

function New-ReadBitsFrame {
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

function New-WriteBitFrame {
    param([int]$Address, [bool]$Value)
    $payload = [byte[]]@( $(if ($Value) { 1 } else { 0 }) )
    $f = New-Object byte[] (22 + $payload.Length)
    $tpl = [byte[]](0x16,0x00,0x01,0x03,0x01,0x00,0x00,0x00,0x02,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
    [Array]::Copy($tpl, $f, 22)
    $a = [BitConverter]::GetBytes([int]$Address)
    [Array]::Copy($a, 0, $f, 14, 4)
    [Array]::Copy($payload, 0, $f, 22, $payload.Length)
    $tb = [BitConverter]::GetBytes([int]$f.Length)
    $f[0] = $tb[0]; $f[1] = $tb[1]
    $bits = 1
    $bb = [BitConverter]::GetBytes([int]$bits)
    $f[18] = $bb[0]; $f[19] = $bb[1]; $f[20] = $bb[2]
    return $f
}

function Hex([byte[]]$Data) { return (($Data | ForEach-Object { $_.ToString('X2') }) -join ' ') }

$uAddr = [BitConverter]::ToInt32([byte[]](0x9B,0x11,0x10,0x02), 0)
$mAddr = [BitConverter]::ToInt32([byte[]](0x9B,0x11,0x10,0x00), 0)

$client = New-Object System.Net.Sockets.TcpClient
$client.NoDelay = $true
$client.ReceiveTimeout = 5000
$client.Connect($TargetHost, $TargetPort)
$stream = $client.GetStream()
Write-Host ("connected {0}:{1}" -f $TargetHost, $TargetPort)
Write-Host ("U addr bytes = {0}" -f (Hex ([BitConverter]::GetBytes($uAddr))))
Write-Host ("M addr bytes = {0}" -f (Hex ([BitConverter]::GetBytes($mAddr))))
Write-Host ''

function Read-Bit {
    param($Stream, [int]$Address, [string]$Label)
    $resp = Send-Frame -Stream $Stream -Frame (New-ReadBitsFrame -Address $Address -Bits 1)
    if ($resp.Length -le 22) {
        Write-Host ("  read {0}: len={1} [8]=0x{2:X2} (no data)" -f $Label, $resp.Length, $resp[8])
        return $null
    }
    return (($resp[22] -band 1) -eq 1)
}

Write-Host '=== before ==='
$uBefore = Read-Bit -Stream $stream -Address $uAddr -Label 'UB 210119B'
$mBefore = Read-Bit -Stream $stream -Address $mAddr -Label 'M2811'
Write-Host ("  UB 210119B = {0}" -f $uBefore)
Write-Host ("  M2811      = {0}" -f $mBefore)

Write-Host ''
Write-Host '=== write UB 210119B = 1 ==='
$writeResp = Send-Frame -Stream $stream -Frame (New-WriteBitFrame -Address $uAddr -Value $true)
Write-Host ("  write resp(len={0}, [8]=0x{1:X2}) = {2}" -f $writeResp.Length, $writeResp[8], (Hex $writeResp))

Write-Host ''
Write-Host '=== after ==='
$uAfter = Read-Bit -Stream $stream -Address $uAddr -Label 'UB 210119B'
$mAfter = Read-Bit -Stream $stream -Address $mAddr -Label 'M2811'
Write-Host ("  UB 210119B = {0}" -f $uAfter)
Write-Host ("  M2811      = {0}" -f $mAfter)

Write-Host ''
if ($uAfter -eq $true -and $mAfter -eq $true) {
}
elseif ($uAfter -eq $true -and $mAfter -ne $true) {
}
else {
}

$stream.Close()
$client.Close()
Write-Host 'done'
