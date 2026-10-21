# A3: normalize [ParallelSafe] pinyin trailing comments to Chinese.
# Comment-only edit; ASCII-only script per repo rule R1 (Chinese via codepoints).
# Read/write strictly UTF-8 (no BOM) via [IO.File] (repo rule R3/R4), String.Replace only.
$ErrorActionPreference = 'Stop'
$root = 'D:\C#\VM\Plugins'

function Z([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }

# ---- Chinese fragments built from codepoints ----
$chunShiJue    = Z @(0x7EAF,0x89C6,0x89C9,0x8BA1,0x7B97)                              # chun shi jue ji suan
$chunJiSuanLei = Z @(0x7EAF,0x8BA1,0x7B97,0x7C7B)                                      # chun ji suan lei
$danXiaoFei    = Z @(0x5355,0x6D88,0x8D39,0x7EBF,0x7A0B)                              # dan xiao fei xian cheng
$chuanXing     = Z @(0x4E32,0x884C)                                                    # chuan xing
$shiLiGeLi     = Z @(0x5B9E,0x4F8B,0x9694,0x79BB)                                      # shi li ge li
$juBuDuiXiang  = Z @(0x5C40,0x90E8,0x5BF9,0x8C61)                                      # ju bu dui xiang
$bujinShi      = Z @(0x6B65,0x8FDB,0x65F6)                                             # bu jin shi
$xiezuoQuxiao  = Z @(0x534F,0x4F5C,0x53D6,0x6D88)                                      # xie zuo qu xiao
$fanshi        = Z @(0x8303,0x5F0F)                                                    # fan shi
$xieZou        = Z @(0x5199,0x8D70)                                                    # xie zou
$jizhangZidian = Z @(0x8BB0,0x8D26,0x5B57,0x5178)                                      # ji zhang zi dian
$xieYou        = Z @(0x5199,0x7531)                                                    # xie you
$bianjiqi      = Z @(0x7F16,0x8F91,0x5668)                                             # bian ji qi
$jiancha       = Z @(0x68C0,0x67E5)                                                    # jian cha
$dai           = Z @(0x5E26)                                                           # dai

# ---- ordered replacements (longest first), old strings are pure ASCII ----
$pairs = @(
    @{ Old = 'chun shijue jisuan, shili geli + HALCON jubu duixiang'; New = ($chunShiJue + ', ' + $shiLiGeLi + ' + HALCON ' + $juBuDuiXiang) },
    @{ Old = 'RecordingHub BlockingCollection + dan xiaofei xiancheng chuanxing'; New = ('RecordingHub BlockingCollection + ' + $danXiaoFei + $chuanXing) },
    @{ Old = 'UploadQueue dan xiaofei xiancheng + SpoolLock'; New = ('UploadQueue ' + $danXiaoFei + ' + SpoolLock') },
    @{ Old = 'chun jisuan lei (50ms bujin shi xiezuo quxiao fanshi)'; New = ($chunJiSuanLei + ' (50ms ' + $bujinShi + $xiezuoQuxiao + $fanshi + ')') },
    @{ Old = 'Runtime xie zou jizhang zidian; Global xie you bianjianqi jiancha'; New = ('Runtime ' + $xieZou + $jizhangZidian + '; Global ' + $xieYou + $bianjiqi + $jiancha) },
    @{ Old = 'YoloSession.Cache dai CacheLock'; New = ('YoloSession.Cache ' + $dai + ' CacheLock') },
    @{ Old = 'chun shijue jisuan'; New = $chunShiJue },
    @{ Old = 'chun jisuan lei'; New = $chunJiSuanLei }
)

$utf8 = New-Object System.Text.UTF8Encoding($false)
$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)

$files = @(
    'Plugin.BeadInspect\BeadInspectPlugin.cs',
    'Plugin.BlobDetect\BlobDetectPlugin.cs',
    'Plugin.Calibration\CalibrationPlugin.cs',
    'Plugin.CaliperMeasure\CaliperMeasurePlugin.cs',
    'Plugin.CodeReader\CodeReaderPlugin.cs',
    'Plugin.ColorCheck\ColorCheckPlugin.cs',
    'Plugin.ColorRegion\RegionColorPlugin.cs',
    'Plugin.CreateRoi\CreateRoiPlugin.cs',
    'Plugin.DataRecord\DataRecordPlugin.cs',
    'Plugin.Ocr\OcrPlugin.cs',
    'Plugin.PoseTransform\PoseTransformPlugin.cs',
    'Plugin.PreProcessing\PreProcessingPlugin.cs',
    'Plugin.ResultUpload\ResultUploadPlugin.cs',
    'Plugin.Utility\ComparisonPlugin.cs',
    'Plugin.Utility\DataConversionPlugin.cs',
    'Plugin.Utility\DelayPlugin.cs',
    'Plugin.Utility\LogicOperationPlugin.cs',
    'Plugin.Utility\MathCalculationPlugin.cs',
    'Plugin.Utility\VariableAssignmentPlugin.cs',
    'Plugin.Yolo\YoloPlugin.cs'
)

$total = 0
foreach ($rel in $files) {
    $path = Join-Path $root $rel
    $beforeLines = ([IO.File]::ReadAllLines($path, $strictUtf8)).Count
    $text = [IO.File]::ReadAllText($path, $strictUtf8)   # strict decode: throws on bad utf-8
    $count = 0
    foreach ($p in $pairs) {
        $n = ([regex]::Matches($text, [regex]::Escape($p.Old))).Count
        if ($n -gt 0) {
            $text = $text.Replace($p.Old, $p.New)
            $count += $n
        }
    }
    if ($count -gt 0) {
        [IO.File]::WriteAllText($path, $text, $utf8)
        $afterLines = ([IO.File]::ReadAllLines($path, $strictUtf8)).Count
        if ($afterLines -ne $beforeLines) { throw ('LINE COUNT CHANGED: ' + $rel) }
        Write-Output ("OK {0} ({1} replaced)" -f $rel, $count)
        $total += $count
    }
    else {
        Write-Output ("SKIP {0} (nothing matched)" -f $rel)
    }
}
Write-Output ("TOTAL replaced: {0}" -f $total)

# leftover scan: any pinyin token left?
$left = Select-String -Path (Join-Path $root '*\*.cs') -Pattern 'chun |jubu|xiancheng chuanxing|jizhang|bianjianqi|bujin shi' -SimpleMatch:$false
if ($left) { $left | ForEach-Object { Write-Output ("LEFTOVER {0}:{1}" -f $_.Path, $_.LineNumber) } }
else { Write-Output 'LEFTOVER: none' }
