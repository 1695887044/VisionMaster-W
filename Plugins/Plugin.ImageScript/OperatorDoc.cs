using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.ImageScript
{
    /// <summary>算子参数描述（名/类型/含义）。</summary>
    public sealed class OpParam
    {
        public string Name { get; }
        public string Type { get; }
        public string Desc { get; }

        public OpParam(string name, string type, string desc)
        {
            Name = name;
            Type = type;
            Desc = desc;
        }
    }

    /// <summary>一个 Halcon 算子的精编文档：签名 + 中文含义 + 参数说明 + 常见用途。</summary>
    public sealed class OpDoc
    {
        public string Name { get; }
        public string Category { get; }
        public string Summary { get; }
        public string Usage { get; }
        public OpParam[] Io { get; }   // 输入 iconic
        public OpParam[] Oo { get; }   // 输出 iconic
        public OpParam[] Ic { get; }   // 输入控制
        public OpParam[] Oc { get; }   // 输出控制

        public OpDoc(string name, string category, string summary, string usage,
            OpParam[] io, OpParam[] oo, OpParam[] ic, OpParam[] oc)
        {
            Name = name;
            Category = category;
            Summary = summary;
            Usage = usage;
            Io = io ?? Array.Empty<OpParam>();
            Oo = oo ?? Array.Empty<OpParam>();
            Ic = ic ?? Array.Empty<OpParam>();
            Oc = oc ?? Array.Empty<OpParam>();
        }

        /// <summary>调用形参序列（HDevelop 顺序：io, oo, ic, oc）。</summary>
        public OpParam[] CallParams => Io.Concat(Oo).Concat(Ic).Concat(Oc).ToArray();

        /// <summary>补全插入模板：op (P1, P2, ...)，形参名作占位。</summary>
        public string CallTemplate => Name + " (" + string.Join(", ", CallParams.Select(p => p.Name)) + ")";

        /// <summary>文档签名：op ( io : oo : ic : oc )。</summary>
        public string Signature =>
            $"{Name} ( {string.Join(", ", Io.Select(p => p.Name))} : {string.Join(", ", Oo.Select(p => p.Name))} : " +
            $"{string.Join(", ", Ic.Select(p => p.Name))} : {string.Join(", ", Oc.Select(p => p.Name))} )";
    }

    /// <summary>
    /// 内置中文精编算子库（工业视觉常用）+ 全量算子名（来自 Keyword.cs，供名称补全）。
    /// 数据单一来源：Docs 字典；Names 由精编库 + Keyword.s_HalconProcedure 合并。
    /// </summary>
    public static class OperatorDoc
    {
        private static readonly Dictionary<string, OpDoc> Docs = BuildDocs();

        /// <summary>全量算子名（精编库 ∪ Keyword 表，去重排序）。</summary>
        public static readonly string[] AllNames =
            Docs.Keys
                .Concat(Keyword.s_HalconProcedure.Split(
                    new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

        /// <summary>按名取精编文档（无则 null）。</summary>
        public static OpDoc Find(string name) =>
            name != null && Docs.TryGetValue(name, out var d) ? d : null;

        /// <summary>前缀匹配（补全列表用）：精编库优先，其余算子名兜底。</summary>
        public static List<OpCompletionData> Match(string prefix, int max = 60)
        {
            var list = new List<OpCompletionData>();
            if (string.IsNullOrEmpty(prefix)) return list;

            foreach (var n in AllNames)
            {
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new OpCompletionData(n, Find(n)));
                if (list.Count >= max) break;
            }
            return list;
        }

        // ---------- 精编数据 ----------

        private static OpParam P(string n, string t, string d) => new OpParam(n, t, d);
        private static OpParam[] A(params OpParam[] p) => p ?? Array.Empty<OpParam>();

        private static Dictionary<string, OpDoc> BuildDocs()
        {
            var docs = new List<OpDoc>
            {
                // ===== 图像获取 / 输入输出 =====
                new OpDoc("read_image", "图像IO",
                    "从文件/相机目录读取一幅图像",
                    "离线调试时读测试图；批量检测时按文件名规律读图",
                    A(), A(P("Image", "image", "读出的图像")),
                    A(P("Filename", "string", "图像文件路径或图像源名"),
                      P("Number", "integer", "多幅图时的编号，默认 1")), A()),

                new OpDoc("write_image", "图像IO",
                    "把图像保存为文件（'png'/'bmp'/'jpg' 等格式）",
                    "保存 NG 原图留档、生成检测报告附图",
                    A(P("Image", "image", "要保存的图像")), A(),
                    A(P("Format", "string", "文件格式，如 'png'"),
                      P("FileName", "string", "文件名（可含路径）")),
                    A(P("InfoChannel", "handle", "异步写句柄，一般忽略"))),

                new OpDoc("get_image_size", "图像IO",
                    "获取图像的宽和高（像素）",
                    "动态裁剪、坐标换算、判断视野是否合适",
                    A(P("Image", "image", "输入图像")), A(), A(),
                    A(P("Width", "integer", "图像宽度"), P("Height", "integer", "图像高度"))),

                new OpDoc("get_image_type", "图像IO",
                    "获取图像像素类型（byte/int2/uint2/real）",
                    "分支处理不同位深图像前的类型确认",
                    A(P("Image", "image", "输入图像")), A(), A(), A(P("Type", "string", "类型名"))),

                new OpDoc("count_channels", "图像IO",
                    "获取图像通道数",
                    "判断是否彩色图（3 通道）再决定转灰度",
                    A(P("Image", "image", "输入图像")), A(), A(), A(P("NumChannels", "integer", "通道数"))),

                new OpDoc("rgb1_to_gray", "颜色",
                    "RGB 彩色图转单通道灰度图",
                    "绝大多数形状/尺寸检测的第一步：彩色转灰度",
                    A(P("RGBImage", "image", "彩色图")), A(P("GrayImage", "image", "灰度图")), A(), A()),

                new OpDoc("decompose3", "颜色",
                    "三通道彩色图分解为 R/G/B 三个单通道图",
                    "只对某一颜色通道做阈值分割（如只检红色缺陷）",
                    A(P("Image", "image", "彩色图")),
                    A(P("R", "image", "红通道"), P("G", "image", "绿通道"), P("B", "image", "蓝通道")), A(), A()),

                new OpDoc("compose3", "颜色",
                    "R/G/B 三通道合成彩色图",
                    "把处理结果回填到某通道做可视化叠加",
                    A(P("R", "image", "红通道"), P("G", "image", "绿通道"), P("B", "image", "蓝通道")),
                    A(P("MultiChannelImage", "image", "合成彩色图")), A(), A()),

                new OpDoc("reduce_domain", "图像预处理",
                    "把图像的有效域限制到指定区域（域外不参与运算）",
                    "只在 ROI 内做 threshold/找边，提速并防误检",
                    A(P("Image", "image", "输入图像"), P("Region", "region", "限制区域")),
                    A(P("ImageReduced", "image", "域受限图像")), A(), A()),

                new OpDoc("get_domain", "区域",
                    "获取图像当前的有效域区域",
                    "取 reduce_domain 设置的域做后续分析",
                    A(P("Image", "image", "输入图像")), A(P("Region", "region", "当前域")), A(), A()),

                new OpDoc("full_domain", "图像预处理",
                    "把图像域恢复为整幅矩形",
                    "清除 reduce_domain 的影响，恢复全图处理",
                    A(P("Image", "image", "输入图像")), A(P("ImageFull", "image", "全域图像")), A(), A()),

                new OpDoc("crop_part", "图像预处理",
                    "裁剪图像的矩形子块",
                    "固定视野下截取关注区，减少数据量",
                    A(P("Image", "image", "输入图像")), A(P("ImagePart", "image", "裁剪结果")),
                    A(P("Row", "integer", "左上角行"), P("Column", "integer", "左上角列"),
                      P("Height", "integer", "高"), P("Width", "integer", "宽")), A()),

                new OpDoc("crop_domain", "图像预处理",
                    "按当前域的最小外接矩形裁剪图像",
                    "把 ROI 外像素彻底去掉，配合模板匹配",
                    A(P("Image", "image", "输入图像")), A(P("ImageResult", "image", "裁剪结果")), A(), A()),

                new OpDoc("mirror_image", "图像预处理",
                    "沿行/列/对角翻转图像",
                    "统一工件方向后再做模板匹配",
                    A(P("Image", "image", "输入图像")), A(P("ImageMirrored", "image", "翻转结果")),
                    A(P("Mode", "string", "'row'/'column'/'diagonal'")), A()),

                new OpDoc("scale_image_max", "图像预处理",
                    "把灰度线性拉伸到 0~255 满量程",
                    "低对比度图像增强，让阈值分割更稳定",
                    A(P("Image", "image", "输入图像")), A(P("ImageScaled", "image", "增强结果")), A(), A()),

                new OpDoc("scale_image", "图像预处理",
                    "按 g'=g*Mult+Add 做灰度线性变换",
                    "亮度/对比度调节",
                    A(P("Image", "image", "输入图像")), A(P("ImageScaled", "image", "变换结果")),
                    A(P("Mult", "number", "倍率"), P("Add", "number", "偏移")), A()),

                new OpDoc("convert_image_type", "图像预处理",
                    "转换像素类型（byte/uint2/real 等）",
                    "12 位相机图转 byte 显示；转 real 做浮点运算",
                    A(P("Image", "image", "输入图像")), A(P("ResultImage", "image", "结果图像")),
                    A(P("Type", "string", "目标类型 'byte'/'real'...")), A()),

                new OpDoc("copy_image", "图像IO",
                    "复制一幅图像（独立句柄）",
                    "保留原图副本供后续对比",
                    A(P("Source", "image", "源图像")), A(P("Destination", "image", "副本")), A(), A()),

                new OpDoc("clear_image", "图像IO",
                    "释放图像占用的内存",
                    "循环处理中及时释放，防止内存增长",
                    A(P("Image", "image", "要释放的图像")), A(), A(), A()),

                new OpDoc("gen_image_const", "图像生成",
                    "生成指定大小、恒定灰度的图像",
                    "造测试图、做掩膜底板",
                    A(), A(P("Image", "image", "生成图像")),
                    A(P("Width", "integer", "宽"), P("Height", "integer", "高"),
                      P("Value", "number", "灰度值")), A()),

                // ===== 滤波 / 增强 =====
                new OpDoc("gauss_image", "滤波",
                    "高斯平滑滤波",
                    "阈值前降噪，抑制细小毛刺",
                    A(P("Image", "image", "输入图像")), A(P("ImageGauss", "image", "平滑结果")),
                    A(P("MaskSize", "integer", "核尺寸（奇数），3~9 常用")), A()),

                new OpDoc("mean_image", "滤波",
                    "均值滤波",
                    "快速轻度降噪",
                    A(P("Image", "image", "输入图像")), A(P("ImageMean", "image", "结果")),
                    A(P("MaskWidth", "integer", "核宽"), P("MaskHeight", "integer", "核高")), A()),

                new OpDoc("median_image", "滤波",
                    "中值滤波（去椒盐噪声利器，保边缘）",
                    "去除孤立亮点/暗点",
                    A(P("Image", "image", "输入图像")), A(P("MedianImage", "image", "结果")),
                    A(P("MaskSize", "integer", "核尺寸"), P("By", "string", "'light'/'dark' 等"),
                      P("Mode", "string", "'histogram'/'circle' 等")), A()),

                new OpDoc("eq_histo_image", "滤波",
                    "直方图均衡化增强",
                    "光照不均的图像拉平对比度",
                    A(P("Image", "image", "输入图像")), A(P("ImageResult", "image", "结果")), A(), A()),

                new OpDoc("emphasize", "滤波",
                    "图像锐化增强",
                    "让边缘更突出，利于找边",
                    A(P("Image", "image", "输入图像")), A(P("ImageEmphasized", "image", "结果")),
                    A(P("MaskWidth", "integer", "核宽"), P("MaskHeight", "integer", "核高"),
                      P("Factor", "number", "增强系数")), A()),

                // ===== 阈值分割 =====
                new OpDoc("threshold", "阈值分割",
                    "按灰度区间提取区域（最基础最常用的分割算子）",
                    "亮目标/暗缺陷二值化提取，几乎所有检测流程的第一步",
                    A(P("Image", "image", "输入图像（域受限更有效）")),
                    A(P("Region", "region", "灰度落在区间内的区域")),
                    A(P("MinGray", "number", "灰度下限"), P("MaxGray", "number", "灰度上限")), A()),

                new OpDoc("dyn_threshold", "阈值分割",
                    "与局部参考图比较的动态阈值（适应光照渐变）",
                    "背景光照不均时提取缺陷",
                    A(P("Image", "image", "原图"), P("RefImage", "image", "大核平滑后的参考图")),
                    A(P("Region", "region", "结果区域")),
                    A(P("Offset", "number", "偏差阈值"),
                      P("Mode", "string", "'light'比参考亮/'dark'比参考暗")), A()),

                new OpDoc("var_threshold", "阈值分割",
                    "基于局部均值和标准差的动态阈值",
                    "纹理背景上的斑点缺陷检测",
                    A(P("Image", "image", "输入图像")), A(P("Region", "region", "结果")),
                    A(P("MaskSize", "integer", "统计窗口"), P("StdDevScale", "number", "标准差倍率"),
                      P("AbsThreshold", "number", "绝对阈值下限")), A()),

                new OpDoc("auto_threshold", "阈值分割",
                    "按灰度直方图自动求最优阈值（Otsu 类）",
                    "双峰直方图图像的免调参分割",
                    A(P("Image", "image", "输入图像")), A(P("Regions", "region", "各区间区域")),
                    A(P("Sigma", "number", "直方图平滑度")), A()),

                new OpDoc("binary_threshold", "阈值分割",
                    "自动二值化分割，可选亮/暗目标",
                    "文档/字符等二值图快速分割",
                    A(P("Image", "image", "输入图像")), A(P("Region", "region", "结果")),
                    A(P("Flag", "string", "'max_separability'"),
                      P("LightDark", "string", "'light'/'dark'")),
                    A(P("UsedThreshold", "number", "实际使用的阈值"))),

                new OpDoc("fast_threshold", "阈值分割",
                    "低对比度快速阈值（带最小对比度约束）",
                    "大幅面图像提速分割",
                    A(P("Image", "image", "输入图像")), A(P("Region", "region", "结果")),
                    A(P("MinContrast", "integer", "最小对比度"),
                      P("MinGray", "integer", "最小灰度"), P("Size", "integer", "最小区域尺寸")), A()),

                new OpDoc("histo_to_thresh", "阈值分割",
                    "由直方图计算分割阈值",
                    "自定义直方图策略时配合灰度直方图使用",
                    A(), A(),
                    A(P("Histogram", "array", "灰度直方图"),
                      P("Name", "string", "算法名 'smooth_histo'等"),
                      P("Value", "number", "算法参数")),
                    A(P("UsedThreshold", "number", "求得的阈值"))),

                // ===== 区域生成 / 运算 =====
                new OpDoc("gen_rectangle1", "区域生成",
                    "生成轴平行矩形区域",
                    "划定 ROI、画检测框",
                    A(), A(P("Rectangle", "region", "矩形区域")),
                    A(P("Row1", "number", "上边行"), P("Column1", "number", "左边列"),
                      P("Row2", "number", "下边行"), P("Column2", "number", "右边列")), A()),

                new OpDoc("gen_rectangle2", "区域生成",
                    "生成旋转矩形区域",
                    "有角度工件的 ROI 划定",
                    A(), A(P("Rectangle", "region", "旋转矩形")),
                    A(P("Row", "number", "中心行"), P("Column", "number", "中心列"),
                      P("Phi", "number", "角度(弧度)"),
                      P("Length1", "number", "半长边"), P("Length2", "number", "半短边")), A()),

                new OpDoc("gen_circle", "区域生成",
                    "生成圆形区域",
                    "圆形 ROI、生成结构元",
                    A(), A(P("Circle", "region", "圆区域")),
                    A(P("Row", "number", "圆心行"), P("Column", "number", "圆心列"),
                      P("Radius", "number", "半径")), A()),

                new OpDoc("gen_ellipse", "区域生成",
                    "生成椭圆区域",
                    "椭圆 ROI 或拟合结果可视化",
                    A(), A(P("Ellipse", "region", "椭圆区域")),
                    A(P("Row", "number", "中心行"), P("Column", "number", "中心列"),
                      P("Phi", "number", "角度"), P("Radius1", "number", "长半轴"),
                      P("Radius2", "number", "短半轴")), A()),

                new OpDoc("gen_region_points", "区域生成",
                    "由离散点生成区域",
                    "把坐标数组转成区域显示",
                    A(), A(P("Region", "region", "结果区域")),
                    A(P("Rows", "integer", "行数组"), P("Columns", "integer", "列数组"),
                      P("GrayValues", "integer", "灰度，默认 255")), A()),

                new OpDoc("gen_empty_region", "区域生成",
                    "生成空区域",
                    "循环累加前的初始化",
                    A(), A(P("EmptyRegion", "region", "空区域")), A(), A()),

                new OpDoc("union1", "区域运算",
                    "把区域数组全部并成一个区域",
                    "合并所有缺陷区域后整体统计",
                    A(P("Regions", "region", "输入区域数组")), A(P("RegionUnion", "region", "并集")), A(), A()),

                new OpDoc("union2", "区域运算",
                    "两个区域求并",
                    "合并两处 ROI",
                    A(P("Region1", "region", "区域1"), P("Region2", "region", "区域2")),
                    A(P("RegionUnion", "region", "并集")), A(), A()),

                new OpDoc("intersection", "区域运算",
                    "两个区域求交",
                    "判断缺陷是否落在指定检测带内",
                    A(P("Region1", "region", "区域1"), P("Region2", "region", "区域2")),
                    A(P("RegionIntersection", "region", "交集")), A(), A()),

                new OpDoc("difference", "区域运算",
                    "区域1减去区域2",
                    "排除已知背景区，只留新缺陷",
                    A(P("Region1", "region", "被减区域"), P("Region2", "region", "减去区域")),
                    A(P("RegionDifference", "region", "差集")), A(), A()),

                new OpDoc("complement", "区域运算",
                    "求区域补集",
                    "反转前景背景",
                    A(P("Region", "region", "输入区域")), A(P("RegionComplement", "region", "补集")), A(), A()),

                new OpDoc("clip_region", "区域运算",
                    "把区域裁剪到指定矩形范围",
                    "去掉视野边缘的溢出区域",
                    A(P("Region", "region", "输入区域")), A(P("RegionClipped", "region", "结果")),
                    A(P("Row1", "number", "上"), P("Column1", "number", "左"),
                      P("Row2", "number", "下"), P("Column2", "number", "右")), A()),

                new OpDoc("move_region", "区域运算",
                    "平移区域",
                    "按偏移复制 ROI",
                    A(P("Region", "region", "输入区域")), A(P("RegionMoved", "region", "结果")),
                    A(P("Row", "number", "行偏移"), P("Column", "number", "列偏移")), A()),

                // ===== 形态学 =====
                new OpDoc("dilation_circle", "形态学",
                    "圆盘结构元膨胀（区域长大）",
                    "连接断裂的细小线条",
                    A(P("Region", "region", "输入区域")), A(P("RegionDilation", "region", "结果")),
                    A(P("Radius", "number", "圆盘半径")), A()),

                new OpDoc("erosion_circle", "形态学",
                    "圆盘结构元腐蚀（区域缩小）",
                    "去除粘连、分离目标",
                    A(P("Region", "region", "输入区域")), A(P("RegionErosion", "region", "结果")),
                    A(P("Radius", "number", "圆盘半径")), A()),

                new OpDoc("opening_circle", "形态学",
                    "开运算：先腐蚀后膨胀",
                    "去孤立噪点，不改变目标大小",
                    A(P("Region", "region", "输入区域")), A(P("RegionOpening", "region", "结果")),
                    A(P("Radius", "number", "圆盘半径")), A()),

                new OpDoc("closing_circle", "形态学",
                    "闭运算：先膨胀后腐蚀",
                    "填平目标上的小孔洞/裂缝",
                    A(P("Region", "region", "输入区域")), A(P("RegionClosing", "region", "结果")),
                    A(P("Radius", "number", "圆盘半径")), A()),

                new OpDoc("fill_up", "形态学",
                    "填充区域中的所有孔洞",
                    "让环形/带孔工件变成实心区域",
                    A(P("Region", "region", "输入区域")), A(P("RegionFillUp", "region", "结果")), A(), A()),

                new OpDoc("boundary", "形态学",
                    "提取区域边界（内边界/外边界）",
                    "轮廓线检测、描边显示",
                    A(P("Region", "region", "输入区域")), A(P("RegionBoundary", "region", "边界区域")),
                    A(P("Type", "string", "'edge'外边界/'edge1'含内边界")), A()),

                new OpDoc("shape_trans", "形态学",
                    "把区域变换成凸包/外接矩形/圆等典型形状",
                    "求最小外接矩形框住目标",
                    A(P("Region", "region", "输入区域")), A(P("RegionTrans", "region", "结果")),
                    A(P("Mode", "string", "'convex'/'rectangle1'/'rectangle2'/'circle'等")), A()),

                // ===== 连通域 / 特征 =====
                new OpDoc("connection", "连通域",
                    "把区域按连通性拆分成区域数组",
                    "分割后拆分单个缺陷/单个工件，逐个分析",
                    A(P("Region", "region", "输入区域")), A(P("ConnectedRegions", "region", "拆分结果数组")), A(), A()),

                new OpDoc("select_shape", "连通域",
                    "按几何特征筛选区域数组",
                    "按面积/圆度/长宽比剔除干扰目标",
                    A(P("Regions", "region", "输入数组")), A(P("SelectedRegions", "region", "筛选结果")),
                    A(P("Features", "string", "特征名 'area'/'circularity'等"),
                      P("Operation", "string", "'and'/'or'"),
                      P("Min", "number", "下限"), P("Max", "number", "上限")), A()),

                new OpDoc("select_shape_std", "连通域",
                    "按标准特征（最大/最小/面积区间）筛选",
                    "快速取最大区域",
                    A(P("Regions", "region", "输入数组")), A(P("SelectedRegions", "region", "结果")),
                    A(P("StandardFeatures", "string", "'max_area'/'max_compactness'等"),
                      P("FeatureThreshold", "number", "阈值，-1=仅取最优")), A()),

                new OpDoc("area_center", "特征",
                    "计算区域面积与重心",
                    "尺寸判定与定位输出",
                    A(P("Regions", "region", "输入区域")), A(), A(),
                    A(P("Area", "number", "面积(像素)"),
                      P("Row", "number", "重心行"), P("Column", "number", "重心列"))),

                new OpDoc("orientation_region", "特征",
                    "计算区域主方向角",
                    "判断工件摆放角度",
                    A(P("Regions", "region", "输入区域")), A(), A(),
                    A(P("Phi", "number", "角度(弧度)"))),

                new OpDoc("circularity", "特征",
                    "计算区域圆度（越接近 1 越圆）",
                    "区分圆点和线状缺陷",
                    A(P("Regions", "region", "输入区域")), A(), A(), A(P("Circularity", "number", "圆度"))),

                new OpDoc("rectangularity", "特征",
                    "计算区域矩形度",
                    "区分矩形件与异形件",
                    A(P("Regions", "region", "输入区域")), A(), A(), A(P("Rectangularity", "number", "矩形度"))),

                new OpDoc("convexity", "特征",
                    "计算区域凸度（面积/凸包面积）",
                    "检测带缺口/凹陷的形状",
                    A(P("Regions", "region", "输入区域")), A(), A(), A(P("Convexity", "number", "凸度"))),

                new OpDoc("compactness", "特征",
                    "计算紧凑度（周长²/面积）",
                    "区分毛刺状与团状缺陷",
                    A(P("Regions", "region", "输入区域")), A(), A(), A(P("Compactness", "number", "紧凑度"))),

                new OpDoc("contlength", "特征",
                    "计算区域轮廓总长度",
                    "细长缺陷的量化",
                    A(P("Regions", "region", "输入区域")), A(), A(), A(P("ContLength", "number", "周长"))),

                new OpDoc("diameter_region", "特征",
                    "计算区域最大直径及两端点",
                    "长条缺陷的长度测量",
                    A(P("Regions", "region", "输入区域")), A(), A(),
                    A(P("Diameter", "number", "最大距离"), P("Row1", "number", "端点1行"),
                      P("Column1", "number", "端点1列"), P("Row2", "number", "端点2行"),
                      P("Column2", "number", "端点2列"))),

                new OpDoc("smallest_rectangle1", "特征",
                    "求区域的最小轴平行外接矩形",
                    "输出检测框坐标",
                    A(P("Regions", "region", "输入区域")), A(), A(),
                    A(P("Row1", "number", "上边"), P("Column1", "number", "左边"),
                      P("Row2", "number", "下边"), P("Column2", "number", "右边"))),

                new OpDoc("smallest_rectangle2", "特征",
                    "求区域的最小旋转外接矩形",
                    "有角度工件的定位框",
                    A(P("Regions", "region", "输入区域")), A(), A(),
                    A(P("Row", "number", "中心行"), P("Column", "number", "中心列"),
                      P("Phi", "number", "角度"), P("Length1", "number", "半长边"),
                      P("Length2", "number", "半短边"))),

                new OpDoc("smallest_circle", "特征",
                    "求区域的最小外接圆",
                    "圆形工件定位与直径测量",
                    A(P("Regions", "region", "输入区域")), A(), A(),
                    A(P("Row", "number", "圆心行"), P("Column", "number", "圆心列"),
                      P("Radius", "number", "半径"))),

                new OpDoc("inner_circle", "特征",
                    "求区域的最大内切圆",
                    "在有效区内找最稳的采样中心",
                    A(P("Region", "region", "输入区域")), A(), A(),
                    A(P("Row", "number", "圆心行"), P("Column", "number", "圆心列"),
                      P("Radius", "number", "半径"))),

                new OpDoc("intensity", "特征",
                    "计算区域在图像中的平均/偏差灰度",
                    "颜色/灰度类缺陷判定",
                    A(P("Regions", "region", "区域"), P("Image", "image", "图像")), A(), A(),
                    A(P("Intensity", "number", "平均灰度"), P("SymmDifference", "number", "偏差"))),

                new OpDoc("gray_features", "特征",
                    "计算区域内多种灰度统计特征",
                    "纹理分析、灰度直方图特征分类",
                    A(P("Regions", "region", "区域"), P("Image", "image", "图像")), A(), A(),
                    A(P("Features", "number", "特征值数组"))),

                // ===== XLD 轮廓 =====
                new OpDoc("edges_sub_pix", "XLD",
                    "亚像素边缘提取（Canny/Deriche/Lanser/Sobel）",
                    "高精度尺寸测量的轮廓来源",
                    A(P("Image", "image", "输入图像")), A(P("Edges", "xld", "边缘轮廓数组")),
                    A(P("Filter", "string", "'canny'/'deriche'/'lanser'/'sobel'"),
                      P("Alpha", "number", "平滑系数(1~3常用)"),
                      P("Low", "number", "低阈值"), P("High", "number", "高阈值")), A()),

                new OpDoc("threshold_sub_pix", "XLD",
                    "灰度等值线亚像素提取",
                    "二值化边界的高精度版本",
                    A(P("Image", "image", "输入图像")), A(P("Border", "xld", "等值线轮廓")),
                    A(P("Threshold", "number", "等值线灰度")), A()),

                new OpDoc("gen_contour_polygon_xld", "XLD",
                    "由点列生成折线轮廓",
                    "把测量点连线画出来",
                    A(), A(P("Contour", "xld", "生成轮廓")),
                    A(P("Row", "number", "行数组"), P("Col", "number", "列数组")), A()),

                new OpDoc("get_contour_xld", "XLD",
                    "取出轮廓的点坐标数组",
                    "遍历轮廓点做自定义计算",
                    A(P("Contour", "xld", "输入轮廓")), A(), A(),
                    A(P("Row", "number", "行数组"), P("Col", "number", "列数组"))),

                new OpDoc("length_xld", "XLD",
                    "计算轮廓长度",
                    "线状缺陷长度/周长测量",
                    A(P("Contours", "xld", "输入轮廓")), A(), A(), A(P("Length", "number", "长度"))),

                new OpDoc("fit_line_contour_xld", "XLD",
                    "对轮廓拟合直线（Tukey/Huber 抗差）",
                    "从边缘点集求精确直线参数",
                    A(P("Contours", "xld", "输入轮廓")), A(),
                    A(P("Algorithm", "string", "'regression'/'tukey'等"),
                      P("NumPoints", "integer", "采样点数"), P("MaxNumIterations", "integer", "迭代次数"),
                      P("ClusterNum", "integer", "线段数"), P("MinSize", "number", "最小长度"),
                      P("Alpha", "number", "角度约束"), P("Cutoff", "number", "离群截断"),
                      P("MaxDeviation", "number", "最大偏差")),
                    A(P("RowBegin", "number", "起点行"), P("ColumnBegin", "number", "起点列"),
                      P("RowEnd", "number", "终点行"), P("ColumnEnd", "number", "终点列"),
                      P("Nr", "number", "法向量行分量"), P("Nc", "number", "法向量列分量"),
                      P("Distance", "number", "原点到线距离"))),

                new OpDoc("fit_circle_contour_xld", "XLD",
                    "对轮廓拟合圆",
                    "从圆弧边缘点求圆心半径",
                    A(P("Contours", "xld", "输入轮廓")), A(),
                    A(P("Algorithm", "string", "'algebraic'/'ahc'等"),
                      P("MaxNumIterations", "integer", "迭代次数"),
                      P("MaxDegree", "number", "最大阶"), P("StartPoint", "integer", "起点"),
                      P("EndPoint", "integer", "终点"), P("MaxDeviation", "number", "最大偏差")),
                    A(P("Row", "number", "圆心行"), P("Column", "number", "圆心列"),
                      P("Radius", "number", "半径"), P("StartPhi", "number", "起始角"),
                      P("EndPhi", "number", "终止角"), P("PointOrder", "string", "点序"))),

                new OpDoc("select_contours_xld", "XLD",
                    "按长度/闭合性等筛选轮廓",
                    "剔除噪声短线，只留有效轮廓",
                    A(P("Contours", "xld", "输入轮廓数组")), A(P("SelectedContours", "xld", "结果")),
                    A(P("Mode", "string", "'contour_length_selected'等"),
                      P("Property", "string", "属性名"),
                      P("Lower", "number", "下限"), P("Upper", "number", "上限")), A()),

                new OpDoc("gen_circle_contour_xld", "XLD",
                    "生成圆形轮廓",
                    "叠加到图像上做对位检查可视化",
                    A(), A(P("Contour", "xld", "圆轮廓")),
                    A(P("Row", "number", "圆心行"), P("Column", "number", "圆心列"),
                      P("Radius", "number", "半径"), P("StartPhi", "number", "起始角"),
                      P("EndPhi", "number", "终止角"), P("PointOrder", "string", "'positive'/'negative'")),
                    A(P("Resolution", "number", "点密度"))),

                // ===== 模板匹配 =====
                new OpDoc("create_shape_model", "匹配",
                    "由图像/区域创建形状模板",
                    "形状匹配定位的第一步（配合 find_shape_model）",
                    A(P("Image", "image", "模板图像（建议 reduce_domain 到模板区域）")), A(),
                    A(P("AngleStart", "angle", "起始角"), P("AngleStep", "angle", "角度步"),
                      P("AngleExtent", "angle", "角度范围"),
                      P("Optimization", "string", "'auto'等"), P("Metric", "string", "'use_polarity'/'ignore_global_contrast'等"),
                      P("Contrast", "integer", "对比度（用图像时）"), P("MinContrast", "integer", "最小对比度")),
                    A(P("ModelId", "handle", "模板句柄"))),

                new OpDoc("add_shape_model_xld_contours", "匹配",
                    "用 XLD 轮廓添加模板形状",
                    "由测量轮廓（更干净）构建形状模板",
                    A(P("ModelId", "handle", "模板句柄"), P("Contours", "xld", "轮廓")), A(),
                    A(P("Weight", "number", "权重")), A()),

                new OpDoc("find_shape_model", "匹配",
                    "在图像中搜索形状模板（旋转/缩放不变）",
                    "工件定位、有无检测的主力算子",
                    A(P("Image", "image", "搜索图像")),
                    A(),
                    A(P("ModelID", "handle", "模板句柄"),
                      P("AngleStart", "angle", "起始角"), P("AngleExtent", "angle", "角度范围"),
                      P("MinScore", "number", "最低匹配分(0~1)"), P("NumMatches", "integer", "求前N个"),
                      P("MaxDeformable", "number", "最大偏差"), P("PyrLevel", "integer", "金字塔层"),
                      P("Greediness", "number", "贪婪度(0.3~0.8)")),
                    A(P("Row", "number", "匹配中心行"), P("Column", "number", "匹配中心列"),
                      P("Angle", "angle", "匹配角度"), P("Score", "number", "匹配分数"))),

                new OpDoc("find_scaled_shape_model", "匹配",
                    "带缩放的形状模板搜索",
                    "工件大小会变化的定位",
                    A(P("Image", "image", "搜索图像")), A(),
                    A(P("ModelID", "handle", "模板"), P("AngleStart", "angle", "起始角"),
                      P("AngleExtent", "angle", "角度范围"), P("ScaleMin", "number", "最小缩放"),
                      P("ScaleMax", "number", "最大缩放"), P("MinScore", "number", "最低分"),
                      P("NumMatches", "integer", "个数"), P("MaxDeformable", "number", "最大偏差"),
                      P("PyrLevel", "integer", "金字塔"), P("Greediness", "number", "贪婪度")),
                    A(P("Row", "number", "行"), P("Column", "number", "列"),
                      P("Angle", "angle", "角"), P("Scale", "number", "缩放"), P("Score", "number", "分"))),

                new OpDoc("get_shape_model_contours", "匹配",
                    "取出模板的轮廓（可视化用）",
                    "叠加模板轮廓检查模板质量",
                    A(), A(P("ModelContours", "xld", "模板轮廓")),
                    A(P("ModelID", "handle", "模板句柄"), P("Angle", "angle", "角度"),
                      P("Row", "number", "行"), P("Column", "number", "列"),
                      P("Scale", "number", "缩放")), A()),

                new OpDoc("clear_shape_model", "匹配",
                    "释放形状模板内存",
                    "流程结束/换型时释放句柄",
                    A(), A(), A(P("ModelID", "handle", "模板句柄")), A()),

                new OpDoc("create_ncc_model", "匹配",
                    "创建灰度(NCC)模板",
                    "无角度/低对比度目标的灰度匹配定位",
                    A(P("Image", "image", "模板图像")), A(),
                    A(P("AngleStart", "angle", "起始角"), P("AngleStep", "angle", "角步"),
                      P("AngleExtent", "angle", "角范围"), P("Optimization", "string", "优化方式"),
                      P("GrayValues", "string", "灰度处理"), P("Contrast", "integer", "对比度"),
                      P("MinContrast", "integer", "最小对比度")),
                    A(P("ModelId", "handle", "NCC模板句柄"))),

                new OpDoc("find_ncc_model", "匹配",
                    "搜索灰度(NCC)模板",
                    "灰度相似目标定位（如字符、图案）",
                    A(P("Image", "image", "搜索图像")), A(),
                    A(P("ModelID", "handle", "模板"), P("AngleStart", "angle", "起始角"),
                      P("AngleExtent", "angle", "角范围"), P("MinScore", "number", "最低分"),
                      P("NumMatches", "integer", "个数"), P("SubPixel", "string", "亚像素方式"),
                      P("Greediness", "number", "贪婪度")),
                    A(P("Row", "number", "行"), P("Column", "number", "列"),
                      P("Angle", "angle", "角"), P("Score", "number", "分"))),

                // ===== 变换 / 坐标 =====
                new OpDoc("hom_mat2d_identity", "2D变换",
                    "生成单位 2D 齐次变换矩阵",
                    "构造变换矩阵的起点",
                    A(), A(), A(), A(P("HomMat2D", "hom_mat2d", "单位矩阵"))),

                new OpDoc("hom_mat2d_translate", "2D变换",
                    "给变换矩阵追加平移",
                    "按偏移搬运区域/图像",
                    A(P("HomMat2D", "hom_mat2d", "输入矩阵")), A(P("HomMat2DTranslated", "hom_mat2d", "结果矩阵")),
                    A(P("Tx", "number", "列平移"), P("Ty", "number", "行平移")), A()),

                new OpDoc("hom_mat2d_rotate", "2D变换",
                    "给变换矩阵追加旋转（绕指定点）",
                    "按角度摆正工件",
                    A(P("HomMat2D", "hom_mat2d", "输入矩阵")), A(P("HomMat2DRotate", "hom_mat2d", "结果矩阵")),
                    A(P("Phi", "angle", "旋转角"), P("Px", "number", "中心列"), P("Py", "number", "中心行")), A()),

                new OpDoc("vector_angle_to_rigid", "2D变换",
                    "由一对点+角度求刚体变换矩阵",
                    "Mark 定位：把模板坐标系变换到当前工件坐标系",
                    A(), A(),
                    A(P("Row1", "number", "起点行"), P("Column1", "number", "起点列"), P("Angle1", "angle", "起点角"),
                      P("Row2", "number", "目标行"), P("Column2", "number", "目标列"), P("Angle2", "angle", "目标角")),
                    A(P("HomMat2D", "hom_mat2d", "刚体变换矩阵"))),

                new OpDoc("affine_trans_pixel", "2D变换",
                    "用矩阵变换像素坐标",
                    "把 ROI 坐标随工件位姿联动",
                    A(), A(),
                    A(P("Row", "number", "原行"), P("Column", "number", "原列"), P("HomMat2D", "hom_mat2d", "矩阵")),
                    A(P("RowTrans", "number", "变换行"), P("ColTrans", "number", "变换列"))),

                new OpDoc("affine_trans_region", "2D变换",
                    "仿射变换区域",
                    "ROI 随位姿旋转平移",
                    A(P("Region", "region", "输入区域")), A(P("RegionTrans", "region", "结果")),
                    A(P("HomMat2D", "hom_mat2d", "变换矩阵"), P("Interpolation", "string", "'nearest_neighbor'默认")), A()),

                new OpDoc("affine_trans_image", "2D变换",
                    "仿射变换图像（旋转/平移/缩放整幅图）",
                    "图像摆正后再测量",
                    A(P("Image", "image", "输入图像")), A(P("ImageAffineTrans", "image", "结果")),
                    A(P("HomMat2D", "hom_mat2d", "矩阵"),
                      P("Interpolation", "string", "'nearest_neighbor'/'bilinear'"),
                      P("AdaptVis", "string", "'true'自动扩大画幅")), A()),

                new OpDoc("affine_trans_contour_xld", "2D变换",
                    "仿射变换 XLD 轮廓",
                    "轮廓随位姿联动显示",
                    A(P("Contours", "xld", "输入轮廓")), A(P("ContoursAffineTrans", "xld", "结果")),
                    A(P("HomMat2D", "hom_mat2d", "矩阵")), A()),

                new OpDoc("hom_mat2d_to_affine_par", "2D变换",
                    "把 2D 矩阵分解为平移/旋转/缩放分量",
                    "读出位姿角与缩放做判定",
                    A(P("HomMat2D", "hom_mat2d", "输入矩阵")), A(), A(),
                    A(P("Sx", "number", "X缩放"), P("Sy", "number", "Y缩放"), P("Phi", "angle", "旋转角"),
                      P("Tx", "number", "平移X"), P("Ty", "number", "平移Y"))),

                // ===== 距离 / 几何 =====
                new OpDoc("distance_pp", "几何",
                    "两点间距离",
                    "孔距、边距测量",
                    A(), A(),
                    A(P("Row1", "number", "点1行"), P("Column1", "number", "点1列"),
                      P("Row2", "number", "点2行"), P("Column2", "number", "点2列")),
                    A(P("Distance", "number", "距离"))),

                new OpDoc("distance_pl", "几何",
                    "点到直线距离",
                    "边缘到基准线偏移",
                    A(), A(),
                    A(P("Row", "number", "点行"), P("Column", "number", "点列"),
                      P("Row1", "number", "线起点行"), P("Column1", "number", "线起点列"),
                      P("Row2", "number", "线终点行"), P("Column2", "number", "线终点列")),
                    A(P("Distance", "number", "距离"))),

                new OpDoc("angle_ll", "几何",
                    "两直线夹角",
                    "直角/角度判定",
                    A(), A(),
                    A(P("Row1", "number", "线1点A行"), P("Column1", "number", "线1点A列"),
                      P("Row2", "number", "线1点B行"), P("Column2", "number", "线1点B列"),
                      P("Row3", "number", "线2点A行"), P("Column3", "number", "线2点A列"),
                      P("Row4", "number", "线2点B行"), P("Column4", "number", "线2点B列")),
                    A(P("Angle", "angle", "夹角"))),

                new OpDoc("intersection_lines", "几何",
                    "两直线求交点",
                    "由两条边线求角点",
                    A(), A(),
                    A(P("Row1", "number", "线1A行"), P("Column1", "number", "线1A列"),
                      P("Row2", "number", "线1B行"), P("Column2", "number", "线1B列"),
                      P("Row3", "number", "线2A行"), P("Column3", "number", "线2A列"),
                      P("Row4", "number", "线2B行"), P("Column4", "number", "线2B列")),
                    A(P("Row", "number", "交点行"), P("Column", "number", "交点列"),
                      P("IsParallel", "boolean", "是否平行"))),

                // ===== 对象管理 =====
                new OpDoc("count_obj", "对象",
                    "统计对象（区域/图像/轮廓）个数",
                    "判断有没有检出目标",
                    A(P("Objects", "iconic", "对象集合")), A(), A(), A(P("Number", "integer", "个数"))),

                new OpDoc("select_obj", "对象",
                    "按序号取出集合中的一个对象（1 起）",
                    "取最大/第一个缺陷单独分析",
                    A(P("Objects", "iconic", "对象集合")), A(P("ObjectSelected", "iconic", "取出的对象")),
                    A(P("Index", "integer", "序号，从1开始")), A()),

                new OpDoc("sort_obj", "对象",
                    "按特征给对象数组排序",
                    "按位置从左到右排列工件",
                    A(P("Objects", "iconic", "对象集合")), A(P("SortedObjects", "iconic", "排序结果")),
                    A(P("Features", "string", "'first_point'等"), P("SortOrder", "string", "'true'升/'false'降")), A()),

                new OpDoc("concat_obj", "对象",
                    "把单个对象追加进集合",
                    "循环里累加结果",
                    A(P("Objects1", "iconic", "集合"), P("Objects2", "iconic", "追加对象")),
                    A(P("ObjectsConcat", "iconic", "结果集合")), A(), A()),

                new OpDoc("clear_obj", "对象",
                    "释放对象内存",
                    "循环中释放中间对象",
                    A(P("Objects", "iconic", "要释放的对象")), A(), A(), A()),

                new OpDoc("region_to_bin", "图像生成",
                    "把区域转成 0/255 二值图",
                    "生成掩膜/叠加显示",
                    A(P("Region", "region", "输入区域")), A(P("Image", "image", "结果二值图")),
                    A(P("GrayValue", "number", "区域灰度(常255)"), P("BackgroundValue", "number", "背景灰度(常0)"),
                      P("Width", "integer", "图宽"), P("Height", "integer", "图高")), A()),

                // ===== Tuple =====
                new OpDoc("tuple_gen_sequence", "Tuple",
                    "生成等差数列",
                    "构造坐标序列/循环索引",
                    A(), A(P("Sequence", "tuple", "结果数组")),
                    A(P("N", "integer", "个数"), P("Begin", "number", "首值"), P("Step", "number", "步长")), A()),

                new OpDoc("tuple_length", "Tuple",
                    "取元组长度",
                    "统计数组元素个数",
                    A(P("Tuple", "tuple", "输入元组")), A(), A(), A(P("Length", "integer", "长度"))),

                new OpDoc("tuple_select_range", "Tuple",
                    "截取元组子段",
                    "取前 N 个结果",
                    A(P("Tuple", "tuple", "输入元组")), A(P("SubTuple", "tuple", "结果")),
                    A(P("Begin", "integer", "起始下标"), P("End", "integer", "结束下标")), A()),

                new OpDoc("tuple_sort", "Tuple",
                    "元组排序",
                    "结果按大小排序",
                    A(P("Tuple", "tuple", "输入元组")), A(P("Sorted", "tuple", "排序结果")),
                    A(P("Mode", "string", "'true'升/'false'降")), A()),

                new OpDoc("tuple_find", "Tuple",
                    "在元组中查找值",
                    "判断测量值是否出现",
                    A(P("Tuple", "tuple", "输入元组")), A(P("Indices", "integer", "命中下标数组")),
                    A(P("Value", "tuple", "要找的值")), A()),

                new OpDoc("tuple_max", "Tuple",
                    "元组最大值",
                    "取最大缺陷面积",
                    A(P("Tuple", "tuple", "输入元组")), A(), A(), A(P("Max", "number", "最大值"))),

                new OpDoc("tuple_min", "Tuple",
                    "元组最小值",
                    "取最小测量值",
                    A(P("Tuple", "tuple", "输入元组")), A(), A(), A(P("Min", "number", "最小值"))),

                new OpDoc("tuple_mean", "Tuple",
                    "元组均值",
                    "多次测量取平均",
                    A(P("Tuple", "tuple", "输入元组")), A(), A(), A(P("Mean", "number", "均值"))),

                new OpDoc("tuple_deviation", "Tuple",
                    "元组标准差",
                    "测量重复性(SD)评估",
                    A(P("Tuple", "tuple", "输入元组")), A(), A(), A(P("Deviation", "number", "标准差"))),

                new OpDoc("gen_tuple_const", "Tuple",
                    "生成定值数组",
                    "初始化判定数组",
                    A(), A(P("Tuple", "tuple", "结果")),
                    A(P("NumElements", "integer", "个数"), P("Const", "tuple", "填充值")), A()),

                // ===== 显示辅助（dev_*，仅 HDevelop 交互用）=====
                new OpDoc("dev_open_window", "显示",
                    "打开一个显示窗口",
                    "HDevelop 调试显示",
                    A(), A(P("WindowHandle", "window", "窗口句柄")),
                    A(P("Row", "integer", "窗口位置行"), P("Column", "integer", "列"),
                      P("Width", "integer", "宽"), P("Height", "integer", "高"),
                      P("BackgroundColor", "string", "'white'/'black'/'auto'")), A()),

                new OpDoc("dev_display", "显示",
                    "在窗口中显示对象（图像/区域/轮廓叠加）",
                    "调试时叠加显示结果",
                    A(P("Object", "iconic", "要显示的对象")), A(), A(), A()),

                new OpDoc("dev_set_color", "显示",
                    "设置后续显示对象的颜色",
                    "不同缺陷用不同颜色区分",
                    A(), A(), A(P("Color", "string", "'green'/'red'等")), A()),

                new OpDoc("dev_set_draw", "显示",
                    "设置区域显示方式（填充/边缘）",
                    "只看轮廓时设为 margin",
                    A(), A(), A(P("Mode", "string", "'margin'/'fill'")), A()),

                new OpDoc("dev_set_part", "显示",
                    "设置窗口显示范围（放大局部）",
                    "细看缺陷局部",
                    A(), A(),
                    A(P("Row1", "number", "上"), P("Column1", "number", "左"),
                      P("Row2", "number", "下"), P("Column2", "number", "右")), A()),

                new OpDoc("dev_close_window", "显示",
                    "关闭当前窗口",
                    "调试收尾",
                    A(), A(), A(), A()),
            };

            var dict = new Dictionary<string, OpDoc>(StringComparer.Ordinal);
            foreach (var d in docs)
                dict[d.Name] = d;
            return dict;
        }
    }
}
