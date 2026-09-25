using System.Collections.Generic;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 经典视觉流程模板库（参考 HDevelop 官方例程改写）。
    /// 编辑器右键菜单「插入示例代码」使用。
    /// 图片/模型只写短名（如 'marks'），插件运行时自动解析到程序目录 ScriptAssets，
    /// 脚本无死路径，换电脑直接跑；装了 Halcon 时官方图像名（如 'fabrik'）也能直接用。
    ///
    /// 本类同时是「插件自带默认脚本」的唯一真相源（DefaultProcedureName / DefaultBody /
    /// CreateDefaultProcedure / CreateDefaultInputVars / CreateDefaultOutputVars）：
    /// 新步骤首次打开配置窗口时自动装一份，工具栏「恢复默认脚本」按钮也调这一份，
    /// 两个入口共用同一定义，行为永远一致。
    /// </summary>
    public sealed class TemplateDef
    {
        public string Category { get; }
        public string Title { get; }
        public string Code { get; }

        public TemplateDef(string category, string title, string code)
        {
            Category = category;
            Title = title;
            Code = code;
        }
    }

    public static class ScriptTemplates
    {
        // ═══════════════════════ 插件自带的默认脚本 ═══════════════════════

        /// <summary>
        /// 默认过程名。不能叫 main —— RunProcedureNameList 明确排除 main，
        /// 叫了会导致「运行过程」下拉里看不到它、执行时也找不到过程。
        /// </summary>
        public const string DefaultProcedureName = "script";

        /// <summary>
        /// 默认脚本正文：1 个图像进 → 1 个图像出 + 1 个 OK/NG 结论出。
        /// 判定阈值放在「示教参数区」，不额外开控制量输入端口 —— 端口越少越好懂。
        /// </summary>
        public const string DefaultBody = @"* ──────────────────────────────────────
* 默认脚本：1 个图像进 → 1 个图像出 + 1 个结论出
*   Image       输入图像（本步骤的输入端口，需要连上游）
*   ResultImage 处理后的图像（可以直接连给下游）
*   Result      判定结论 'OK' / 'NG'（可以接条件分支）
* 下面这段是「数亮斑个数」的示范，把它换成你自己的算子即可
* ──────────────────────────────────────
* ── 示教参数区：换产品只改这一段，下面代码不用动 ──
* 阈值下限：亮于它的像素算目标（0~255，先看灰度直方图再定，别拍脑袋）
ThreshMin := 128
* 目标最小面积（像素）：比它小的当噪点丢掉
MinArea := 50

* 第1步 彩色转灰度：机器视觉大多在灰度图上做，数据量小一半
* 必须先判断通道数——对单通道图调用 rgb1_to_gray 会直接报错
count_channels (Image, Channels)
if (Channels >= 3)
    rgb1_to_gray (Image, GrayImage)
else
    copy_image (Image, GrayImage)
endif

* 第2步 阈值分割：亮于 ThreshMin 的像素连成候选区域
threshold (GrayImage, Region, ThreshMin, 255)

* 第3步 拆分与筛选：粘连的拆成一个个，再按面积甩掉噪点
connection (Region, ConnectedRegions)
select_shape (ConnectedRegions, Targets, 'area', 'and', MinArea, 1e7)
count_obj (Targets, Number)

* 第4步 判定：数出个数就能判 OK/NG
* 换成测量 / 匹配 / OCR 也是同一套路：先算出来，再和规格比，最后给结论
if (Number > 0)
    Result := 'NG'
else
    Result := 'OK'
endif

* 第5步 画在画布上：脚本结束时会自动回读成效果图送到界面窗口1
dev_display (GrayImage)
dev_set_draw ('margin')
dev_set_line_width (2)
dev_set_color ('red')
dev_display (Targets)
dev_disp_text ('Result: ' + Result + '  数量: ' + Number$'.0f', 'window', 12, 12, 'black', ['box','box_color'], ['true','yellow'])

* 第6步 输出图像：ResultImage 就是本步骤的图像输出端口
ResultImage := GrayImage";

        /// <summary>默认脚本的输入变量表（与 CreateDefaultProcedure 的接口列表严格对应）</summary>
        public static ScriptVarDef[] CreateDefaultInputVars() => new[]
        {
            new ScriptVarDef { Name = "Image", Type = ScriptVarType.HImage },
        };

        /// <summary>默认脚本的输出变量表：一张图 + 一个结论</summary>
        public static ScriptVarDef[] CreateDefaultOutputVars() => new[]
        {
            new ScriptVarDef { Name = "ResultImage", Type = ScriptVarType.HImage },
            // 必须显式写 String：GuessType 对不以 i/s 开头的名字一律猜 Double，
            // 而 Result 这个输出要的就是 'OK'/'NG' 文本
            new ScriptVarDef { Name = "Result", Type = ScriptVarType.String },
        };

        /// <summary>构造默认过程：接口列表必须与上面两张变量表一致，否则编译前反向同步会打架</summary>
        public static EProcedure CreateDefaultProcedure() => new EProcedure
        {
            Name = DefaultProcedureName,
            IconicInputList = new List<string> { "Image" },
            IconicOutputList = new List<string> { "ResultImage" },
            CtrlOutputList = new List<string> { "Result" },
            Body = DefaultBody,
        };

        // ═══════════════════════ 右键菜单「插入示例代码」的模板 ═══════════════════════

        public static readonly TemplateDef[] All =
        {
            // ═══════════════════════ A 图像预处理 ═══════════════════════

            new TemplateDef("A 图像预处理", "对比度增强（雾天/发灰图）",
@"* ──────────────────────────────────────
* 例｜对比度增强：把灰蒙蒙的图变通透
* 比方：清晨起雾拍照发白→手机「增强对比度」一键让画面通透，这就是同一件事
* 三步：读图 → 灰度拉伸铺满0~255 → 中值滤波收尾
* 试跑：插进去直接点「执行」，右侧窗口看效果
* 坑：拉伸会把噪声也放大，所以最后配一步去噪
* ──────────────────────────────────────
* 读示例图（图名=程序自带图片，无需路径）
read_image (Image, 'hull')
* 彩色转灰度：机器视觉大多在灰度图上做，数据量小一半
rgb1_to_gray (Image, GrayImage)
* 自动拉伸：最暗的拉到0，最亮的拉到255，中间按比例放大（只有入图和出图两个参数）
scale_image_max (GrayImage, ScaleImageMax)
* 中值滤波：窗口3去噪（数字越大越糊，3~5常用）
median_image (ScaleImageMax, Result, 'circle', 3, 'mirrored')
dev_display (Result)"),

            new TemplateDef("A 图像预处理", "去噪三部曲（高斯→中值→双边）",
@"* ──────────────────────────────────────
* 例｜组合去噪：三种滤镜按「先柔化→点杀→保边」顺序上
* 比方：高斯=磨砂玻璃（整体糊但均匀）；中值=挑出坏果扔掉（专治孤立噪点）；
*       双边=只糊平坦区保留轮廓线（聪明但慢）
* 三步：高斯抹细噪 → 中值杀椒盐黑白点 → 双边收尾保边缘
* 坑：测量类项目慎用大窗口高斯——边缘糊了测量值就漂了
* ──────────────────────────────────────
read_image (Image, 'mreut4_3')
rgb1_to_gray (Image, GrayImage)
* 第1步 高斯：Size=5 是「掩膜窗口边长」（整数、奇数），不是标准差；噪声重可到7、9（越大越糊）
gauss_image (GrayImage, Gauss, 5)
* 第2步 中值：窗口5，孤立黑白点一次清光
median_image (Gauss, Median, 'circle', 5, 'mirrored')
* 第3步 双边：Sigma=5容差Theta=20，去噪同时守住边缘（慢，可省略）
* 参数顺序：原图 → 联合引导图(没有就再填一次原图) → 出图 → Sigma → Theta → 两个_gen参数
bilateral_filter (Median, Median, Bilateral, 5, 20, [], [])
dev_display (Bilateral)"),

            new TemplateDef("A 图像预处理", "光照不均校正（中间亮四周暗）",
@"* ──────────────────────────────────────
* 例｜光照均匀化：打光不匀导致「一个阈值切不开全图」的克星
* 比方：照片左上角被台灯照亮了一块——先拍一张「只有灯光没有工件」的底图，
*       用它把光照「垫」回去，全图亮度就平了
* 三步：大窗口滤波估出「纯光照底图」→ 原图减底图 → 再加128回到中性灰
* 坑：估背景的窗口(51)必须比工件特征大，否则细节也被当成背景减没了
* ──────────────────────────────────────
read_image (Image, 'meningg5')
rgb1_to_gray (Image, GrayImage)
* 超大窗口中值：只看得见光照渐变，看不见小细节 → 得到背景亮度图
median_image (GrayImage, Background, 'circle', 51, 'mirrored')
* 原图-背景+128：亮度梯度拉平，128=不亮不暗的中性灰
sub_image (GrayImage, Background, Corrected, 1, 128)
* 校正后一个普通阈值就能全局分割
threshold (Corrected, Region, 140, 255)
dev_display (Corrected)
dev_display (Region)"),

            new TemplateDef("A 图像预处理", "旋转校正（把歪的摆正）",
@"* ──────────────────────────────────────
* 例｜自动转正：文字/工件放歪了，先转正再处理（OCR和比对的前置神技）
* 比方：拍照时文档歪了，扫描APP自动「摆正」——就是算出倾角再反向旋转
* 三步：分割出内容 → 算主轴倾角Phi → 构造反向旋转矩阵转正整图
* 坑：Halcon角度全是弧度！rad(30)=30度；orientation_region对对称图形会「猜」方向
* ──────────────────────────────────────
read_image (Image, 'dot_print_slanted')
rgb1_to_gray (Image, GrayImage)
* 文字比背景暗 → 取暗区
threshold (GrayImage, TextRegion, 0, 120)
* 算文字整体倾角Phi（弧度）
orientation_region (TextRegion, Phi)
* 以图像中心为轴，构造「转回0度」的旋转矩阵
get_image_size (GrayImage, Width, Height)
vector_angle_to_rigid (Height / 2, Width / 2, Phi, Height / 2, Width / 2, 0, HomMat2D)
* 整图执行仿射（插值模式必须写全名：'nearest_neighbor'快、'bilinear'文字边缘不锯齿）
* 参数顺序：图 → 出图 → 矩阵 → Interpolate → AdaptImageSize('true'=自动扩大画布防裁掉)
affine_trans_image (GrayImage, ImageRectified, HomMat2D, 'bilinear', 'false')
dev_display (ImageRectified)"),

            new TemplateDef("A 图像预处理", "彩色检测（按颜色找目标）",
@"* ──────────────────────────────────────
* 例｜按颜色找东西：只检红色缺陷/红色零件，灰度图做不到
* 比方：彩色照片拆成「红牌绿牌蓝牌」三张黑白照；红色物体在红牌上最亮、
*       绿牌上最暗——两牌一减，红色差异被放大，普通阈值随便切
* 三步：拆R/G/B → 红减绿得差值图 → 阈值+筛选
* 坑：光照色温变化大时先做白平衡，或改用HSV色相判断更稳
* ──────────────────────────────────────
read_image (Image, 'pcb_color')
* 彩色图拆成R/G/B三张单通道灰度图
decompose3 (Image, R, G, B)
* R-G差值：红色区域=大正数，一眼可分
sub_image (R, G, RedDiff, 1, 0)
* 差值>60算「明显偏红」：误报多调大，漏检调小
threshold (RedDiff, RedRegion, 60, 255)
connection (RedRegion, ConnectedRegions)
select_shape (ConnectedRegions, RedParts, 'area', 'and', 100, 1e7)
dev_display (Image)
dev_display (RedParts)"),

            // ═══════════════════════ B Blob分析 ═══════════════════════

            new TemplateDef("B Blob分析", "划痕/暗斑缺陷检测（入门第一课）",
@"* ──────────────────────────────────────
* 例｜表面缺陷检测：金属/塑料表面的暗斑划痕，找出+数清+定位
* 比方：白米饭里挑黑点——①把黑的挑出来(阈值) ②粘连的分开数(连通域)
*       ③太小的是芝麻不算(面积筛选)
* 三步：threshold分割 → connection拆分 → select_shape筛选 → count计数
* 坑：阈值范围别拍脑袋——先看灰度直方图；面积下限是「像素」不是毫米
* ──────────────────────────────────────
read_image (Image, 'surface_scratch')
rgb1_to_gray (Image, GrayImage)
* 第1步 分割：划痕比亮表面暗 → 取0~100的暗区（按实际图调）
threshold (GrayImage, DarkRegion, 0, 100)
* 第2步 拆分：连成一片的暗区拆成一个个独立候选缺陷
connection (DarkRegion, ConnectedRegions)
* 第3步 筛选：面积≥30像素才算缺陷，以下当噪点放过
* 长条划痕改用细长比筛选：select_shape (..., 'elongation', 'and', 4, 100)
select_shape (ConnectedRegions, Defects, 'area', 'and', 30, 1e7)
* 第4步 结果：缺陷个数+每个的位置坐标
count_obj (Defects, Number)
area_center (Defects, Area, Row, Column)
dev_display (Image)
dev_display (Defects)"),

            new TemplateDef("B Blob分析", "零件计数（粘连分离）",
@"* ──────────────────────────────────────
* 例｜数零件：料盘里零件互相贴着也能数准
* 比方：两颗糖粘一起——「开运算」= 先泡软表面(腐蚀)冲掉粘连细桥，
*       再复原(膨胀)，糖各是糖，桥已经断了
* 三步：阈值分割 → 开运算磨断粘连桥 → 连通域计数
* 坑：开运算半径太大零件会被磨没；先用小图试半径再上产线
* ──────────────────────────────────────
read_image (Image, 'pellets')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, Region, 100, 255)
* 开运算半径3.0：刚好磨断粘连桥不伤本体（粘得越粗值越大）
opening_circle (Region, RegionOpened, 3.0)
connection (RegionOpened, ConnectedRegions)
* 面积下限=最小零件的实际面积，滤掉磨下来的碎渣
select_shape (ConnectedRegions, Parts, 'area', 'and', 100, 1e7)
count_obj (Parts, Number)
dev_display (Image)
dev_display (Parts)"),

            new TemplateDef("B Blob分析", "孔洞检测（缺孔/堵孔）",
@"* ──────────────────────────────────────
* 例｜查孔：冲孔板/电路板上的孔堵了缺了变形了
* 比方：一张纸剪了几个洞——把纸的轮廓「补全」成实心纸，
*       实心纸减去原纸，剪下来的洞全掉出来了（差集思想）
* 三步：分割板材 → fill_up填成实心 → 差集得到所有孔 → 按面积挑异常
* 坑：fill_up只填「完全封闭」的孔；边缘破口的孔要用外接矩形对比法
* ──────────────────────────────────────
read_image (Image, 'punched_holes')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, BoardRegion, 128, 255)
* 填平所有洞 → 「没有孔的整板」
fill_up (BoardRegion, RegionFillUp)
* 实心板 - 原板 = 板上所有孔
difference (RegionFillUp, BoardRegion, Holes)
connection (Holes, ConnectedHoles)
* 面积异常小=堵孔（正常孔径按实际改）
select_shape (ConnectedHoles, BadHoles, 'area', 'and', 0, 60)
count_obj (BadHoles, Number)
dev_display (Image)
dev_display (BadHoles)"),

            new TemplateDef("B Blob分析", "粒子统计与位置排序",
@"* ──────────────────────────────────────
* 例｜数据整理：一堆粒子测完大小位置，还要按左右顺序排好
* 比方：全班同学量身高——数据是一维数组(tuple)；
*       tuple_sort_index=「按身高排队后的学号表」，按表读数据就是有序的
* 三步：Blob测量 → tuple_sort_index生成排序索引 → 统计均值/最大值
* 坑：Halcon数组下标从0开始；Row=Y方向(向下)，Column=X方向(向右)
* ──────────────────────────────────────
read_image (Image, 'particle')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, Region, 100, 255)
connection (Region, ConnectedRegions)
area_center (ConnectedRegions, Area, Row, Column)
* 按X坐标升序的索引表——Indices[0]=最左边粒子的编号
tuple_sort_index (Column, Indices)
LeftmostRow := Row[Indices[0]]
LeftmostCol := Column[Indices[0]]
* 统计：最大面积远超均值=有粘连团聚
tuple_mean (Area, MeanArea)
tuple_max (Area, MaxArea)
tuple_length (Area, NumParticles)"),

            new TemplateDef("B Blob分析", "区域轮廓提取与平滑",
@"* ──────────────────────────────────────
* 例｜锯齿变丝滑：阈值区域边缘全是阶梯，转成光滑XLD轮廓
* 比方：马赛克拼的圆放大看全是方块台阶——「XLD」是用数学曲线描的丝滑版
* 三步：boundary取边界 → 转XLD轮廓 → smooth_contours_xld抹平
* 坑：平滑窗口越大越圆滑也越「失真」，精密测量前慎用大窗口
* ──────────────────────────────────────
read_image (Image, 'can')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, CanRegion, 100, 255)
opening_circle (CanRegion, CanClean, 2.0)
* 区域→外边界→XLD轮廓线
boundary (CanClean, RegionBorder, 'outer')
gen_contour_region_xld (RegionBorder, Contours, 'border')
* 平滑窗口11（必须奇数），毛刺重加大
smooth_contours_xld (Contours, SmoothedContours, 11)
dev_display (Image)
dev_display (SmoothedContours)"),

            // ═══════════════════════ C 定位 ═══════════════════════

            new TemplateDef("C 定位", "十字Mark定位",
@"* ──────────────────────────────────────
* 例｜找十字Mark：工件坐标系原点定下来，后续检测全跟着它走
* 比方：拼图先找四个角的「十字标记」——形状特殊的东西天生适合当基准
* 三步：阈值分割 → 圆度+面积双条件筛出十字 → 取中心坐标
* 坑：视野里有多个像十字的干扰物时，再加矩形度/边长条件收紧筛选
* ──────────────────────────────────────
read_image (Image, 'marks')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, Region, 128, 255)
connection (Region, ConnectedRegions)
* 圆度0~1越接近1越紧凑，十字约0.5~0.8（按实际放宽）
select_shape (ConnectedRegions, Crosses, ['circularity','area'], 'and', [0.5,300], [1.0,1e7])
count_obj (Crosses, Number)
if (Number >= 1)
    select_obj (Crosses, FirstCross, 1)
    area_center (FirstCross, Area, Row, Column)
endif
dev_display (Image)
dev_display (Crosses)"),

            new TemplateDef("C 定位", "圆心亚像素定位",
@"* ──────────────────────────────────────
* 例｜精密找圆心：比阈值法准10倍的圆定位
* 比方：阈值法=把圆涂色后数格子（边缘全是锯齿）；
*       亚像素=沿边缘「描线」，能描出半个像素的位置差别
* 三步：亚像素边缘提取 → 留长轮廓 → 数学拟合圆
* 坑：edges_sub_pix的双阈值(20/40)控制灵敏度——噪点多就调高
* ──────────────────────────────────────
read_image (Image, 'double_circle')
* 亚像素边缘：canny算子,sigma1平滑,20/40=弱/强边缘阈值
edges_sub_pix (Image, Edges, 'canny', 1, 20, 40)
* 只留≥100像素的长轮廓（短的=噪声）
select_contours_xld (Edges, SelectedContours, 'contour_length', 100, 10000, -0.5, 0.5)
* 拟合圆：输出圆心Row/Column+半径Radius
* 参数顺序：轮廓 → 算法 → MaxNumPoints → MaxClosureDist → ClippingEndPoints → Iterations → ClippingFactor → 6个输出
fit_circle_contour_xld (SelectedContours, 'algebraic', -1, 0, 0, 5, 2, Row, Column, Radius, StartPhi, EndPhi, PointOrder)
dev_display (Image)
* 把拟合出来的圆画回去，一眼看合得上合不上
gen_circle_contour_xld (FitCircle, Row, Column, Radius, 0, 6.283, 'positive', 1)
dev_set_color ('green')
dev_set_line_width (2)
dev_display (FitCircle)"),

            new TemplateDef("C 定位", "边缘角度定位（测倾角）",
@"* ──────────────────────────────────────
* 例｜一条边定角度：来料歪斜，拟合主边缘算出旋转角
* 比方：桌腿歪没歪，拿直尺贴着腿比一下就知道了——直尺=fit_line拟合的直线
* 三步：分割取轮廓 → 拟合直线 → 方向向量转角度atan2
* 坑：atan2(行,列)不是atan2(y,x)——Halcon坐标系Row是Y方向但顺序在前！
* ──────────────────────────────────────
read_image (Image, 'razors1')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, Region, 128, 255)
boundary (Region, RegionBorder, 'outer')
gen_contour_region_xld (RegionBorder, Contours, 'border')
select_contours_xld (Contours, LongContours, 'contour_length', 100, 10000, -0.5, 0.5)
* 拟合直线：两端点+方向向量(Nr,Nc)+偏差Dist(越小拟合越好)
fit_line_contour_xld (LongContours, 'regression', -1, 0, 5, 2, Row1, Column1, Row2, Column2, Nr, Nc, Dist)
* 倾角(弧度)：机器人抓取时直接加到姿态角上
Angle := atan2(Nr[0], Nc[0])
dev_display (Image)
dev_display (LongContours)"),

            new TemplateDef("C 定位", "双Mark点位姿补偿",
@"* ──────────────────────────────────────
* 例｜两个Mark定住整张图：大面板对位的标准打法
* 比方：挂画——两个钉子定了，画就歪不了；两点=平移+旋转全部确定
* 三步：找两个Mark中心 → 连线方向=工件X轴 → 构造设计到实际的变换矩阵
* 坑：Mark要选「稳定+远离加工区」的；两个Mark距离越远角度误差越小
* ──────────────────────────────────────
read_image (Image, 'marks')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, Region, 128, 255)
connection (Region, ConnectedRegions)
* 坑：Features 给2个时，Min/Max 也必须各给2个值（一一对应），不能只写一个标量
select_shape (ConnectedRegions, Marks, ['circularity','area'], 'and', [0.7,200], [1.0,1e7])
count_obj (Marks, Number)
* ── 示教参数区：设计图纸上Mark1的理论位置（量产由示教/配方写入，这里给示例值保证可跑）──
RefRow := 120
RefCol := 100
if (Number >= 2)
    select_obj (Marks, Mark1, 1)
    select_obj (Marks, Mark2, 2)
    area_center (Mark1, A1, Row1, Col1)
    area_center (Mark2, A2, Row2, Col2)
    * Mark1→Mark2连线方向角=工件X轴
    Angle := atan2(Row2 - Row1, Col2 - Col1)
    * (RefRow,RefCol)=设计时Mark1位置；之后任何设计坐标一乘就准
    vector_angle_to_rigid (RefRow, RefCol, 0, Row1, Col1, Angle, HomMat2D)
    affine_trans_pixel (HomMat2D, 100, 200, ActRow, ActCol)
endif
dev_display (Image)
dev_display (Marks)"),

            // ═══════════════════════ D 模板匹配 ═══════════════════════

            new TemplateDef("D 模板匹配", "形状模板匹配（工业定位之王）",
@"* ──────────────────────────────────────
* 例｜形状匹配：产品随便转、光照随便变，都能找到它+给出角度
* 比方：拼图找那块「边缘形状」对的碎片——不看颜色只看轮廓形状，
*       所以光照变化不怕、明暗反转也不怕
* 三步：建模型(提边缘特征) → 图上转着圈找最像的 → 输出位置+角度+分数
* 坑：建模图片要「裁剪到只剩产品」(crop_domain)——背景边缘会当特征学进去
* ──────────────────────────────────────
read_image (Image, 'fabrik')
rgb1_to_gray (Image, GrayImage)
* 坑①：绝不要拿整图建模！fabrik 512x512 整图建模后 find 返回 0 个结果——
*       'auto' 优化在大图上把特征点剪得只剩几个，搜索直接扑空。
*       正确做法：只裁「产品本体」一小块建模（示教时用鼠标框，这里写死中心201x201）
crop_rectangle1 (GrayImage, Template, 156, 156, 356, 356)
* 建模：金字塔4层,角度0~360°步长3°,'use_polarity'明暗可反转,对比度30
* 坑②：参数顺序 NumLevels,AngleStart,AngleExtent,AngleStep —— Extent(跨度)在前、Step(步长)在后
create_shape_model (Template, 4, 0, rad(360), rad(3), 'auto', 'use_polarity', 30, 10, ModelID)
* 匹配：分数≥0.5才算找到(误报多调高到0.7),只要1个最优
* 参数顺序：图 → 模型 → 起始角 → 角度范围 → MinScore → NumMatches → MaxOverlap → SubPixel → NumLevels → Greediness → 4个输出
find_shape_model (GrayImage, ModelID, 0, rad(360), 0.5, 1, 0.5, 'none', 0, 0.5, Row, Column, Angle, Score)
get_shape_model_contours (ModelContours, ModelID, 1)
dev_display (Image)
* 坑③：没匹到的时候 Row/Column 是「空数组」，直接喂给 vector_angle_to_rigid
*       会报 Wrong number of values of control parameter 4 —— 必须先判 |Row|
if (|Row| == 0)
    Result := 'NOT_FOUND'
else
    * 把模型轮廓画到匹配位置——肉眼一秒验证匹配对不对
    vector_angle_to_rigid (0, 0, 0, Row, Column, Angle, HomMat2D)
    affine_trans_contour_xld (ModelContours, TransContours, HomMat2D)
    dev_display (TransContours)
    Result := 'OK'
endif
* 进阶：模型可 write_shape_model 存盘，量产程序只 read 不 create（省建模时间）"),

            new TemplateDef("D 模板匹配", "灰度NCC匹配（纹理型产品）",
@"* ──────────────────────────────────────
* 例｜NCC匹配：轮廓不清但「花纹」丰富的产品用它
* 比方：形状匹配认「脸型轮廓」，NCC认「整张脸的照片像素」——
*       铸造面/织物/木纹这类轮廓烂但花纹稳的，NCC更牢靠
* 三步：建NCC模型 → 匹配 → 输出位姿
* 坑：NCC对光照整体变化敏感于形状匹配——光源必须稳定
* ──────────────────────────────────────
read_image (Image, 'engraved')
rgb1_to_gray (Image, GrayImage)
* 建NCC模型：金字塔4层,角度0~360°步长3°
* 参数顺序：图 → NumLevels → AngleStart → AngleExtent → AngleStep → Metric → ModelID（NCC没有'false'/'auto'这两项）
create_ncc_model (Image, 4, 0, rad(360), rad(3), 'use_polarity', ModelID)
* 匹配：MinScore 0.5起步，误报往0.7调
find_ncc_model (Image, ModelID, 0, rad(360), 0.5, 1, 0.75, 'true', 0, Row, Column, Angle, Score)
dev_display (Image)
if (|Row| > 0)
    * 十字标出匹配中心：臂长30像素，0.7854弧度=45°斜十字（比正十字醒目）
    gen_cross_contour_xld (Cross, Row, Column, 30, 0.7854)
    dev_set_color ('green')
    dev_display (Cross)
endif"),

            new TemplateDef("D 模板匹配", "ROI随匹配位姿联动",
@"* ──────────────────────────────────────
* 例｜检测区跟着产品走：设计时画的框，产品歪了框自动跟过去
* 比方：贴纸模板对准后整张模板跟着转——设计ROI=模板上的孔位，
*       匹配到产品在哪歪着，ROI就变换到哪
* 三步：匹配得位姿 → 变换设计ROI → reduce_domain只检ROI内部
* 坑：RefRow/RefCol必须填「建模时产品中心」，填错ROI会整体偏移
* ──────────────────────────────────────
read_image (Image, 'clip')
rgb1_to_gray (Image, GrayImage)
* 第1步：匹配产品本体
* ModelID 正常应示教一次后 write_shape_model 存盘、量产只 read_shape_model；
* 这里为保证示例可直接运行，现场建模一次
* ⚠ 只裁产品中心建模：整图建模在大图上会被'auto'剪到没特征，find直接返回0个结果
crop_rectangle1 (GrayImage, Template, 351, 355, 471, 475)
create_shape_model (Template, 4, 0, rad(360), rad(3), 'auto', 'use_polarity', 30, 10, ModelID)
find_shape_model (GrayImage, ModelID, -rad(10), rad(20), 0.5, 1, 0.5, 'none', 0, 0.5, Row, Column, Angle, Score)
* 第2步：设计阶段画好的ROI（行300~380，列300~460）
gen_rectangle1 (ROIDesign, 300, 300, 380, 460)
* ── 示教参数区：建模那一刻那块产品中心（填错则ROI整体偏移）──
RefRow := 411
RefCol := 415
dev_display (Image)
* 第3步：设计ROI→实际ROI（跟着产品平移旋转）
if (|Row| == 0)
    * 没匹到就退回设计位置显示；量产时要报警停机，绝不能拿空数组算位姿
    dev_display (ROIDesign)
else
    vector_angle_to_rigid (RefRow, RefCol, 0, Row, Column, Angle, HomMat2D)
    affine_trans_region (ROIDesign, ROITrans, HomMat2D, 'false')
    * 第4步：后续检测只在ROI内——又快又稳，视野外干扰全隔绝
    reduce_domain (Image, ROITrans, ImageReduced)
    dev_display (ROITrans)
endif"),

            new TemplateDef("D 模板匹配", "多目标匹配（一帧找N个同款）",
@"* ──────────────────────────────────────
* 例｜一次找一堆：托盘上N个相同零件，一次匹配全部定位
* 比方：连连看游戏开局——同一张图找N个一样的图案，一次全报位置
* 三步：匹配(NumMatches=100最多找100个) → 结果数组 → 遍历输出坐标
* 坑：MaxOverlap重叠容忍度——零件挨得近要调小，否则一个报俩
* ──────────────────────────────────────
read_image (Image, 'pellets')
rgb1_to_gray (Image, GrayImage)
* ModelID 正常由示教对「单个颗粒」建一次；这里为保证示例可跑，先裁出一颗现场建模
* 坑：建模图一定要 crop_rectangle1 / crop_domain 裁到只剩一个产品，
*     整图建模会把背景边缘当特征学进去，反而匹配不上
crop_rectangle1 (Image, Template, 100, 100, 200, 200)
create_shape_model (Template, 4, 0, rad(360), rad(3), 'auto', 'use_polarity', 30, 10, ModelID)
find_shape_model (Image, ModelID, 0, rad(360), 0.4, 100, 0.5, 'none', 0, 0.75, Row, Column, Angle, Score)
* 实际找到的个数=结果数组长度
NumFound := |Score|
* 遍历拼成CSV文本发给上位机
Result := ''
for i := 0 to NumFound - 1 by 1
    tuple_number (Row[i], RowStr)
    tuple_number (Column[i], ColStr)
    Result := Result + RowStr + ',' + ColStr + ';'
endfor
dev_display (Image)"),

            // ═══════════════════════ E 精密测量 ═══════════════════════

            new TemplateDef("E 精密测量", "卡尺测边距（宽/厚/缝）",
@"* ──────────────────────────────────────
* 例｜卡尺测距：像游标卡尺一样精确量两条边之间的距离（亚像素级）
* 比方：质检员拿卡尺夹住引脚量宽度——「卡尺工具」就是在图上放一条带子，
*       沿带子扫灰度跳变，跳变点=边缘，两边缘间距=测量值
* 三步：放卡尺(位置角度) → 扫描成对边缘 → 取距离乘标定系数
* 坑：卡尺方向要「横跨」被测边（和边垂直）；边缘对比度太低调小Threshold30
* ──────────────────────────────────────
read_image (Image, 'ic0')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* ── 示教参数区：卡尺的位置/角度/尺寸 + 像素当量 ──
* 量产时这几项全部由「示教界面拖动卡尺」得到并存进配方，此处给一组能跑通的示例值
* 注意：CAPhi=0 时卡尺长边沿水平方向，CAPhi=rad(90) 才是竖直方向
CARow := 0.5 * Height
CAColumn := 0.5 * Width
CAPhi := 0.0
CAL1 := 0.2 * Width
CAL2 := 20
PixelSizeMm := 0.01
* 第1步 放卡尺：中心(CARow,CAColumn) 方向CAPhi 半长CAL1 半宽CAL2
gen_measure_rectangle2 (CARow, CAColumn, CAPhi, CAL1, CAL2, Width, Height, 'bilinear', MeasureHandle)
* 第2步 扫描：Sigma1平滑 Threshold30灵敏度 'all'任意方向跳变全收
measure_pairs (Image, MeasureHandle, 1, 30, 'all', 'all', RowEdgeFirst, ColumnEdgeFirst, AmplitudeFirst, RowEdgeSecond, ColumnEdgeSecond, AmplitudeSecond, IntraDistance, InterDistance)
* 第3步 第1对边缘的距离=宽度(像素)→乘系数变毫米
Distance_mm := IntraDistance[0] * PixelSizeMm
dev_display (Image)"),

            new TemplateDef("E 精密测量", "孔径测量（圆周卡尺）",
@"* ──────────────────────────────────────
* 例｜量孔径：绕孔扫一圈边缘点，拟合出精确直径（机加工首件检测常用）
* 比方：量井口直径——拿很多把小尺子沿圆周挨个量到井壁，
*       所有点连起来套个最合适的圆，直径就出来了
* 三步：粗定位圆 → 沿圆周布几十把卡尺 → 扫出的点拟合精确圆
* 坑：粗半径误差±20%以内没关系，搜索带宽给足会自己找到真边缘
* ──────────────────────────────────────
read_image (Image, 'double_circle')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* 第1步 粗定位：分割出圆→最小外接圆，拿到粗圆心粗半径（实际项目由示教或匹配给出）
threshold (GrayImage, Rough, 128, 255)
smallest_circle (Rough, CoarseRow, CoarseColumn, CoarseRadius)
* 第2步 沿整圆布卡尺：AnnulusRadius=15像素是搜索带宽，用来容忍粗定位的偏差
* 参数顺序：粗圆心行 → 粗圆心列 → 粗半径 → 起始角 → 角度范围 → 搜索带宽 → 图宽 → 图高 → 插值 → 句柄
gen_measure_arc (CoarseRow, CoarseColumn, CoarseRadius, 0, rad(360), 15, Width, Height, 'bilinear', MeasureHandle)
measure_pos (Image, MeasureHandle, 1, 30, 'all', 'all', RowEdge, ColumnEdge, Amplitude, Distance)
* 第3步 扫到的边缘点串成轮廓→拟合精确圆（最后那个2是ClippingFactor，必给）
gen_contour_polygon_xld (Contour, RowEdge, ColumnEdge)
fit_circle_contour_xld (Contour, 'algebraic', -1, 0, 0, 5, 2, Row, Column, Radius, StartPhi, EndPhi, PointOrder)
* 像素当量(毫米/像素)：量产程序里由标定得到，这里给个示例值
PixelSizeMm := 0.01
Diameter_mm := Radius * 2 * PixelSizeMm
* 卡尺句柄用完必须释放，否则连续跑会累积泄漏
* 注意：算子名是 close_measure（关闭测量对象），不存在 clear_measure
close_measure (MeasureHandle)
dev_display (Image)
dev_display (Contour)"),

            new TemplateDef("E 精密测量", "线宽测量（胶线/焊丝/印刷线）",
@"* ──────────────────────────────────────
* 例｜量线宽：胶线/焊丝/导线粗细一次测完
* 比方：一根香肠上撒盐——离皮越远的盐粒「离边缘越远」；
*       线中心处的「离边缘距离」=半根线的宽度（距离变换原理）
* 三步：分割出线状物 → distance_transform距离变换 → 取最大值=最粗处
* 坑：距离变换对断裂的线会低估；先做小膨胀把断口接上
* ──────────────────────────────────────
read_image (Image, 'fuse')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* 金属丝比背景暗 → 取暗区
threshold (GrayImage, WireRegion, 0, 100)
connection (WireRegion, ConnectedRegions)
* 最大的一段=被测的丝
select_shape_std (ConnectedRegions, MainWire, 'max_area', 70)
* 距离变换：每个丝上像素记录「离最近边缘多远」，中心处=半宽
* 参数顺序：区域 → 出图 → 距离类型 → Foreground('true'=算区域内的点) → 图宽 → 图高（宽和后两项必填，没有'max'这个值）
distance_transform (MainWire, DistImage, 'euclidean', 'true', Width, Height)
* 丝上距离值的最大/最小=最粗处/最细处半宽
intensity (MainWire, DistImage, MinDist, MaxDist)
* 像素当量(毫米/像素)：由标定得到，这里给个示例值
PixelSizeMm := 0.01
WidthMax_mm := MaxDist * 2 * PixelSizeMm
dev_display (Image)
dev_display (MainWire)"),

            new TemplateDef("E 精密测量", "像素标定（像素→毫米）",
@"* ──────────────────────────────────────
* 例｜单位换算：把「512像素」变成「10.24毫米」——定量测量的前置课
* 比方：地图比例尺——先拿一把已知长度的尺拍张照，
*       数出它占多少像素，「像素/毫米」系数就出来了
* 三步：拍标准尺 → 量它的像素长度 → 系数=像素长度/实际长度
* 坑：相机/镜头/工作距离动过必须重新标定；高精度场合用标定板+标定助手
* ──────────────────────────────────────
* 已知：量块实际10mm，图像上量出512像素
Known_mm := 10.0
Known_pixel := 512.0
PixelPerMm := Known_pixel / Known_mm
* 之后任何测量值：毫米 = 像素 / 系数
AnyDistance_pixel := 300.0
Measured_mm := AnyDistance_pixel / PixelPerMm
* 完整标定（矫正镜头畸变/倾斜视角）：用HDevelop标定助手配calib_data_*算子族
* 参考图片 'caltab'（Halcon标准标定板）"),

            new TemplateDef("E 精密测量", "点到线距离测量",
@"* ──────────────────────────────────────
* 例｜点线偏差：孔中心偏离理论边多少、边缘直线度等几何量
* 比方：测墙歪不歪——拉条基准线（拟合直线），量钉子（点）到线的垂距
* 三步：图上拟合出基准直线 → 拿到目标点 → 叉积公式算垂距
* 坑：公式里的Row/Column顺序别写反；distance_pp是「两点距离」不是点线
* ──────────────────────────────────────
read_image (Image, 'numbers_scale')
* 第1步 拟合基准线：亚像素边缘 → 只留≥60像素的长轮廓 → 拟合直线
* fit_line参数顺序：轮廓 → 算法 → MaxNumPoints → ClippingEndPoints → Iterations → ClippingFactor → 7个输出
edges_sub_pix (Image, Edges, 'canny', 1, 20, 40)
select_contours_xld (Edges, LongEdges, 'contour_length', 60, 100000, -0.5, 0.5)
fit_line_contour_xld (LongEdges, 'regression', -1, 0, 5, 2, LineRows, LineCols, LineEndRows, LineEndCols, Nr, Nc, Dist)
* 取第一条线做演示（实际项目里按需要 select_obj 挑）
LRow1 := LineRows[0]
LCol1 := LineCols[0]
LRow2 := LineEndRows[0]
LCol2 := LineEndCols[0]
* 第2步 目标点：实际项目里来自 area_center，这里取图像中部一个点
PRow := 120
PCol := 200
* 第3步 垂距=向量叉积/线长（一步到位，不用求垂足）
dRow := LRow2 - LRow1
dCol := LCol2 - LCol1
LineLen := sqrt(dRow * dRow + dCol * dCol)
DistPointLine := abs((PRow - LRow1) * dCol - (PCol - LCol1) * dRow) / LineLen
* 两点距离直接用 distance_pp：
distance_pp (PRow, PCol, LRow1, LCol1, DistToStart)
* 像素当量(毫米/像素)：由标定得到，这里给个示例值
PixelSizeMm := 0.01
Deviation_mm := DistPointLine * PixelSizeMm
* 画出来：基准线 + 目标点十字
gen_contour_polygon_xld (RefLine, [LRow1,LRow2], [LCol1,LCol2])
gen_cross_contour_xld (Cross, PRow, PCol, 20, 0.7854)
dev_display (Image)
dev_set_color ('yellow')
dev_display (RefLine)
dev_set_color ('green')
dev_display (Cross)"),

            // ═══════════════════════ F 识别 ═══════════════════════

            new TemplateDef("F 识别", "印刷字符OCR（读键盘数字）",
@"* ──────────────────────────────────────
* 例｜OCR读字：识别喷码/印刷的批次号、日期、序列号
* 比方：教小朋友认字三步走——①把字描黑(二值化) ②一个字一个字卡片上
*       (拆字符) ③拿卡片认(分类器)。分类器=Halcon训练好的「认字字典」
* 五步：二值化 → 拆字符去噪 → 按阅读顺序排 → 分类器认字 → 拼字符串
* 坑：字符粘连=拆成一个大块→先腐蚀断开；字号变了要改高宽筛选范围
* ──────────────────────────────────────
read_image (Image, 'keypad')
rgb1_to_gray (Image, GrayImage)
* 第1步 自动二值化：按直方图双峰自动找分界，明暗字符都兼容
binary_threshold (GrayImage, TextRegion, 'max_separability', 'light', UsedThreshold)
* 第2步 拆字符+去噪：高8~30宽4~30像素才算「字符身材」
connection (TextRegion, ConnectedRegions)
select_shape (ConnectedRegions, Chars, ['height','width','area'], 'and', [8,4,20], [30,30,500])
* 第3步 按阅读顺序排（像读书一样先上后左）
* sort_region只有5个参数：区域 → 出区域 → SortMode → Order → RowOrCol（'row'即先行后列，没有第6个）
sort_region (Chars, SortedChars, 'character', 'true', 'row')
* 第4步 加载自带「认字字典」(0-9A-Z) 认每个字符
read_ocr_class_mlp ('Industrial_0-9A-Z_Rej', OCRHandle)
do_ocr_multi_class_mlp (SortedChars, Image, OCRHandle, Class, Confidence)
* 第5步 拼成完整字符串
Text := ''
for i := 0 to |Class| - 1 by 1
    Text := Text + Class[i]
endfor
dev_display (Image)
dev_display (Chars)"),

            new TemplateDef("F 识别", "点阵字符识别（针打码）",
@"* ──────────────────────────────────────
* 例｜读点阵码：针式打印/点阵喷码（字符由小圆点拼成，常见于日期码）
* 比方：点阵字=用图钉钉出的字——先「连线」(膨胀)把点焊成笔画再认
* 关键技巧：dilation_circle膨胀半径2把点连成笔画，其余同标准OCR流程
* 坑：膨胀过头相邻字符会粘一起——从半径1.5开始试
* ──────────────────────────────────────
read_image (Image, 'dot_print_slanted')
rgb1_to_gray (Image, GrayImage)
binary_threshold (GrayImage, CharRegion, 'max_separability', 'dark', UsedThreshold)
* 膨胀把点阵「焊」成完整笔画
dilation_circle (CharRegion, RegionDilation, 2.0)
connection (RegionDilation, ConnectedRegions)
select_shape (ConnectedRegions, Chars, ['height','width'], 'and', [15,8], [40,40])
sort_region (Chars, SortedChars, 'character', 'true', 'row')
read_ocr_class_mlp ('Industrial_0-9A-Z_Rej', OCRHandle)
do_ocr_multi_class_mlp (SortedChars, Image, OCRHandle, Class, Confidence)
Text := ''
for i := 0 to |Class| - 1 by 1
    Text := Text + Class[i]
endfor
dev_display (Image)
dev_display (SortedChars)"),

            new TemplateDef("F 识别", "DataMatrix二维码读取",
@"* ──────────────────────────────────────
* 例｜读DM码：零部件激光打码追溯（MES过站标配）
* 比方：DM码=高密度的「黑白方格二维码」，手机扫不了工业DPM打码，
*       专用解码器从任意方向找三个「L形定位边」再解码
* 两步：建解码器(一次) → 全图找+解码(每帧)
* 坑：读不到先检查：码太小→加ROI或换镜头；打码太浅→调对比度增强
* ──────────────────────────────────────
read_image (Image, 'engraved')
* 'Data Matrix ECC 200'=最通用的DM码标准
create_data_code_2d_model ('Data Matrix ECC 200', [], [], DataCodeHandle)
* 全图搜索（[]=不限ROI）；结果字符串在DecodedDataStrings
* 参数顺序：图 → 码的轮廓XLD → 句柄 → 通用参数名[] → 通用参数值[] → 结果句柄 → 解码文本
find_data_code_2d (Image, SymbolXLDs, DataCodeHandle, [], [], ResultHandles, DecodedDataStrings)
* 坑：ResultHandles 是「句柄数组」(控制量)，不是图元对象，不能用 count_obj；
*     数个数直接用 |数组| 取长度
Number := |ResultHandles|
if (Number > 0)
    CodeText := DecodedDataStrings[0]
endif
dev_display (Image)"),

            new TemplateDef("F 识别", "一维条码读取",
@"* ──────────────────────────────────────
* 例｜读条码：产品标签Code128/EAN（扫码枪的软件替代方案）
* 比方：条码=宽窄条纹的摩尔斯电码——解码器找「条空宽度序列」翻译回数字
* 两步：建读取器(一次) → 解码(每帧)；'Automatic'自动试所有码制
* 坑：确定码制时写死'Code128'更快更准；条码反光先加偏振片
* ──────────────────────────────────────
read_image (Image, 'audi2')
rgb1_to_gray (Image, GrayImage)
create_bar_code_model ([], [], BarCodeHandle)
* 解码：Region=[]全图搜；找到后Region=条码位置
find_bar_code (Image, Region, BarCodeHandle, 'auto', DecodedDataStrings)
if (|DecodedDataStrings| > 0)
    Barcode := DecodedDataStrings[0]
endif
dev_display (Image)
dev_display (Region)"),

            // ═══════════════════════ G 有无与缺陷检测 ═══════════════════════

            new TemplateDef("G 缺陷检测", "涂胶连续性检测",
@"* ──────────────────────────────────────
* 例｜断胶检测：点胶工艺质检，胶线断了/缺胶一眼暴露
* 比方：挤牙膏——连续一条=合格；断成几截=挤堵了；
*       「数有几截」(连通域个数)就是断胶检测的本质
* 判定：好品=1~2个连通域+主段面积达标；碎成4段以上或主段太小=NG
* 坑：透明胶普通光看不见——用紫外光或偏振光打光后再检
* ──────────────────────────────────────
read_image (Image, 'needle1')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, GlueRegion, 0, 100)
opening_circle (GlueRegion, GlueClean, 1.5)
connection (GlueClean, ConnectedRegions)
count_obj (ConnectedRegions, BeadCount)
* 最大一段=主胶线，它的面积=胶量指标
* 注意：23.05 没有 area 这个算子，取面积统一用 area_center，行/列用 _ 占位丢弃
select_shape_std (ConnectedRegions, MainBead, 'max_area', 70)
area_center (MainBead, MainArea, _, _)
* ── 示教参数区：合格主胶段的最小面积（由「黄金样品」实测得到，写进配方）──
MinAreaExpected := 5000
if (BeadCount > 3 or MainArea < MinAreaExpected)
    Result := 'NG'
else
    Result := 'OK'
endif
dev_display (Image)
dev_display (MainBead)"),

            new TemplateDef("G 有无检测", "零件装配有无（ROI占比法）",
@"* ──────────────────────────────────────
* 例｜防错检测：卡座/插槽里零件装没装、装没装全
* 比方：检查书包里有没有课本——只翻开课本那格(ROI)看占了多满，
*       不用管整个书包。「占比>60%=装了」
* 三步：圈ROI(只看这块) → 数ROI内零件像素 → 占比判定
* 坑：工件位置会动→先模板匹配定位再圈ROI（见D类ROI联动例程）
* ──────────────────────────────────────
read_image (Image, 'clip')
* ── 示教参数区：ROI四角坐标（量产由示教界面画框得到并存进配方）──
* 这里取一块 100x200 的示例框；位置会动的产品要先匹配定位再变换ROI（见D类例程）
ROI_Row1 := 100
ROI_Col1 := 100
ROI_Row2 := 200
ROI_Col2 := 300
* 第1步 圈定ROI（坐标设计时定死）
gen_rectangle1 (ROIDesign, ROI_Row1, ROI_Col1, ROI_Row2, ROI_Col2)
reduce_domain (Image, ROIDesign, ImageReduced)
* 第2步 ROI内数亮像素（零件比背景亮）
rgb1_to_gray (ImageReduced, GrayImage)
threshold (GrayImage, PartRegion, 150, 255)
* 取面积只用 area_center（没有单独的 area 算子），圆心的行/列用 _ 丢弃
area_center (PartRegion, PartArea, _, _)
area_center (ROIDesign, ROIArea, ROIRow, ROICol)
* 第3步 占比判定（零件实际占ROI约80%→阈值0.6留余量）
Ratio := PartArea / ROIArea
if (Ratio > 0.6)
    Result := 'OK'
else
    Result := 'NG'
endif
dev_display (Image)
dev_display (ROIDesign)"),

            new TemplateDef("G 缺陷检测", "脏污/斑点（动态阈值法）",
@"* ──────────────────────────────────────
* 例｜查脏污：玻璃/白塑/布面上的污点——光照不匀时全局阈值失灵就用它
* 比方：判断一个人是否「鹤立鸡群」不能跟全国平均比，
*       要跟他「周围的鸡」比——动态阈值=每个像素和邻居均值比
* 三步：大窗口估局部背景 → var_threshold局部判异常 → 面积筛选
* 坑：StdDevScale0.4越小越敏感（误报越多），先0.5再往下调
* ──────────────────────────────────────
read_image (Image, 'can')
rgb1_to_gray (Image, GrayImage)
* 局部均值±标准差判异常：邻域15×15，只查比周围亮的('light')
var_threshold (GrayImage, DirtyRegion, 15, 15, 0.4, 2, 'light')
connection (DirtyRegion, ConnectedRegions)
select_shape (ConnectedRegions, Defects, 'area', 'and', 30, 1e6)
count_obj (Defects, Number)
dev_display (Image)
dev_display (Defects)"),

            new TemplateDef("G 缺陷检测", "标准图比对（BGA印刷缺陷）",
@"* ──────────────────────────────────────
* 例｜和黄金样图找不同：丝印/焊盘多印漏印连锡全抓住
* 比方：两张完全一样的透明胶片叠起来对着光看——不一样的地方「透不过光」
*       就是差异。abs_diff_image=自动版叠片检查
* 三步：读样图+当前图 → 逐像素求差 → 差值阈值化找异常块
* 坑：比对前必须对齐！错位1像素满图都是「假缺陷」（先模板匹配对齐）
* ──────────────────────────────────────
read_image (RefImage, 'bga_14x14_model')
read_image (Image, 'bga_14x14_defects')
* 逐像素差的绝对值：一样=0，不一样=差值×系数（abs_diff_image 只有4个参数）
abs_diff_image (Image, RefImage, AbsDiff, 1)
* 差值>50才算真差异（以下=压缩噪声）
threshold (AbsDiff, DiffRegion, 50, 255)
connection (DiffRegion, ConnectedRegions)
select_shape (ConnectedRegions, Defects, 'area', 'and', 20, 1e7)
count_obj (Defects, Number)
dev_display (Image)
dev_display (Defects)"),

            new TemplateDef("G 缺陷检测", "轮缘缺口检测（半径波动法）",
@"* ──────────────────────────────────────
* 例｜查崩边：齿轮/砂轮/轮圈外缘有没有缺口
* 比方：圆规画圆——圆上每点到圆心距离都=半径；哪段半径突然变短，
*       哪里就是「缺了一块」。跟踪轮廓点到圆心距离=缺口检测
* 三步：取外轮廓 → 平移到圆心为原点 → 半径数组统计找异常低点
* 坑：CenterRow/CenterCol先用Blob粗算圆心；齿轮本身半径有波动，
*     阈值要按「齿深」留余量
* ──────────────────────────────────────
read_image (Image, 'rim')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, RimRegion, 100, 255)
boundary (RimRegion, RegionBorder, 'outer')
gen_contour_region_xld (RegionBorder, Contour, 'border')
* ── 圆心：这里先用「区域质心」粗算（更稳的做法是模板匹配或拟合圆给出）──
area_center (RimRegion, RimArea, CenterRow, CenterCol)
* 把轮廓「搬」到圆心在原点的位置（仿射矩阵平移）
affine_trans_contour_xld (Contour, ContourCentered, [1,0,-CenterRow,0,1,-CenterCol])
* gen_contour_region_xld 一次产出「多条」轮廓，而 get_contour_xld 只吃「一条」
* 所以先按长度筛出最长的那条（=轮缘外圈），再取点；一条都没有时直接判NG
select_contours_xld (ContourCentered, LongContours, 'contour_length', 500, 1000000000, -0.5, 0.5)
count_obj (LongContours, NumContours)
if (NumContours == 0)
    Result := 'NG'
else
    select_obj (LongContours, OneContour, 1)
    get_contour_xld (OneContour, Rows, Cols)
    * 每点到圆心距离=半径数组
    RadiusArr := sqrt(Rows * Rows + Cols * Cols)
    tuple_mean (RadiusArr, MeanR)
    MinR := min(RadiusArr)
    * 最浅处比平均半径小15% = 有崩口（按零件实际调）
    if (MinR < MeanR * 0.85)
        Result := 'NG'
    else
        Result := 'OK'
    endif
endif
dev_display (Image)
dev_display (Contour)"),

            // ═══════════════════════ H 亚像素轮廓 ═══════════════════════

            new TemplateDef("H 亚像素轮廓", "边缘提取+直线拟合（车道线）",
@"* ──────────────────────────────────────
* 例｜亚像素直线：提取比像素还细的边缘并拟合直线
* 比方：铅笔线放大看是「好几个灰度渐变的像素」——亚像素边缘
*       能定位到「渐变的中点」，精度比数格子高一个量级
* 三步：edges_sub_pix提边缘 → 按长度筛选 → fit_line拟合
* 坑：sigma越大边缘越「圆滑」但细节丢；高速运动件先加频闪光源再提
* ──────────────────────────────────────
read_image (Image, 'autobahn')
* 亚像素边缘：canny,sigma1.5抗噪,20/40=双阈值
edges_sub_pix (Image, Edges, 'canny', 1.5, 20, 40)
* 只留长边缘（车道线很长，短边缘=车/护栏噪点）
select_contours_xld (Edges, LongEdges, 'contour_length', 200, 10000, -0.5, 0.5)
* 拟合直线：端点+方向+偏差Dist(越小拟合越好)
fit_line_contour_xld (LongEdges, 'regression', -1, 0, 5, 2, Row1, Column1, Row2, Column2, Nr, Nc, Dist)
dev_display (Image)
dev_display (LongEdges)"),

            new TemplateDef("H 亚像素轮廓", "轮廓平滑与简化",
@"* ──────────────────────────────────────
* 例｜轮廓瘦身：毛刺轮廓变干净+点数减半提速
* 比方：描图——先用铅笔随手勾(毛刺多)，再用「平滑」描一遍(光顺)，
*       最后「简化」只留关键拐点(点数少一半，形状不变)
* 三步：boundary取边 → smooth_contours_xld平滑 → gen_polygons_xld简化
* 坑：简化参数2=允许偏差像素，越大点越少越失真，比对用途统一即可
* ──────────────────────────────────────
read_image (Image, 'tooth_rim')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, GearRegion, 100, 255)
boundary (GearRegion, RegionBorder, 'outer')
gen_contour_region_xld (RegionBorder, Contours, 'border')
* 平滑窗口15（奇数），毛刺重加大到21
smooth_contours_xld (Contours, Smoothed, 15)
* 'ramer'普克算法抽稀：偏差2像素内的点全删（提速一半以上）
gen_polygons_xld (Smoothed, Simplified, 'ramer', 2)
dev_display (Image)
dev_display (Simplified)"),

            // ═══════════════════════ I 结果显示 ═══════════════════════

            new TemplateDef("I 结果显示", "图像标注（文字+十字+圆圈）",
@"* ──────────────────────────────────────
* 例｜图上画标注：检完在图上标 NG 红字 / 目标十字 / 缺陷圈
* 比方：老师批改作业画红圈打勾——机器检完也要「画给操作员看」，
*       人机互信全靠这一笔
* 三件套：dev_disp_text 文字  gen_cross_contour_xld 十字  gen_circle 圆圈
* 关键：标注只改「显示效果」，不改图像数据本身（存盘出去的还是干净原图）
* 坑：网上抄来的 disp_cross / disp_circle 在本插件里不可用（HDevEngine 只认
*     dev_* 那一套），一律换成三行式：
*     gen_* 生成几何 → dev_set_color 定色 → dev_display 画出来
*     文字要放大另说：set_display_font 是可用的，但字号是窗口级状态，
*     收尾得显式写回 12（详见「L 效果图标注 | 大字号箭头标注」）
* ──────────────────────────────────────
read_image (Image, 'fabrik')
get_image_size (Image, Width, Height)
dev_display (Image)
* ① 目标中心画绿色十字：臂长40像素，角度0.7854弧度（=45°斜十字，更醒目）
Row := Height * 0.40
Column := Width * 0.45
gen_cross_contour_xld (Cross, Row, Column, 40, 0.7854)
dev_set_color ('green')
dev_display (Cross)
* ② 缺陷位置画红圈：margin=只描边（fill会把缺陷整个糊住）
DefectRow := Height * 0.62
DefectCol := Width * 0.60
gen_circle (Defect, DefectRow, DefectCol, 50)
dev_set_draw ('margin')
dev_set_color ('red')
dev_display (Defect)
* ③ 左上角写结论：'window'=钉在窗口角上，缩放时不跟着跑
*    末两项给文字垫黑底（box），深色图上照样看得清
dev_disp_text ('Result: 3 defects', 'window', 'top', 'left', 'red', ['box','box_color'], ['true','black'])
* ④ 缺陷旁写编号：'image'=用图像坐标，文字跟着缺陷一起缩放
dev_disp_text ('#1', 'image', DefectRow - 60, DefectCol + 55, 'yellow', [], [])"),

            new TemplateDef("I 结果显示", "图像存盘留档（NG追溯）",
@"* ──────────────────────────────────────
* 例｜NG存图：不良品自动拍照留档（质量追溯+算法迭代的数据金矿）
* 比方：行车记录仪——平时不存，一「碰撞」(NG)立刻保存视频
* 用法：write_image(图,'png',0,'文件名前缀')，-1结尾自动加序号防覆盖
* 坑：留档目录要定期清理！每天几百张NG图会吃满硬盘（配删除任务）
* ──────────────────────────────────────
read_image (Image, 'fabrik')
* ── 示教参数区：Result 由前面的检测例程给出；这里给个示例值保证脚本可独立跑 ──
Result := 'NG'
* 只存NG品（Result来自前面检测例程的判定）
if (Result == 'NG')
    * 存到程序目录下，png无损，末参数自动追加序号防覆盖
    write_image (Image, 'png', 0, 'ng_image')
endif
* 想连「标注画面」一起存：grab_window(24, Screen)抓屏后再write_image(Screen,...)"),

            new TemplateDef("I 结果显示", "多ROI批量检测循环",
@"* ──────────────────────────────────────
* 例｜按清单逐个检查：一张图N个固定检查位（引脚1~N逐个测）
* 比方：体检流水线——项目清单(数组)一项项过，每项一个结果，
*       最后汇总报告
* 三步：检查清单做成数组 → for循环逐个圈ROI检测 → 结果存数组汇总
* 坑：Results:=[Results,新值]是Halcon数组追加写法；循环内别用同名变量
* ──────────────────────────────────────
read_image (Image, 'ic0')
rgb1_to_gray (Image, GrayImage)
* 检查清单：每个检查位的中心坐标（设计时定）
CheckRows := [120,160,200,240,280]
CheckCols := [300,300,300,300,300]
Results := []
for i := 0 to |CheckRows| - 1 by 1
    * 以清单项为中心建20x60的ROI并检测（注意：HDevelop里注释一律用星号开头，单引号会被当成代码而报错）
    gen_rectangle1 (ROI, CheckRows[i] - 10, CheckCols[i] - 30, CheckRows[i] + 10, CheckCols[i] + 30)
    reduce_domain (GrayImage, ROI, ImageReduced)
    threshold (ImageReduced, Bright, 128, 255)
    * 取面积统一用 area_center，行/列用 _ 丢弃
    area_center (Bright, BrightArea, _, _)
    Results := [Results,BrightArea]
endfor
* 汇总：最小面积=最差检查位（每个>200才算全OK）
tuple_min (Results, WorstPin)
dev_display (Image)
dev_display (ROI)"),

            // ═══════════════════════ J 综合工艺（复杂案例）═══════════════════════

            new TemplateDef("J 综合工艺", "涂胶全检四连（定位+连续+宽度+溢出）",
@"* ──────────────────────────────────────
* 例｜涂胶全检：一台相机管住涂胶工艺的四个质量维度（真实工位级流程）
* 场景：电池盖板密封胶——胶没打(漏涂)/打断了(断胶)/打太粗(溢出)
*       都是致命缺陷。一个脚本一次拍照全查完
* 工艺链：模板定位产品 → ROI随位姿走 → 分割胶线 →
*         查有无(面积) → 查连续(段数) → 查宽度(距离变换) → 综合判定
* 坑：四个判定阈值都来自「黄金样品」实测，别拍脑袋定
* ──────────────────────────────────────
read_image (Image, 'needle1')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* ── 示教参数区：以下每一项都来自「示教 + 黄金样品」，换型时只改这一段 ──
* 建模那一刻的产品中心
RefRow := 0.5 * Height
RefCol := 0.5 * Width
* 设计阶段画好的检测框（行/列上下界）
ROI_R1 := 0.25 * Height
ROI_C1 := 0.20 * Width
ROI_R2 := 0.75 * Height
ROI_C2 := 0.80 * Width
* 规格限：胶区最小面积、允许最大胶宽(mm)
MinAreaExpected := 3000
MaxWidth_mm := 0.6
* ── 第1步 定位产品本体（正常示教建模后存盘；这里现场建一次保证示例可跑）──
create_shape_model (Image, 4, 0, rad(360), rad(3), 'auto', 'use_polarity', 30, 10, ModelID)
find_shape_model (Image, ModelID, -rad(10), rad(20), 0.5, 1, 0.5, 'none', 0, 0.75, MRow, MCol, MAngle, MScore)
if (|MRow| == 0)
    Result := 'NG-找不到产品'
    return ()
endif
* ── 第2步 检测区跟随产品位姿 ──
gen_rectangle1 (ROIDesign, ROI_R1, ROI_C1, ROI_R2, ROI_C2)
vector_angle_to_rigid (RefRow, RefCol, 0, MRow, MCol, MAngle, HomMat2D)
affine_trans_region (ROIDesign, ROITrans, HomMat2D, 'false')
reduce_domain (Image, ROITrans, ImageReduced)
* ── 第3步 分割胶线 ──
threshold (ImageReduced, GlueRegion, 0, 100)
opening_circle (GlueRegion, GlueClean, 1.5)
connection (GlueClean, ConnectedRegions)
* ── 第4步 四连判定 ──
* 4a 有无：胶区总面积（没有 area 算子，用 area_center + _ 丢掉圆心）
area_center (GlueClean, GlueArea, _, _)
* 4b 连续：段数≤2为连续
count_obj (ConnectedRegions, BeadCount)
* 4c 宽度：主段距离变换取最大半宽（Foreground='true'，图宽图高必填）
select_shape_std (ConnectedRegions, MainBead, 'max_area', 70)
distance_transform (MainBead, DistImage, 'euclidean', 'true', Width, Height)
intensity (MainBead, DistImage, MinDist, HalfWidthMax)
* 像素当量(毫米/像素)：由标定得到，这里给个示例值
PixelSizeMm := 0.01
GlueWidth_mm := HalfWidthMax * 2 * PixelSizeMm
* 4d 综合判定（规格：面积≥MinArea 段数≤2 宽度≤MaxWidth）
if (GlueArea < MinAreaExpected)
    Result := 'NG-漏胶'
elseif (BeadCount > 2)
    Result := 'NG-断胶'
elseif (GlueWidth_mm > MaxWidth_mm)
    Result := 'NG-胶过宽溢出'
else
    Result := 'OK'
endif
dev_display (Image)
dev_display (ROITrans)
dev_display (MainBead)"),

            new TemplateDef("J 综合工艺", "视觉引导机器人抓取（坐标+角度输出）",
@"* ──────────────────────────────────────
* 例｜引导抓取：输出每个零件的X/Y/角度给机器人（散料拾取核心程序）
* 场景：振动盘散乱零件，机器人逐个吸——每个零件要「在哪+歪多少度」
* 工艺链：分割 → 开运算去粘连 → 筛选合格零件 →
*         smallest_rectangle2最小外接矩形 → orientation_region角度 → 按序输出
* 关键概念：机器人要的是「零件自身朝向」——最小外接矩形的角度就是它
* 坑：Row/Column是像素，发给机器人前必须过「手眼标定」矩阵换算！
* ──────────────────────────────────────
read_image (Image, 'pellets')
rgb1_to_gray (Image, GrayImage)
* ── 第1步 分割+分离 ──
threshold (GrayImage, Region, 100, 255)
opening_circle (Region, RegionOpened, 3.0)
connection (RegionOpened, ConnectedRegions)
select_shape (ConnectedRegions, Parts, 'area', 'and', 300, 1e7)
count_obj (Parts, Number)
* ── 第2步 逐个零件算抓取点+角度 ──
for i := 1 to Number by 1
    select_obj (Parts, Part, i)
    * 抓取点=质心
    area_center (Part, Area, Row, Column)
    * 抓取角=最小外接矩形方向（吸盘要顺着零件长边转）
    smallest_rectangle2 (Part, RC, CC, Phi, Length1, Length2)
    * 输出给机器人的数据（示意：拼成字符串走通讯）
    tuple_number (Row, RStr)
    tuple_number (Column, CStr)
    tuple_number (deg(Phi), DegStr)
    SendData := 'P' + (i - 1) + ':' + RStr + ',' + CStr + ',' + DegStr + ';'
endfor
dev_display (Image)
dev_display (Parts)"),

            new TemplateDef("J 综合工艺", "多Mark阵列定位（稳健平均法）",
@"* ──────────────────────────────────────
* 例｜阵列Mark对位：面板上N个Mark全部找出，「投票」定出最稳位姿
* 场景：显示屏/大面板对位——单个Mark可能被脏污认错，
*       8个Mark一起算，错一个不影响大局（这就是「稳健」）
* 工艺链：Blob找所有Mark → 按设计坐标就近配对 →
*         逐对算偏移 → 剔除离群点 → 剩余取平均=最终位姿
* 坑：配对用「就近原则」；离群点剔除用中位数±2倍MAD，比平均值抗干扰
* ──────────────────────────────────────
read_image (Image, 'marks')
rgb1_to_gray (Image, GrayImage)
* ── 第1步 找所有Mark ──
threshold (GrayImage, Region, 128, 255)
connection (Region, ConnectedRegions)
select_shape (ConnectedRegions, Marks, ['circularity','area'], 'and', [0.7,200], [1.0,1e7])
area_center (Marks, Area, FoundRows, FoundCols)
NumFound := |FoundRows|
* ── 示教参数区：设计时Mark的理论位置（行数组+列数组，一一对应）──
* 量产时由示教界面逐个点出或从DXF图纸导入；这里给4个Mark的示例坐标
DesignRows := [120,120,320,320]
DesignCols := [100,300,100,300]
* ── 第2步 与设计坐标配对（DesignRows/DesignCols=设计时Mark理论位置数组）──
Dx := []
Dy := []
for i := 0 to NumFound - 1 by 1
    * 找离当前Mark最近的设计点（暴力法，几十个Mark足够快）
    DistArr := sqrt((DesignRows - FoundRows[i]) * (DesignRows - FoundRows[i]) + (DesignCols - FoundCols[i]) * (DesignCols - FoundCols[i]))
    * 23.05 没有 idx_min：先按值排序拿到「下标数组」，第一个元素就是最小值的下标
    tuple_sort_index (DistArr, SortIdx)
    MinIdx := SortIdx[0]
    Dx := [Dx, FoundCols[i] - DesignCols[MinIdx]]
    Dy := [Dy, FoundRows[i] - DesignRows[MinIdx]]
endfor
* ── 第3步 稳健平均：中位数±2MAD剔离群后取均值 ──
keepDx := Dx
keepDy := Dy
* 坑：一个Mark都没找到时 Dx 是空数组，直接 tuple_median 会报
*     Wrong number of values of control parameter 1 —— 先判空
if (|Dx| == 0)
    ShiftX := 0
    ShiftY := 0
    Result := 'NO_MARK'
else
    tuple_median (Dx, MedDx)
    tuple_median (Dy, MedDy)
    * ── 结果：整体平移量=平均偏移，直接叠加到所有设计坐标上 ──
    tuple_mean (keepDx, ShiftX)
    tuple_mean (keepDy, ShiftY)
    Result := 'OK'
endif
dev_display (Image)
dev_display (Marks)"),

            new TemplateDef("J 综合工艺", "匹配+卡尺一体化（引脚共面度全检）",
@"* ──────────────────────────────────────
* 例｜定位+测量一条龙：IC引脚逐个量宽度，查共面/变形（产线标准工位）
* 场景：引脚歪了/胖了/瘦了=焊接隐患；产品位置还会来料偏差
* 工艺链：形状匹配定产品位姿 → 生成N个跟随ROI卡尺位 →
*         逐引脚卡尺测宽 → 统计min/max/mean → 超差判定
* 关键：所有测量坐标都从「设计数组」经位姿矩阵变换而来——
*       换型只改设计数组，脚本一行不动
* ──────────────────────────────────────
read_image (Image, 'ic0')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* ── 示教参数区：以下全部来自「示教 + 设计图纸」，换型只改这一段，代码不动 ──
* 建模那一刻的产品中心
RefRow := 0.5 * Height
RefCol := 0.5 * Width
* N个引脚卡尺的设计位置：中心行 / 中心列 / 角度（三个数组一一对应）
PinRows := [150,170,190,210,230]
PinCols := [200,200,200,200,200]
PinPhis := [0,0,0,0,0]
* 卡尺半长/半宽(像素)：半长要横跨整条引脚，半宽是允许的横向抖动带
PinLen1 := 40
PinLen2 := 6
* 判定阈值：各引脚宽度极差超过TolW(像素)即NG
TolW := 4
* ── 第1步 定位产品（模型正常示教一次后存盘、量产read；这里现场建保证可跑）──
create_shape_model (Image, 4, 0, rad(360), rad(3), 'auto', 'use_polarity', 30, 10, ModelID)
find_shape_model (Image, ModelID, -rad(5), rad(10), 0.5, 1, 0.5, 'none', 0, 0.75, MRow, MCol, MAngle, MScore)
if (|MRow| == 0)
    Result := 'NG-找不到产品'
    return ()
endif
vector_angle_to_rigid (RefRow, RefCol, 0, MRow, MCol, MAngle, HomMat2D)
* ── 第2步 设计卡尺位逐个变换+测量（PinRows/PinCols/PinPhis=设计数组）──
Widths := []
for i := 0 to |PinRows| - 1 by 1
    * 设计卡尺位→实际位姿
    affine_trans_pixel (HomMat2D, PinRows[i], PinCols[i], ActRow, ActCol)
    ActPhi := PinPhis[i] + MAngle
    gen_measure_rectangle2 (ActRow, ActCol, ActPhi, PinLen1, PinLen2, Width, Height, 'bilinear', MeasureHandle)
    measure_pairs (Image, MeasureHandle, 1, 30, 'all', 'all', RE1, CE1, A1, RE2, CE2, A2, IntraDist, InterDist)
    * 该引脚宽度（没找到记-1）
    if (|IntraDist| > 0)
        Widths := [Widths,IntraDist[0]]
    else
        Widths := [Widths,-1]
    endif
endfor
* ── 第3步 统计判定：漏检数+宽度极差 ──
* 23.05 没有 tuple_count：逐元素比较得到 0/1 掩码，再求和就是漏检个数
MissMask := Widths = -1
tuple_sum (MissMask, MissCount)
tuple_min (Widths, MinW)
tuple_max (Widths, MaxW)
if (MissCount > 0 or (MaxW - MinW) > TolW)
    Result := 'NG'
else
    Result := 'OK'
endif
dev_display (Image)"),

            // ═══════════════════════ K 综合工艺·真实工位级 ═══════════════════════
            new TemplateDef("K 工位级综合", "PCB贴片AOI（Mark建系+位号清单逐检）",
@"* ════════════════════════════════════════════════
* 例｜PCB贴片检查：一个相机看整板，逐位号查漏贴/偏移/极性
* 比方：老师按点名册(位号清单)逐个检查学生到没到、坐没坐歪——
*       先对准教室门牌(Mark)确定『哪个座位号对应哪个位置』，再点名
* 流程：Mark建板坐标系 → 位号清单换算成实际位置 → 逐位：有无+偏移+极性 → NG名单
* 亮点：换板型只改『设计坐标数组』，代码一行不动（这就是清单驱动的价值）
* 坑：Mark要选板上永久存在的（丝印框/焊盘），别选可能漏贴的元件当基准
* 试跑：本例用打包好的 bga_14x14_model.png（BGA芯片焊球阵列，真实SMT图）
* ════════════════════════════════════════════════
read_image (Image, 'bga_14x14_model')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)

* ─── 第1步：找板Mark建立『板坐标系』（设计坐标→实际坐标的翻译官） ───
* 板上十字/角Mark，位置永远准确（丝印做的，贴片再烂它也在）
* 提速做法：gen_rectangle1在Mark设计位附近开窗+reduce_domain只搜小图
threshold (GrayImage, Dark, 0, 100)
connection (Dark, MarksAll)
* 按面积选出Mark（示例值，按实际Mark大小调）
select_shape (MarksAll, Marks, ['area'], 'and', [50], [5000])
count_obj (Marks, NumMarks)
if (NumMarks < 1)
    Result := 'NG:找不到板Mark'
    return ()
endif
* 取第一个Mark的中心做板原点（select_shape默认按面积排过，示例取[0]）
area_center (Marks, MArea, MarkRow, MarkCol)
BoardRow := MarkRow[0]
BoardCol := MarkCol[0]

* ─── 第2步：位号清单（设计坐标=程序调试时量好的，单位像素） ───
* 每个位号：相对Mark的(行,列) + 元件尺寸 + 最小灰度(极性判据用)
CompRows := [-20, 60, 140, 140]
CompCols := [80, 80, 60, 160]
CompNames := ['C1', 'R1', 'D1', 'U1']
HalfSize := 18
TolOffset := 6

* ─── 第3步：逐位号检查 ───
NGList := []
for i := 0 to |CompRows| - 1 by 1
    * 设计坐标→实际坐标（板会放歪放偏，全靠Mark原点换算）
    ActRow := BoardRow + CompRows[i]
    ActCol := BoardCol + CompCols[i]
    * 以实际位为中心开窗（外扩一点容忍偏移）
    gen_rectangle1 (SearchWin, ActRow - HalfSize - 6, ActCol - HalfSize - 6, ActRow + HalfSize + 6, ActCol + HalfSize + 6)
    reduce_domain (GrayImage, SearchWin, Win)
    * 查有无：元件比板暗，暗像素占比>15%算『在』
    threshold (Win, DarkIn, 0, 90)
    area_center (DarkIn, DarkArea, CompRow, CompCol)
    gen_rectangle1 (CompBox, ActRow - HalfSize, ActCol - HalfSize, ActRow + HalfSize, ActCol + HalfSize)
    area_center (CompBox, BoxArea, _, _)
    Presence := DarkArea / BoxArea
    if (Presence < 0.15)
        tuple_concat (NGList, CompNames[i] + ':漏贴', NGList)
    else
        * 查偏移：元件实际中心 vs 设计中心
        OffsetDist := sqrt((CompRow - ActRow) * (CompRow - ActRow) + (CompCol - ActCol) * (CompCol - ActCol))
        if (OffsetDist > TolOffset)
            tuple_concat (NGList, CompNames[i] + ':偏移' + OffsetDist$'.1f', NGList)
        endif
    endif
endfor

* ─── 第4步：结果显示 + NG名单 ───
dev_display (GrayImage)
dev_set_color ('yellow')
dev_display (SearchWin)
if (|NGList| > 0)
    Result := NGList
    dev_disp_text (NGList, 'window', 12, 12, 'red', [], [])
else
    Result := ['OK']
endif
return ()"),

            new TemplateDef("K 工位级综合", "多相机图像拼接（含拼接标定）",
@"* ════════════════════════════════════════════════
* 例｜多相机拼大图：4个小视野相机拼成1张大图（大视野检测省镜头钱）
* 比方：把4格漫画拼成整页——每格可能歪一点、缩放一点，
*       靠『相邻格都露脸的同一个参照物』定出每格该贴哪、怎么旋转
* 流程：标定(一次性)：拍公共Mark→每台相机算一个『子图→全局』矩阵
*       运行(每次)：各子图按矩阵摆正→叠进同一全局画布
* 亮点：矩阵存成tuple数组，产线每次只用5行代码完成拼接
* 坑：标定Mark必须落在两视野的【重叠区】；Mark越少越偏，精度=标定质量
* 试跑：用打包好的 ic0/ic_pin 两张图当左右相机（真实产线用同一颗芯片的两拍）
* ════════════════════════════════════════════════
read_image (Cam1Image, 'ic0')
read_image (Cam2Image, 'ic_pin')
get_image_size (Cam1Image, W1, H1)

* ─── 第1步：拼接标定（每台相机各做一次，结果永久复用） ───
* 相机1视野里的公共Mark像素坐标（3个点对应全局坐标，越多越准）
* 全局坐标怎么来？——用机床走一个『固定步长』拍出来的位置，或直接量治具
SrcRows1 := [50, 50, 400]
SrcCols1 := [60, 500, 300]
DstRows1 := [50, 50, 400]
DstCols1 := [60, 560, 300]
vector_to_hom_mat2d (SrcRows1, SrcCols1, DstRows1, DstCols1, HomMat1)
* 相机2：Mark在它的视野里偏右了，要『平移回来』才能和相机1接上
SrcRows2 := [50, 50, 400]
SrcCols2 := [50, 490, 250]
DstRows2 := [50, 50, 400]
DstCols2 := [560, 1000, 760]
vector_to_hom_mat2d (SrcRows2, SrcCols2, DstRows2, DstCols2, HomMat2)

* ─── 第2步：拼接执行（每台相机一行，扩展第N台照抄） ───
* 子图按矩阵变换到全局位置（插值模式必须写全名 'nearest_neighbor' 最快；精度要求高用'bilinear'）
affine_trans_image (Cam1Image, Global1, HomMat1, 'nearest_neighbor', 'false')
affine_trans_image (Cam2Image, Global2, HomMat2, 'nearest_neighbor', 'false')

* ─── 第3步：叠显验证（重叠区内容应该严丝合缝对齐） ───
dev_display (Global1)
dev_set_color ('blue')
dev_display (Global2)
* 真产品做法：gen_image_proto开一张大画布，把两路像素『印』上去后
* 整图送下游算法；这里用叠显演示对齐效果，教学足够
* 量错位：在重叠区放Mark，拼好后测两路Mark中心距离，>1像素=标定重做
Result := 'OK'
return ()"),

            new TemplateDef("K 工位级综合", "手眼标定+抓取引导（9点法）",
@"* ════════════════════════════════════════════════
* 例｜相机-机器人手眼标定：把『眼睛看到的像素』翻译成『手要去的毫米』
* 比方：隔着玻璃指东西——你眼睛说『在那！』，手要换算成自己用多大力气
*       戳过去；标定=让眼睛和手『对暗号』：机器人去9个已知点，
*       相机各拍一次记像素坐标，两本账一对照就得出翻译公式
* 流程：标定(9点采样→矩阵，存起来永久用) → 运行(找工件→矩阵换算→输出XYZ)
* 适用：相机装架子不动、机器人动（Eye-to-Hand固定式）
* 坑：①9个点要摊满整个工作区，挤一角则边缘误差大
*     ②Z向高度这套矩阵不管！Z=标定平面高度+固定抓取深，写进常量
* 试跑：没机器人也能学——把9组『机器人坐标↔像素坐标』换成纸上手画的
*       假数据，看懂矩阵怎么把像素行变成毫米行
* ════════════════════════════════════════════════
read_image (Image, 'can')
rgb1_to_gray (Image, GrayImage)

* ─── 第1步：手眼标定（一次性，标定完把HomMat存成配置） ───
* 机器人示教9个点（毫米），相机拍照记下工件在图里的像素位置
* 真实做法：机器人走9宫格，每站拍下吸盘上Mark的像素，两列抄进数组
RobotX := [100, 200, 300, 100, 200, 300, 100, 200, 300]
RobotY := [100, 100, 100, 200, 200, 200, 300, 300, 300]
PixRow := [412, 415, 418, 512, 514, 517, 611, 614, 617]
PixCol := [302, 452, 603, 301, 451, 602, 300, 450, 601]
* 像素→机器人 的仿射矩阵（含旋转/缩放/平移，9点最小二乘最稳）
vector_to_hom_mat2d (PixRow, PixCol, RobotY, RobotX, HomMatEye2Arm)

* ─── 第2步：生产运行（每次抓取） ───
* 找工件（示例：易拉罐顶视，圆形好认）
threshold (GrayImage, Region, 0, 120)
connection (Region, Regions)
select_shape (Regions, Part, ['circularity', 'area'], 'and', [0.7, 500], [1, 1e9])
count_obj (Part, Num)
if (Num < 1)
    Result := 'NG:没找到工件'
    return ()
endif
area_center (Part, Area, WRow, WCol)
* 姿态角（长条零件吸盘要顺着长边转）
smallest_rectangle2 (Part, CRow, CCol, Phi, Len1, Len2)

* ─── 第3步：像素坐标→机器人坐标（翻译！就这一行） ───
affine_trans_point_2d (HomMatEye2Arm, WRow, WCol, RobotRow, RobotCol)
* 角度也要换算：矩阵里的旋转分量单独取出来加到零件角上
* （仿射矩阵R行：[0,0]=cos缩放 [0,1]=sin缩放，atan2还原旋转角）
* 机器人Z=标定台面高+工件厚的一半（标定时量好写死，或加激光测高）
GrabZ := 45.0
* 输出给机器人（示意：拼字符串走TCP/PLC，数值单位mm）
Result := [RobotCol$'.2f', RobotRow$'.2f', GrabZ$'.2f', (Phi * 3.14159 / 180)$'.3f']

* ─── 第4步：画验证（像素系里画出机器人要去的位置，肉眼复核） ───
dev_display (GrayImage)
* 反算回像素画十字：机器人坐标→像素 用逆矩阵
hom_mat2d_invert (HomMatEye2Arm, HomMatInv)
affine_trans_point_2d (HomMatInv, RobotCol, RobotRow, ChkRow, ChkCol)
gen_cross_contour_xld (Cross, ChkRow, ChkCol, 20, 0.785398)
dev_set_color ('green')
dev_display (Cross)
return ()"),

            new TemplateDef("K 工位级综合", "颜色检测（色差判定OK/NG）",
@"* ════════════════════════════════════════════════
* 例｜颜色检测：判断产品颜色对不对（涂料/护套/指示灯/连接器壳体）
* 比方：验货员拿『标准色卡』对着看——我们把色卡变成三个数字
*       (R,G,B)均值，算『颜色距离』，超过阈值就是偏色/错色
* 流程：拆R/G/B通道 → 检测区取三通道均值 → 与标准色算距离 → 判定
* 亮点：颜色距离公式=空间两点距离，红配绿距离大、深红配红距离小
* 坑：①光照影响RGB绝对值！先在同一光源下采『标准色』
*     ②反光/阴影区取均值会被拉偏——ROI避开高光和阴影
*     ③要求更高用XYZ/Lab色空间（更接近人眼感知），入门用RGB够用
* 试跑：本例用打包好的 pcb_color.png
*     ⚠ 资产库 32 张图里只有它是 3 通道，其余全是单通道——
*       对单通道图 decompose3 会直接报 Wrong number of image channels
* ════════════════════════════════════════════════
read_image (Image, 'pcb_color')

* ─── 第1步：彩色图拆成 R/G/B 三张单通道灰度图 ───
decompose3 (Image, RChannel, GChannel, BChannel)

* ─── 第2步：定义检测区（灯头位置，圆ROI；产线用标定/匹配定住它） ───
gen_circle (CheckROI, 200, 330, 25)

* ─── 第3步：取ROI内三通道均值（intensity=区域统计，_不要的出参） ───
intensity (CheckROI, RChannel, MeanR, _)
intensity (CheckROI, GChannel, MeanG, _)
intensity (CheckROI, BChannel, MeanB, _)

* ─── 第4步：和『标准色』比距离（标准色=调试时同光源采一次抄下来） ───
StdR := 200
StdG := 30
StdB := 30
ColorDist := sqrt((MeanR - StdR) * (MeanR - StdR) + (MeanG - StdG) * (MeanG - StdG) + (MeanB - StdB) * (MeanB - StdB))
TolColor := 60
* 实测值和判定依据一起打出来，偏色时一眼看到差在哪个通道
Meas := 'RGB(' + MeanR$'.0f' + ',' + MeanG$'.0f' + ',' + MeanB$'.0f' + ') Std(' + StdR$'.0f' + ',' + StdG$'.0f' + ',' + StdB$'.0f' + ') Dist' + ColorDist$'.1f' + ' Tol' + TolColor$'.0f'
if (ColorDist > TolColor)
    Result := ['NG: color deviation', Meas]
else
    Result := ['OK', Meas]
endif

* ─── 第5步：判它到底是哪个主色（备用：不知道标准色时反推） ───
if (MeanR > MeanG and MeanR > MeanB)
    MainColor := 'red'
elseif (MeanG > MeanB)
    MainColor := 'green'
else
    MainColor := 'blue'
endif

* ─── 第6步：显示（ROI框出来+报出测得的RGB） ───
dev_display (Image)
dev_set_color ('yellow')
dev_display (CheckROI)
dev_disp_text (['R:' + MeanR$'.0f','G:' + MeanG$'.0f','B:' + MeanB$'.0f','Main:' + MainColor], 'window', 10, 10, 'green', [], [])
return ()"),

            new TemplateDef("K 工位级综合", "线序检测（端子排/排线颜色顺序）",
@"* ════════════════════════════════════════════════
* 例｜线序检测：端子排上6根线，颜色顺序接错/漏接/错位全抓出来
* 比方：电工接线的『色标规矩』(棕红橙黄绿蓝)——质检员逐孔看色环，
*       我们给每个孔拍『色卡照片』(RGB均值)和规矩表逐一对答案
* 流程：按孔位数组逐个开窗 → 测该孔导线颜色 → 就近匹配色号
*       → 和期望线序比对 → 输出『第几孔:应为X实为Y』
* 亮点：孔位数、线序、色号全是数组——换规格改3行数据不改代码
* 坑：①导线会反光：ROI取线身中段，避开高光点
*     ②黑白线在灰度下分不清！必须用彩色R/G/B（这就是本例核心）
*     ③色号参考值要在产线光源下重新采一遍（实验室值会偏）
* 试跑：本例用 pcb_color.png 演示『逐位取色对答案』的完整骨架，
*       换上端子排实拍图（线放平、光打匀）即可实战
* ════════════════════════════════════════════════
read_image (Image, 'pcb_color')
decompose3 (Image, RChannel, GChannel, BChannel)
get_image_size (Image, Width, Height)

* ─── 第1步：色号库（调试时在产线光源下采的标准RGB） ───
ColorNames := ['brown', 'red', 'orange', 'yellow', 'green', 'blue', 'black', 'white']
RefR := [120, 200, 230, 230, 0, 0, 30, 230]
RefG := [60, 30, 120, 200, 120, 0, 30, 230]
RefB := [20, 30, 20, 0, 60, 200, 30, 230]

* ─── 第2步：孔位清单（每孔导线中心的像素坐标，调试时示教好） ───
WireRows := [240, 240, 240, 240, 240, 240]
WireCols := [120, 170, 220, 270, 320, 370]
* 期望线序（色号库下标）：棕红橙黄绿蓝 = 标准接法
ExpectSeq := [0, 1, 2, 3, 4, 5]

* ─── 第3步：逐孔测色对答案 ───
NGList := []
InfoList := []
for i := 0 to |WireRows| - 1 by 1
    * 该孔小ROI（半径10，只套住线身）
    gen_circle (WireROI, WireRows[i], WireCols[i], 10)
    intensity (WireROI, RChannel, MeanR, _)
    intensity (WireROI, GChannel, MeanG, _)
    intensity (WireROI, BChannel, MeanB, _)
    * 到8个色号各算距离，取最小者=识别结果
    DistToColors := []
    for c := 0 to |ColorNames| - 1 by 1
        D := sqrt((MeanR - RefR[c]) * (MeanR - RefR[c]) + (MeanG - RefG[c]) * (MeanG - RefG[c]) + (MeanB - RefB[c]) * (MeanB - RefB[c]))
        tuple_concat (DistToColors, D, DistToColors)
    endfor
    * 23.05 没有 min_index：tuple_sort_index 给出升序下标，[0]即最近色号
    tuple_sort_index (DistToColors, SortedIdx)
    BestIdx := SortedIdx[0]
    * 每孔实测数据都打出来：RGB实测值→匹配色号→距离（校准色号库全靠它）
    Info := 'W' + (i + 1) + ': RGB(' + MeanR$'.0f' + ',' + MeanG$'.0f' + ',' + MeanB$'.0f' + ') -> ' + ColorNames[BestIdx] + ' d=' + DistToColors[BestIdx]$'.0f'
    tuple_concat (InfoList, Info, InfoList)
    * 距离太大=啥色都不像（漏线/反光大）单独报
    if (DistToColors[BestIdx] > 90)
        tuple_concat (NGList, 'W' + (i + 1) + ': no valid color', NGList)
    elseif (BestIdx != ExpectSeq[i])
        tuple_concat (NGList, 'W' + (i + 1) + ': want ' + ColorNames[ExpectSeq[i]] + ' got ' + ColorNames[BestIdx], NGList)
    endif
endfor

* ─── 第4步：结果显示（绿圈=孔位；左上=每孔实测RGB和匹配色） ───
dev_display (Image)
dev_set_color ('green')
for i := 0 to |WireRows| - 1 by 1
    gen_circle (Mark, WireRows[i], WireCols[i], 12)
    dev_display (Mark)
endfor
dev_disp_text (InfoList, 'window', 10, 10, 'yellow', [], [])
if (|NGList| > 0)
    Result := NGList
    dev_disp_text (NGList, 'window', 10, 400, 'red', [], [])
else
    Result := ['OK: wire sequence correct']
endif
return ()"),

            // ═══════════════════════ L 效果图标注 ═══════════════════════

            new TemplateDef("L 效果图标注", "区域显示风格（填充/轮廓/外接框）",
@"* ════════════════════════════════════════════════
* 例｜同一批区域，三种画法同框：让效果图自己会说话
* 比方：验货报告上「涂红的=不良品」「描边的=看着没问题」「画框的=要复测」
*       —— 数据是同一份，画法不同，读图的人理解速度差十倍
* 三个开关：
*   dev_set_draw  ('fill'实心 / 'margin'只描边)
*   dev_set_shape ('original原形 / rectangle2旋转外接框 / outer_circle最小外接圆 /
*                  inner_circle最大内切圆 / convex凸包 / ellipse等面积椭圆 / icon / rectangle1)
*                  ⚠ 23.05 已无老书上的 'all' / 'component'，写了运行期报错
*   dev_set_line_width (线宽，只对 margin 和轮廓类生效)
* 坑：插件在脚本「结束时」导出一次画布，所以先后覆盖的显示是看不见的——
*     要对比就在同一帧里画给不同的对象，本例正是这么做的
* ════════════════════════════════════════════════
read_image (Image, 'punched_holes')
rgb1_to_gray (Image, GrayImage)
* 取亮区：实测本图正好 6 块（每块约 2.4 万像素），三种风格各分到 2 个，同框对比最直观
threshold (GrayImage, BrightRegion, 100, 255)
connection (BrightRegion, Parts)
select_shape (Parts, Parts, 'area', 'and', 40, 10000000)
count_obj (Parts, Number)
dev_display (Image)
* 三种风格轮流套到相邻的孔上，一张图直接对比
Mode := 0
for i := 1 to Number by 1
    select_obj (Parts, One, i)
    if (Mode == 0)
        * 实心填充：一眼看清「有多少、大概多大」，但会盖住底下的图
        dev_set_draw ('fill')
        dev_set_shape ('original')
        dev_set_color ('green')
    elseif (Mode == 1)
        * 只描轮廓：看清「真实形状」，且不遮挡图像细节
        dev_set_draw ('margin')
        dev_set_line_width (2)
        dev_set_shape ('original')
        dev_set_color ('yellow')
    else
        * 换成最小外接矩形：这就是给机器人/下游用的「框」
        dev_set_draw ('margin')
        dev_set_line_width (1)
        dev_set_shape ('rectangle2')
        dev_set_color ('red')
    endif
    dev_display (One)
    Mode := Mode + 1
    if (Mode > 2)
        Mode := 0
    endif
endfor
* 收尾复原默认状态——显示状态是「窗口级全局变量」，不改回去会污染后续脚本
dev_set_shape ('original')
dev_set_draw ('fill')
dev_set_line_width (1)
dev_disp_text ('绿=填充  黄=轮廓  红=外接框  共' + Number$'.0f' + '个', 'window', 12, 12, 'black', ['box','box_color'], ['true','white'])"),

            new TemplateDef("L 效果图标注", "文字与数据标注（dev_disp_text 七参全解）",
@"* ════════════════════════════════════════════════
* 例｜把测量值直接印在图上：工程师看截图就等于看设备屏幕
* dev_disp_text 七个参数：
*   ①Text        文字。可以是「数组」= 多行，行距自动，比写 N 次调用省事
*   ②CoordSystem 'window'=钉在窗口上(随缩放不动) / 'image'=钉在像素上(跟着图放大)
*   ③Row ④Column 数字坐标；当②='window'时还能直接写方位词，九宫格一行搞定：
*        'top'/'center'/'bottom'  ×  'left'/'center'/'right'
*   ⑤Color       字色；给数组则「逐行循环配色」
*   ⑥GenParamName ⑦GenParamValue 外观开关，成对写。23.05 可用：
*        'box' 'box_color' 'box_shape' 'box_padding' 'border_radius'
*        'shadow' 'shadow_color' 'shadow_sigma' 'shadow_dx' 'shadow_dy'
* 坑：①字号是「窗口级」状态，不在七个参数里。想放大得先另起两行：
*       dev_get_window (Window)
*       set_display_font (Window, 24, 'sans', 'false', 'false')
*     ②不用自己收尾还原：宿主每轮执行前会把字体复位回原生。但别拿 Size=-1 当还原——
*       官方过程里 -1 就是 16 号，而画布原生是 default-Normal-12，写 -1 只会越还原越大
*     ③⑦ 这一对里实测只有 'box' / 'box_color' 真落墨，而且 'box' 默认就是开的：
*       同一行 12 号字，默认 2500 px，写 ['box'],['false'] 关框后只剩 428 px。
*       所以再写 ['box'],['true'] 纯属冗余；'shadow' 一个像素都不加（428 → 428）；
*       'font' / 'size' 更是直接让算子失败——字号没有「单次调用」这条路，只能走②上面那个
*     ④文字和图像叠在同一个窗口，深色底要么用白字要么给 'box_color' 配个亮色，否则看不见
*     ⑤disp_message / disp_text 会被插件改写成 dev_disp_text，且多余参数会被截掉，
*       所以本例统一走 dev_disp_text 全参写法，行为最可控
* ════════════════════════════════════════════════
read_image (Image, 'pellets')
rgb1_to_gray (Image, GrayImage)
threshold (GrayImage, Region, 100, 255)
connection (Region, AllParts)
* 不卡面积下限会把 1 像素的灰尘也数进去：实测 153 个 → 卡 50 以后 14 个
select_shape (AllParts, Parts, 'area', 'and', 50, 10000000)
count_obj (Parts, Number)
area_center (Parts, Area, Rows, Cols)
tuple_sum (Area, TotalArea)
dev_display (Image)
* ① 左上角标题：带底色方框，任何背景都读得清
dev_disp_text ('零件计数', 'window', 12, 12, 'black', ['box','box_color'], ['true','yellow'])
* ② 九宫格：右下角盖「结论章」，位置永远不挡产品
if (Number > 0)
    Verdict := 'OK'
else
    Verdict := 'NO PART'
endif
dev_disp_text (Verdict, 'window', 'bottom', 'right', 'green', ['box','shadow'], ['true','true'])
* ③ 多行数组 + 中上：一次调用排好一屏统计
Stats := ['数量: ' + Number$'.0f', '总面积: ' + TotalArea$'.0f', '阈值: 100..255']
dev_disp_text (Stats, 'window', 'top', 'center', 'blue', ['box','box_color'], ['true','white'])
* ④ 'image' 坐标系：文字钉死在第 1 个零件的质心上，放大图时它跟着走
if (Number > 0)
    dev_set_color ('red')
    dev_set_line_width (2)
    gen_circle (Mark, Rows[0], Cols[0], 12)
    dev_display (Mark)
    dev_disp_text ('#1', 'image', Rows[0] - 20, Cols[0], 'red', [], [])
endif"),

            new TemplateDef("L 效果图标注", "几何标注（十字/圆/框/折线/直线）",
@"* ════════════════════════════════════════════════
* 例｜五种「图形标注」一次配齐：点、圆、正框、斜框、轨迹
* 关键认知：新后端不再提供 disp_circle / disp_rectangle1 / disp_arrow 这类
*   「直接画到窗口」的老算子——统一拆成两步：
*     第1步 gen_xxx 造出一个对象（region 或 xld-contour）
*     第2步 dev_display 把它显示出来
*   好处：对象可以留着复用、可以存盘、可以参与后续运算
* 五种对象：
*   gen_cross_contour_xld  十字(XLD)  —— 标记中心点、抓取点
*   gen_circle             圆(region) —— 标记半径、覆盖范围
*   gen_rectangle1         正框(region)—— 轴对齐外接框，报坐标最直观
*   gen_rectangle2         斜框(region)—— 带角度的框，对应机器人吸盘姿态
*   gen_contour_polygon_xld 折线(XLD) —— 涂胶轨迹、走线路径、轮廓比对
*   gen_region_line        直线(region)—— 两点一线，画基准边
* 坑：①XLD 线宽靠 dev_set_line_width，region 轮廓靠 dev_set_line_width+dev_set_draw('margin')
*     ②dev_set_contour_style 只影响 XLD：'stroke'细线 / 'fill'填充闭合轮廓 /
*       'stroke_and_fill'。对不闭合的折线设 'fill' 是什么都画不出来的
*     ③HALCON 坐标一律 (行, 列) = (Y, X)，别写反
* ════════════════════════════════════════════════
read_image (Image, 'clip')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
CenterRow := 0.5 * Height
CenterCol := 0.5 * Width
dev_display (Image)
* ① 十字：定位点（匹配中心、标定板原点都用它）
gen_cross_contour_xld (Cross, CenterRow, CenterCol, 28, rad(45))
dev_set_color ('green')
dev_set_line_width (2)
dev_display (Cross)
* ② 圆：region 型，用 margin 画出来就是一个圆圈
gen_circle (Circle, CenterRow, CenterCol, 70)
dev_set_draw ('margin')
dev_set_color ('cyan')
dev_set_line_width (2)
dev_display (Circle)
* ③ 轴对齐外接框
gen_rectangle1 (Rect1, CenterRow - 60, CenterCol - 80, CenterRow + 60, CenterCol + 80)
dev_set_color ('yellow')
dev_set_line_width (1)
dev_display (Rect1)
* ④ 旋转框：中心 + 长轴方向 + 两个半轴（机器人抓取框就是这个）
gen_rectangle2 (Rect2, CenterRow, CenterCol, rad(30), 95, 50)
dev_set_color ('red')
dev_display (Rect2)
* ⑤ 折线：任意多点轨迹，两个等长数组分别是行、列
PathRows := [100, 160, 130, 220, 300, 340]
PathCols := [80, 140, 220, 280, 250, 340]
gen_contour_polygon_xld (Path, PathRows, PathCols)
dev_set_color ('orange')
dev_set_line_width (3)
dev_display (Path)
* ⑥ 闭合多边形 + 填充：高亮一块「关注区」，盖半层色不影响看图
gen_contour_polygon_xld (Poly, [420,420,520,520], [520,640,640,520])
dev_set_contour_style ('fill')
dev_display (Poly)
* 收尾复原（显示状态是窗口级全局的，必须还原）
dev_set_contour_style ('stroke')
dev_set_draw ('fill')
dev_set_line_width (1)
dev_set_color ('white')"),

            new TemplateDef("L 效果图标注", "伪彩与热力图（dev_set_lut）",
@"* ════════════════════════════════════════════════
* 例｜灰度转伪彩：人眼分不出的 3 个灰阶，换成颜色一眼就看出来
* 比方：天气预报的雨量图——数值画成黑白根本看不出边界，
*       涂成蓝→绿→黄→红，0.1mm 的差别都能从图上跳出来
* dev_set_lut 在 23.05 的全部合法值（写别的运行期直接报错）：
*   灰度曲线类：default linear inverse sqr inv_sqr sqrt inv_sqrt
*               cube inv_cube cubic_root inv_cubic_root cyclic_gray
*   分档类    ：three six twelve twenty_four color1 color2 color3 color4
*   伪彩类    ：rainbow jet jet_inverse temperature cyclic_temperature
*               change1 change2 change3 hsi
*   ⚠ 是 'rainbow' 不是 'rain_bow'；老书上的 'heat'、'hot_cold' 在 23.05 已无
* 坑：①LUT 只改「显示」不改像素！threshold 吃的还是原灰度值，
*       千万别以为图上变红了数据就变了（要改数据用 scale_image /emphasize）
*     ②直方图均衡会把噪声一起放大，官方文档原话：可能看出「假边缘」，
*       所以它只用于「给人看」，不用于「给算法算」
*     ③dev_set_paint 在 23.05 只剩 3D/矢量场用途('3d_plot','vector_field')，
*       2D 检测图上没有可用取值，不要照着老教程写
* ════════════════════════════════════════════════
read_image (Image, 'meningg5')
rgb1_to_gray (Image, GrayImage)
* 这张图中间亮四周暗，「比周围暗一点」的斑在纯灰度下几乎看不见
* 实测灰度只挤在 112..150 这 38 级里（正常图是 0..255）——等于全图蒙了层灰纱
* 全局直方图均衡：把这 38 级拉开到 256 级（正名 equ_histo_image，23.05 没有 equ_histogram）
equ_histo_image (GrayImage, Equalized)
* 上伪彩：这一步之后 dev_display 出来的图就是彩色的
dev_set_lut ('jet')
dev_display (Equalized)
* 伪彩底 + 白描边，是人眼最好读的组合
threshold (Equalized, DarkSpots, 0, 12)
connection (DarkSpots, Spots)
select_shape (Spots, BigSpots, 'area', 'and', 60, 10000000)
count_obj (BigSpots, Number)
dev_set_draw ('margin')
dev_set_line_width (2)
dev_set_color ('white')
dev_display (BigSpots)
dev_disp_text (['LUT: jet', '异常暗斑: ' + Number$'.0f'], 'window', 12, 12, 'black', ['box','box_color'], ['true','yellow'])
* 收尾还原：LUT 不动数据，但会污染下一个脚本的显示
dev_set_lut ('default')"),

            new TemplateDef("L 效果图标注", "局部放大（dev_set_part 放大镜）",
@"* ════════════════════════════════════════════════
* 例｜放大镜：把窗口可视范围缩到一小块，3 像素的缺陷直接占满屏幕
* 比方：手机相册看照片双指一撑——像素没变，变的是「显示区域」
* dev_set_part (Row1, Column1, Row2, Column2)：
*   之后所有 dev_display 只画这个矩形内的内容，并拉满整个窗口 = 放大
*   坐标仍是「原图坐标」，叠加的区域/轮廓不用换算，HALCON 自动对齐
*   还原全图：dev_set_part (0, 0, Height-1, Width-1)
* 和 crop_part 的区别（面试常问）：
*   dev_set_part 只是「看着大」，数据没动，适合给人看
*   crop_part    是真挖出一张小图，能单独存盘、单独再跑一遍算法
* 坑：①视野矩形必须落在图内，越界运行期报错 → 用 max2/min2 把四个边夹住
*     ②线宽是「屏幕像素」单位，放大后不会跟着变粗，所以放大视图常要调细线宽
*     ③crop_part 参数顺序是 行,列,宽,高（宽在前！和 gen_rectangle1 的 行1列1行2列2 不一样）
* ════════════════════════════════════════════════
read_image (Image, 'bga_14x14_defects')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* ─── 先把缺陷找完，再决定放大镜放哪 ───
* ⚠ 本图极性反直觉：板面是亮的，缺陷是「更亮的点」→ 切高段 200..255（实测 10 个，每个 10~16 像素）
* ⚠ 面积必须给上限：只写下限的话，整块板面会被当成一个 12 万像素的「超大缺陷」
threshold (GrayImage, BrightRegion, 200, 255)
connection (BrightRegion, Spots)
select_shape (Spots, Defects, 'area', 'and', 10, 3000)
count_obj (Defects, Number)
area_center (Defects, Area, Rows, Cols)
dev_display (Image)
if (Number == 0)
    dev_disp_text ('未发现缺陷', 'window', 'top', 'center', 'green', ['box','box_color'], ['true','white'])
else
    * 挑面积最大的那颗：23.05 没有 max_index，用排序下标取最后一个
    tuple_sort_index (Area, Idx)
    Worst := Idx[|Idx| - 1]
    select_obj (Defects, One, Worst + 1)
    smallest_rectangle1 (One, R1, C1, R2, C2)
    * 以缺陷为中心外扩 40 像素，并用 max2/min2 夹进图像范围内
    Pad := 40
    ZR1 := max2(R1 - Pad, 0)
    ZC1 := max2(C1 - Pad, 0)
    ZR2 := min2(R2 + Pad, Height - 1)
    ZC2 := min2(C2 + Pad, Width - 1)
    dev_set_part (ZR1, ZC1, ZR2, ZC2)
    dev_display (Image)
    * 同一批坐标直接叠，位置自动对得上
    dev_set_draw ('margin')
    dev_set_line_width (1)
    dev_set_color ('red')
    dev_display (One)
    Box := ['视野: ' + ZR1$'.0f' + ',' + ZC1$'.0f' + ' ~ ' + ZR2$'.0f' + ',' + ZC2$'.0f', '缺陷面积: ' + Area[Worst]$'.0f' + ' px']
    dev_disp_text (Box, 'window', 12, 12, 'black', ['box','box_color'], ['true','white'])
    * 想「真挖出来」另存或再算一遍就用 crop_part：行,列,宽,高
    crop_part (GrayImage, ZoomCrop, ZR1, ZC1, ZC2 - ZC1 + 1, ZR2 - ZR1 + 1)
    dev_set_part (0, 0, Height - 1, Width - 1)
endif"),

            new TemplateDef("L 效果图标注", "综合检测报告页（一屏出图即出报告）",
@"* ════════════════════════════════════════════════
* 例｜把前几招拼成一张能直接发群里的效果图
* 版面分层（和画 HMI 画面是同一套思路）：
*   底层  原图
*   第1层 缺陷实心红   —— 一眼看到「哪里不行」
*   第2层 缺陷外接框   —— 给下游/机器人读的坐标框（同一批区域再画一遍）
*   第3层 左上标题栏   —— 带底色，任何背景都读得清
*   第4层 右下 OK/NG 章 —— 九宫格定位，永远不挡产品
* 一条铁律：dev_* 只负责「画」，所有判定（threshold / select_shape / 计数）
*   必须在画之前就全部算完 —— 效果图是结果的照片，不是算法的一部分
* ════════════════════════════════════════════════
read_image (Image, 'bga_14x14_defects')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* ─── 算法段：先算完 ───
* 极性与面积上限同「局部放大」那条：缺陷是亮点，且必须卡上限甩掉整块板面
threshold (GrayImage, BrightRegion, 200, 255)
connection (BrightRegion, All)
select_shape (All, Defects, 'area', 'and', 10, 3000)
count_obj (Defects, Number)
area_center (Defects, Area, Rows, Cols)
if (Number > 0)
    tuple_max (Area, MaxArea)
    Verdict := 'NG'
else
    MaxArea := 0
    Verdict := 'OK'
endif
* ─── 绘制段：分层往上叠 ───
dev_display (Image)
* 第1层 实心
dev_set_draw ('fill')
dev_set_color ('red')
dev_display (Defects)
* 第2层 同一批区域换画法再叠一遍：旋转外接框
dev_set_draw ('margin')
dev_set_line_width (1)
dev_set_color ('yellow')
dev_set_shape ('rectangle2')
dev_display (Defects)
dev_set_shape ('original')
* 第3层 左上标题栏
Header := ['BGA 焊球检测', '缺陷数: ' + Number$'.0f', '最大缺陷: ' + MaxArea$'.0f' + ' px']
dev_disp_text (Header, 'window', 12, 12, 'black', ['box_color'], ['white'])
* 第4层 右下 OK/NG 章（红/绿分色，扫一眼就知道结果）
* 'box' 默认就是开的，这里只改框色；实测 'shadow' 一个像素都不加，所以不写
if (Number > 0)
    dev_disp_text (Verdict, 'window', 'bottom', 'right', 'red', ['box_color'], ['white'])
else
    dev_disp_text (Verdict, 'window', 'bottom', 'right', 'green', ['box_color'], ['white'])
endif
* 还原默认显示状态，别把它带给下一个脚本（字号不用管，宿主每轮自己复位）
dev_set_draw ('fill')
dev_set_line_width (1)
dev_set_color ('white')"),

            new TemplateDef("L 效果图标注", "大字号箭头标注（指着最大缺陷说话）",
@"* ════════════════════════════════════════════════
* 例｜给操作员看的「就是这里」：粗箭头指到最大缺陷 + 大字号说明
* 比方：老师批卷不会只写个「错」字，她会画个箭头指到你算错的那一步——
*       箭头的作用是代替说话，让人不用自己找
* 两个主角：
*   gen_arrow_contour_xld  只生成箭头的顶点（纯几何，不落笔）
*   dev_display            才真正把箭头画到画布上
* 箭头七个参数（1 输出 + 6 输入）：尾部(Row1,Col1) → 头部(Row2,Col2)、两翼长、两翼宽
*   多写一个参数就报 invalid program line，这是最常见的抄错
* ════════════════════════════════════════════════
read_image (Image, 'bga_14x14_defects')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* ─── 算法段：先把缺陷找完并算出最大那个，再开始画 ───
threshold (GrayImage, BrightRegion, 200, 255)
connection (BrightRegion, All)
select_shape (All, Defects, 'area', 'and', 10, 3000)
count_obj (Defects, Number)
area_center (Defects, Area, Rows, Cols)
tuple_max (Area, MaxArea)
* tuple_find 给的是 0 基数组下标，select_obj 从 1 数，所以 +1
tuple_find (Area, MaxArea, Hit)
Idx := Hit[0] + 1
* ─── 显示段 ───
dev_display (Image)
dev_set_draw ('margin')
dev_set_color ('red')
dev_display (Defects)
* 字号是「窗口级」状态，不在 dev_disp_text 的参数里：先拿句柄，再设字号
dev_get_window (Window)
set_display_font (Window, 26, 'sans', 'false', 'false')
if (Number > 0)
    select_obj (Defects, Worst, Idx)
    area_center (Worst, WorstArea, WorstRow, WorstCol)
    dev_disp_text ('最大缺陷 ' + WorstArea$'.0f' + ' px', 'window', 'top', 'left', 'red', [], [])
    * 箭头从右下角（空处）斜着指到缺陷质心，起点用比例算、换图不用改
    dev_set_color ('yellow')
    dev_set_line_width (3)
    gen_arrow_contour_xld (Arrow, Height * 0.88, Width * 0.9, WorstRow, WorstCol, 45, 28)
    dev_display (Arrow)
else
    dev_disp_text ('无缺陷', 'window', 'top', 'left', 'green', [], [])
endif
* ─── 第 3 段：另一条输出路——publish_preview 直送指定视图 ───
* dev_display 那一套最后会被整张回读成「效果图」发到窗口1；
* publish_preview 是【改道】：这张图不进画布、不参与效果图，直接送到第 N 号视图。
* 两路并存，同一个脚本就能一屏往多个窗口各推一张不同的图。
publish_preview (Image, 2)
*   ↑ 图像：原样直送窗口2，跟画布/底图无关，也不受 dev_set_part 影响
publish_preview (GrayImage, 3)
*   ↑ 还是图像。这一路目前【只能送图像】：区域 / 轮廓送过去会被静默丢掉（窗口不刷新、不报错），
*     想单独看缺陷分布请走画布那条路（dev_set_color + dev_display），或者直接把它 crop/裁剪成图像再送
* ─── 关于字号还原：不用自己写 ───
* 字号是「窗口级」状态，画布窗口又按线程常驻复用，本来会串到下一个脚本。
* 宿主在每次执行前都会把字体复位回原生，所以脚本里只管「要多大就显式设多大」。
* 顺带一个看着合理的陷阱：set_display_font 的 Size 写 -1 不等于还原——
* 官方过程里 -1 就是 16 号，而画布原生是 default-Normal-12。
dev_set_color ('white')
dev_set_line_width (1)
dev_set_draw ('fill')"),
        };
    }
}
