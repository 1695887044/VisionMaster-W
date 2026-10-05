namespace Plugin.BeadInspect
{
    /// <summary>
    /// 参考路径点列的编辑几何（方案说明书 §6.2 决策 D2）：
    /// · 左键加点 = 插到离点击处**最近的线段之间**（而非一律追加到末尾——避免补点把路径画成来回折返）；
    /// · 右键删点 = 删**最近**的点；
    /// · 拖动 = 改坐标。
    ///
    /// 纯静态、无 HALCON 依赖：配置界面（BeadInspectView / BeadInspectPlugin 配置态）与
    /// 断言 12（FlowCanvasChecks：插点保序 / 删点后仍可建模型，§7.2）共用同一份实现，
    /// 保证「断言测的就是界面用的」。
    /// </summary>
    public static class BeadPathEditor
    {
        /// <summary>右键删除的最大命中距离（图像 px）——点太远不删，防误触</summary>
        public const double DefaultDeleteTolerance = 50.0;

        /// <summary>拖动已有点的最大命中距离（图像 px）——约一个胶宽，超出按"加点"处理</summary>
        public const double DefaultDragTolerance = 12.0;

        /// <summary>
        /// 在点列中插入新点：插到离点击处最近的线段之间。
        /// 端点候选（插到最前 / 最后）带 0.999 的距离偏置——点击落在首/末段延长线上时
        /// 线段距离与端点距离相等，偏置让"往外延一截"的意图优先于"插进相邻段"。
        /// </summary>
        /// <returns>恒 true（除点列非法外）；<paramref name="insertIndex"/> 为新点下标</returns>
        public static bool InsertNearest(
            double[] rows,
            double[] cols,
            double clickRow,
            double clickCol,
            out double[] newRows,
            out double[] newCols,
            out int insertIndex,
            out string error)
        {
            newRows = Array.Empty<double>();
            newCols = Array.Empty<double>();
            insertIndex = -1;
            error = string.Empty;
            if (rows == null || cols == null || rows.Length != cols.Length)
            {
                error = "点列无效（行列数不一致）";
                return false;
            }

            int n = rows.Length;
            if (n == 0)
            {
                newRows = new[] { clickRow };
                newCols = new[] { clickCol };
                insertIndex = 0;
                return true;
            }

            if (n == 1)
            {
                // 单点没有"线段之间"的概念：默认接在后面（用户继续点会自然延长路径）
                newRows = new[] { rows[0], clickRow };
                newCols = new[] { cols[0], clickCol };
                insertIndex = 1;
                return true;
            }

            // 候选 1：插到最前（点击在首点外侧）
            double best = Dist(clickRow, clickCol, rows[0], cols[0]) * 0.999;
            insertIndex = 0;

            // 候选 2：插到某条线段之间
            for (int k = 0; k + 1 < n; k++)
            {
                double d = PointToSegmentDistance(clickRow, clickCol, rows[k], cols[k], rows[k + 1], cols[k + 1]);
                if (d < best)
                {
                    best = d;
                    insertIndex = k + 1;
                }
            }

            // 候选 3：接在末尾（点击在末点外侧）
            double tail = Dist(clickRow, clickCol, rows[n - 1], cols[n - 1]) * 0.999;
            if (tail < best)
                insertIndex = n;

            newRows = new double[n + 1];
            newCols = new double[n + 1];
            for (int i = 0, j = 0; i <= n; i++)
            {
                if (i == insertIndex)
                {
                    newRows[i] = clickRow;
                    newCols[i] = clickCol;
                }
                else
                {
                    newRows[i] = rows[j];
                    newCols[i] = cols[j];
                    j++;
                }
            }
            return true;
        }

        /// <summary>
        /// 删除离点击处最近的点。距离超过 <paramref name="maxDist"/> 时不删（防误触），返回 false 并给中文原因。
        /// </summary>
        public static bool DeleteNearest(
            double[] rows,
            double[] cols,
            double clickRow,
            double clickCol,
            double maxDist,
            out double[] newRows,
            out double[] newCols,
            out int deleteIndex,
            out string error)
        {
            newRows = Array.Empty<double>();
            newCols = Array.Empty<double>();
            deleteIndex = -1;
            error = string.Empty;
            if (rows == null || cols == null || rows.Length != cols.Length)
            {
                error = "点列无效（行列数不一致）";
                return false;
            }
            if (rows.Length == 0)
            {
                error = "点列为空：没有可删除的点";
                return false;
            }

            int n = rows.Length;
            int nearest = 0;
            double best = Dist(clickRow, clickCol, rows[0], cols[0]);
            for (int i = 1; i < n; i++)
            {
                double d = Dist(clickRow, clickCol, rows[i], cols[i]);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }
            if (best > maxDist)
            {
                error = $"距最近的点 {best:0.#}px，超过删除半径 {maxDist:0.#}px：未删除（防误触）";
                return false;
            }

            newRows = new double[n - 1];
            newCols = new double[n - 1];
            for (int i = 0, j = 0; i < n; i++)
            {
                if (i == nearest)
                    continue;
                newRows[j] = rows[i];
                newCols[j] = cols[i];
                j++;
            }
            deleteIndex = nearest;
            return true;
        }

        /// <summary>最近点下标（点列为空返回 -1）</summary>
        public static int NearestPointIndex(double[] rows, double[] cols, double row, double col, out double dist)
        {
            dist = double.MaxValue;
            if (rows == null || cols == null || rows.Length == 0 || rows.Length != cols.Length)
                return -1;
            int nearest = 0;
            dist = double.MaxValue;
            for (int i = 0; i < rows.Length; i++)
            {
                double d = Dist(row, col, rows[i], cols[i]);
                if (d < dist)
                {
                    dist = d;
                    nearest = i;
                }
            }
            return nearest;
        }

        /// <summary>点到线段的距离（投影参数截断到 [0,1]，端点即线段端点距离）</summary>
        public static double PointToSegmentDistance(
            double pr, double pc, double ar, double ac, double br, double bc)
        {
            double vr = br - ar, vc = bc - ac;
            double len2 = vr * vr + vc * vc;
            if (len2 < 1e-12)
                return Dist(pr, pc, ar, ac);
            double t = ((pr - ar) * vr + (pc - ac) * vc) / len2;
            t = Math.Clamp(t, 0.0, 1.0);
            return Dist(pr, pc, ar + t * vr, ac + t * vc);
        }

        /// <summary>欧氏距离</summary>
        public static double Dist(double r1, double c1, double r2, double c2) =>
            Math.Sqrt((r1 - r2) * (r1 - r2) + (c1 - c2) * (c1 - c2));
    }
}
