# EasyNet read-only probe (ASCII only).
# Sends reference-protocol frames to a PLC/simulator and prints raw responses.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\oneoff\easynet_probe.ps1 [-Host 127.0.0.1] [-Port 12939]

param(
    [string]$TargetHost = '127.0.0.1',
    [int]$TargetPort = 12939
)

$ErrorActionPreference = 'Stop'

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

function Send-Frame {
    param($Stream, [byte[]]$Frame)
    $prefix = [BitConverter]::GetBytes([uint16]$Frame.Length)
    $Stream.Write($prefix, 0, 2)
    $Stream.Write($Frame, 2, $Frame.Length - 2)
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

$client = New-Object System.Net.Sockets.TcpClient
$client.NoDelay = $true
$client.ReceiveTimeout = 5000
$client.Connect($TargetHost, $TargetPort)
$stream = $client.GetStream()
Write-Host ("connected {0}:{1}" -f $TargetHost, $TargetPort)

# name, address (hex bit address), bits
$cases = @(
    @('bit  1 @2100070',  0x02100070, 1),
    @('bit 100 @2100070', 0x02100070, 100),
    @('word 2 @21001C0',  0x021001C0, 32),
    @('word 2 @501FE0',   0x0501FE0,  32),
    @('word 25 @5044C0',  0x05044C0,  400),
    @('bit  1 @100064',   0x0100064,  1),
    @('word 100 @2003EA0',0x02003EA0, 1600),
    @('word 500 @2003EA0',0x02003EA0, 8000),
    @('word 1000 @2003EA0',0x02003EA0, 16000),
    @('word 2000 @2003EA0',0x02003EA0, 32000),
    @('word 4000 @2003EA0',0x02003EA0, 64000),
    @('bit 8000 @2100070',0x02100070, 8000),
    @('bit 16000 @2100070',0x02100070, 16000)
)

foreach ($case in $cases) {
    $name = $case[0]; $addr = [int]$case[1]; $bits = [int]$case[2]
    $frame = New-ReadFrame -Address $addr -Bits $bits
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $resp = Send-Frame -Stream $stream -Frame $frame
        $sw.Stop()
        $dataLen = $resp.Length - 22
        $errFlag = if ($resp.Length -ge 9) { $resp[8] } else { -1 }
        $errCode = if ($resp.Length -ge 12) { [BitConverter]::ToUInt16($resp, 10) } else { -1 }
        Write-Host ("[{0}] req={1}" -f $name, (Hex $frame))
        Write-Host ("[{0}] resp({1}B, [8]=0x{2:X2}, err={3})={4}" -f $name, $resp.Length, $errFlag, $errCode, (Hex $resp))
        Write-Host ("[{0}] elapsed={1} ms" -f $name, $sw.Elapsed.TotalMilliseconds.ToString('F2'))
        if ($dataLen -gt 0) {
            $data = New-Object byte[] $dataLen
            [Array]::Copy($resp, 22, $data, 0, $dataLen)
            $preview = if ($dataLen -le 32) { Hex $data } else { (Hex ($data[0..31])) + ' ...' }
            Write-Host ("[{0}] data({1}B)={2}" -f $name, $dataLen, $preview)
        }
    }
    catch {
        Write-Host ("[{0}] FAILED: {1}" -f $name, $_.Exception.Message)
        break
    }
}

$stream.Close()
$client.Close()

# ---- 突发吞吐：连续 20 次 2000 字读，中间不打印 ----
$client2 = New-Object System.Net.Sockets.TcpClient
$client2.NoDelay = $true
$client2.ReceiveTimeout = 5000
$client2.Connect($TargetHost, $TargetPort)
$stream2 = $client2.GetStream()
$frameBurst = New-ReadFrame -Address 0x02003EA0 -Bits 32000
$swBurst = [Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 20; $i++) { $null = Send-Frame -Stream $stream2 -Frame $frameBurst }
$swBurst.Stop()
Write-Host ("[burst] 20 x 2000-word: total={0} ms avg={1} ms" -f `
    $swBurst.Elapsed.TotalMilliseconds.ToString('F1'), `
    ($swBurst.Elapsed.TotalMilliseconds / 20).ToString('F2'))
$stream2.Close()
$client2.Close()

Write-Host 'done'
