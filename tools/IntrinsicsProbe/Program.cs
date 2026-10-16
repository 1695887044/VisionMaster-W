using HalconDotNet;
using System.Text;

namespace IntrinsicsProbe;

/// <summary>
/// 标定板内参标定 · 可行性探针（第三轮：决定性扫描）。
///
/// 已知：合成图的**灰度极性**决定 find_calib_object 成败，且极性随板文件不同：
///   · 默认 (bg=128, plate=80, marks=224)  marks 亮 / 板暗 → calplate_*.cpd 能用；
///   · (bg=128, plate=200, marks=20)      marks 暗 / 板亮 → caltab_100mm.descr 能用。
/// 本轮：对每个板 × 两种极性 × 距离做网格扫描，再对能通的组合跑完整 12 张自验证，
/// 把"哪个板 + 哪套灰度 + 哪个距离"能稳定自验证钉死。
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();
    private static readonly string OutPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out.txt");

    private static void L(string s)
    {
        Log.AppendLine(s);
        Console.WriteLine(s);
    }

    private static string CalibDir =>
        Path.Combine(Environment.GetEnvironmentVariable("HALCONROOT") ?? string.Empty, "calib");

    private const double TrueFocus = 0.052, TrueKappa = -1500.0;
    private const double TrueSx = 5.2e-6, TrueSy = 5.2e-6, TrueCx = 640.0, TrueCy = 512.0;
    private const int ImgW = 1280, ImgH = 1024;

    private static HTuple CamPar() => new(
        "area_scan_division", TrueFocus, TrueKappa, TrueSx, TrueSy, TrueCx, TrueCy, ImgW, ImgH);

    private static readonly string[] Boards =
    {
        "calplate_40mm.cpd", "calplate_80mm.cpd", "calplate_160mm.cpd", "calplate_320mm.cpd",
        "caltab_30mm.descr", "caltab_100mm.descr", "caltab_200mm.descr",
    };

    // 极性：A=marks 亮/板暗；B=marks 暗/板亮
    private static readonly (string Name, int Bg, int Plate, int Marks)[] Polarities =
    {
        ("A(bg128/pl80/mk224)", 128, 80, 224),
        ("B(bg128/pl200/mk20)", 128, 200, 20),
    };

    private static readonly double[] Distances = { 0.5, 0.8, 1.2, 1.8, 2.5 };

    private static void Main()
    {
        L("========== 1. 板 × 极性 × 距离 网格扫描（居中正对）==========");
        var winners = new List<(string Board, string Pol, int Bg, int Plate, int Marks, double Tz)>();

        foreach (var board in Boards)
        {
            foreach (var pol in Polarities)
            {
                var hits = new List<string>();
                foreach (double tz in Distances)
                {
                    string r = TryFind(board, pol.Bg, pol.Plate, pol.Marks, tz);
                    if (r == "OK")
                    {
                        hits.Add($"{tz:0.0}");
                        winners.Add((board, pol.Name, pol.Bg, pol.Plate, pol.Marks, tz));
                    }
                }
                L($"  {board,-22} {pol.Name,-22} → 命中距离 [{string.Join(", ", hits)}]"
                  + (hits.Count == 0 ? "  （全挂）" : ""));
            }
        }

        L("");
        L("========== 2. 对命中组合跑完整 12 张自验证 ==========");
        if (winners.Count == 0) { L("  没有任何组合可用——内参标定在本机不可自验证"); Finish(); return; }

        foreach (var board in Boards)
        {
            var cand = winners.Where(w => w.Board == board).ToList();
            if (cand.Count == 0) { L($"  {board,-22} 无命中，跳过"); continue; }

            var pick = cand[cand.Count / 2];
            L($"  --- {board}  [{pick.Pol}] 基准距离 tz={pick.Tz:0.0} ---");
            SelfVerify(board, pick.Bg, pick.Plate, pick.Marks, pick.Tz);
        }

        L("");
        L("========== 3. 耗时（决定断言能否进常规 gate）==========");
        if (winners.Count > 0)
        {
            var w = winners[winners.Count / 2];
            L($"  用 {w.Board} [{w.Pol}] tz={w.Tz:0.0} 测 12 张全流程");
            Timing(w.Board, w.Bg, w.Plate, w.Marks, w.Tz, 12);
        }

        Finish();
    }

    private static void Finish()
    {
        File.WriteAllText(Path.GetFullPath(OutPath), Log.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"--- written: {Path.GetFullPath(OutPath)}");
    }

    private static HImage Sim(string descr, int bg, int pl, int mk,
        double tz, double rxDeg, double ryDeg, double tx, double ty, out HPose pose)
    {
        pose = new HPose(tx, ty, tz, rxDeg, ryDeg, 0.0, "Rp+T", "gba", "point");
        var img = new HImage();
        img.SimCaltab(Path.Combine(CalibDir, descr), new HCamPar(CamPar()), pose, bg, pl, mk, 1.0);
        return img;
    }

    private static string TryFind(string descr, int bg, int pl, int mk, double tz)
    {
        try
        {
            var img = Sim(descr, bg, pl, mk, tz, 0, 0, 0, 0, out _);
            var cd = NewCalibData(descr);
            cd.FindCalibObject(img, 0, 0, 0, new HTuple(), new HTuple());
            img.Dispose();
            return "OK";
        }
        catch (HalconException ex) { return $"FAIL #{ex.GetErrorCode()}"; }
    }

    private static HCalibData NewCalibData(string descr)
    {
        var cd = new HCalibData();
        cd.CreateCalibData("calibration_object", 1, 1);
        cd.SetCalibDataCamParam(new HTuple(0), new HTuple(), new HCamPar(CamPar()));
        cd.SetCalibDataCalibObject(0, new HTuple(Path.Combine(CalibDir, descr)));
        return cd;
    }

    private static void SelfVerify(string descr, int bg, int pl, int mk, double tz)
    {
        int count = 12;
        var images = new HImage[count];
        for (int i = 0; i < count; i++)
        {
            double a = 2 * Math.PI * i / count;
            images[i] = Sim(descr, bg, pl, mk,
                tz * (0.94 + 0.12 * (i % 3) / 2.0),
                16.0 * Math.Sin(a), 14.0 * Math.Cos(a),
                0.016 * Math.Cos(a), 0.012 * Math.Sin(a), out _);
        }

        var cd = NewCalibData(descr);
        int found = 0;
        for (int i = 0; i < count; i++)
        {
            try { cd.FindCalibObject(images[i], 0, 0, i, new HTuple(), new HTuple()); found++; }
            catch (HalconException ex) { L($"    第 {i + 1} 张失败 #{ex.GetErrorCode()}"); }
        }
        L($"    find_calib_object 成功 {found}/{count}");

        try
        {
            double rms = cd.CalibrateCameras();
            var rec = cd.GetCalibData(new HTuple("camera"), new HTuple(0), new HTuple("params"));
            double f = rec[1].D, k = rec[2].D, sx = rec[3].D, sy = rec[4].D, cx = rec[5].D, cy = rec[6].D;
            double fErr = Math.Abs(f - TrueFocus) / TrueFocus * 100;
            L($"    恢复 [{rec}]");
            L($"    focus : {f:G6} vs {TrueFocus:G6} → {fErr:0.####}%   (判据<0.5%) {(fErr < 0.5 ? "PASS" : "FAIL")}");
            L($"    kappa : {k:G6} vs {TrueKappa:G6} → 绝对误差 {Math.Abs(k - TrueKappa):G4}");
            L($"    cx,cy : ({cx:0.####},{cy:0.####}) → 偏差 ({Math.Abs(cx - TrueCx):0.####},{Math.Abs(cy - TrueCy):0.####}) px  (判据<1px)");
            L($"    sx,sy : {sx:G6},{sy:G6} vs {TrueSx:G6},{TrueSy:G6}");
            L($"    RMS   : {rms:0.####} px  (判据<0.3px) {(rms < 0.3 ? "PASS" : "FAIL")}");
        }
        catch (HalconException ex) { L($"    !! calibrate_cameras 失败 #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}"); }

        foreach (var im in images) im.Dispose();
    }

    private static void Timing(string descr, int bg, int pl, int mk, double tz, int count)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var images = new HImage[count];
        for (int i = 0; i < count; i++)
            images[i] = Sim(descr, bg, pl, mk, tz, i * 8.0, i * 5.0, 0.005 * (i % 3 - 1), 0.004 * (i % 4 - 2), out _);
        L($"    合成 {count} 张：{sw.ElapsedMilliseconds} ms");

        var cd = NewCalibData(descr);
        sw.Restart();
        for (int i = 0; i < count; i++)
        {
            try { cd.FindCalibObject(images[i], 0, 0, i, new HTuple(), new HTuple()); } catch (HalconException) { }
        }
        L($"    find_calib_object ×{count}：{sw.ElapsedMilliseconds} ms");

        sw.Restart();
        try { cd.CalibrateCameras(); } catch (HalconException) { }
        L($"    calibrate_cameras：{sw.ElapsedMilliseconds} ms");

        foreach (var im in images) im.Dispose();
    }
}
