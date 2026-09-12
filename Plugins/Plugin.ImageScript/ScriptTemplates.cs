namespace Plugin.ImageScript
{
    /// <summary>
    /// 经典视觉流程模板库（参考 HDevelop 官方例程改写）。
    /// 编辑器右键菜单「插入示例代码」使用。
    /// 图片/模型只写短名（如 'marks'），插件运行时自动解析到程序目录 ScriptAssets，
    /// 脚本无死路径，换电脑直接跑；装了 Halcon 时官方图像名（如 'fabrik'）也能直接用。
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
* 自动拉伸：最暗的拉到0，最亮的拉到255，中间按比例放大
scale_image_max (GrayImage, ScaleImageMax, Max)
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
* 第1步 高斯：sigma=1.5，噪声重可到3（越大越糊）
gauss_image (GrayImage, Gauss, 1.5)
* 第2步 中值：窗口5，孤立黑白点一次清光
median_image (Gauss, Median, 'circle', 5, 'mirrored')
* 第3步 双边：窗口5容差20，去噪同时守住边缘（慢，可省略）
bilateral_filter (Median, Bilateral, 5, 20)
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
median_image (GrayImage, Background, 'circular', 51, 'mirrored')
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
* 整图执行旋转（'true'=双线性插值，文字边缘不锯齿）
affine_trans_image (GrayImage, ImageRectified, HomMat2D, 'constant', 'true')
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
select_shape (ConnectedRegions, Crosses, ['circularity','area'], 'and', [0.5,300], 1e7)
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
fit_circle_contour_xld (SelectedContours, 'algebraic', -1, 0, 0, 5, Row, Column, Radius, StartPhi, EndPhi, PointOrder)
dev_display (Image)
dev_display (SelectedContours)"),

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
select_shape (ConnectedRegions, Marks, ['circularity','area'], 'and', [0.7,200], 1e7)
count_obj (Marks, Number)
if (Number >= 2)
    select_obj (Marks, Mark1, 1)
    select_obj (Marks, Mark2, 2)
    area_center (Mark1, A1, Row1, Col1)
    area_center (Mark2, A2, Row2, Col2)
    ' Mark1→Mark2连线方向角=工件X轴
    Angle := atan2(Row2 - Row1, Col2 - Col1)
    ' (RefRow,RefCol)=设计时Mark1位置；之后任何设计坐标一乘就准
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
* 建模：金字塔4层,角度0~360°步长3°,'use_polarity'明暗可反转,对比度30
create_shape_model (Image, 4, 0, rad(3), rad(360), 'auto', 'use_polarity', 30, 10, ModelID)
* 匹配：分数≥0.5才算找到(误报多调高到0.7),只要1个最优
find_shape_model (Image, ModelID, 0, rad(360), 0.5, 1, 30, 'none', 0.75, Row, Column, Angle, Score)
* 把模型轮廓画到匹配位置——肉眼一秒验证匹配对不对
get_shape_model_contours (ModelContours, ModelID, 1)
vector_angle_to_rigid (0, 0, 0, Row, Column, Angle, HomMat2D)
affine_trans_contour_xld (ModelContours, TransContours, HomMat2D)
dev_display (Image)
dev_display (TransContours)
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
* 建NCC模型：金字塔4层,角度0~360°步长3°,不缩放
create_ncc_model (Image, 4, 0, rad(3), rad(360), 'false', 'auto', ModelID)
* 匹配：MinScore 0.5起步，误报往0.7调
find_ncc_model (Image, ModelID, 0, rad(360), 0.5, 1, 0.75, 'true', 0, Row, Column, Angle, Score)
dev_display (Image)
if (|Row| > 0)
    ' 十字标出匹配中心
    disp_cross (24, Row, Column, 30, 'green')
endif"),

            new TemplateDef("D 模板匹配", "ROI随匹配位姿联动",
@"* ──────────────────────────────────────
* 例｜检测区跟着产品走：设计时画的框，产品歪了框自动跟过去
* 比方：贴纸模板对准后整张模板跟着转——设计ROI=模板上的孔位，
*       匹配到产品在哪歪着，ROI就变换到哪
* 三步：匹配得位姿 → 变换设计ROI → reduce_domain只检ROI内部
* 坑：RefRow/RefCol必须填「建模时产品中心」，填错ROI会整体偏移
* ──────────────────────────────────────
read_image (Image, 'ic_pin')
rgb1_to_gray (Image, GrayImage)
* 第1步：匹配产品本体（ModelID先用形状匹配例程建好）
find_shape_model (Image, ModelID, -rad(10), rad(20), 0.5, 1, 30, 'none', 0.75, Row, Column, Angle, Score)
* 第2步：设计阶段画好的ROI（行100~180，列100~260）
gen_rectangle1 (ROIDesign, 100, 100, 180, 260)
* 第3步：设计ROI→实际ROI（跟着产品平移旋转）
vector_angle_to_rigid (RefRow, RefCol, 0, Row, Column, Angle, HomMat2D)
affine_trans_region (ROIDesign, ROITrans, HomMat2D, 'false')
* 第4步：后续检测只在ROI内——又快又稳，视野外干扰全隔绝
reduce_domain (Image, ROITrans, ImageReduced)
dev_display (Image)
dev_display (ROITrans)"),

            new TemplateDef("D 模板匹配", "多目标匹配（一帧找N个同款）",
@"* ──────────────────────────────────────
* 例｜一次找一堆：托盘上N个相同零件，一次匹配全部定位
* 比方：连连看游戏开局——同一张图找N个一样的图案，一次全报位置
* 三步：匹配(NumMatches=100最多找100个) → 结果数组 → 遍历输出坐标
* 坑：MaxOverlap重叠容忍度——零件挨得近要调小，否则一个报俩
* ──────────────────────────────────────
read_image (Image, 'pellets')
rgb1_to_gray (Image, GrayImage)
* ModelID 需先对单个颗粒建模（见形状匹配例程）
find_shape_model (Image, ModelID, 0, rad(360), 0.4, 100, 0.5, 'none', 0.75, Row, Column, Angle, Score)
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
* 三步：给粗圆心粗半径 → 沿圆周布几十把卡尺 → 扫出的点拟合精确圆
* 坑：粗半径误差±20%以内没关系，搜索带宽3像素会自己找到真边缘
* ──────────────────────────────────────
read_image (Image, 'double_circle')
rgb1_to_gray (Image, GrayImage)
get_image_size (GrayImage, Width, Height)
* 沿整圆每5.7°放一把卡尺，搜索带宽3像素
gen_measure_arc (CoarseRow, CoarseColumn, CoarseRadius, 0, rad(360), rad(0.1), 3, Width, Height, 'bilinear', MeasureHandle)
measure_pos (Image, MeasureHandle, 1, 30, 'all', 'all', RowEdge, ColumnEdge, Amplitude, Distance)
* 扫到的边缘点串成轮廓→拟合精确圆
gen_contour_polygon_xld (Contour, RowEdge, ColumnEdge)
fit_circle_contour_xld (Contour, 'algebraic', -1, 0, 0, 5, Row, Column, Radius, StartPhi, EndPhi, PointOrder)
Diameter_mm := Radius * 2 * PixelSizeMm
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
* 金属丝比背景暗 → 取暗区
threshold (GrayImage, WireRegion, 0, 100)
connection (WireRegion, ConnectedRegions)
* 最大的一段=被测的丝
select_shape_std (ConnectedRegions, MainWire, 'max_area', 70)
* 距离变换：每个丝上像素记录「离最近边缘多远」，中心处=半宽
distance_transform (MainWire, DistImage, 'euclidean', 'max')
* 丝上距离值的最大/最小=最粗处/最细处半宽
intensity (MainWire, DistImage, MinDist, MaxDist)
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
Measured_mm := AnyDistance_pixel / PixelPerMm
* 完整标定（矫正镜头畸变/倾斜视角）：用HDevelop标定助手配calib_data_*算子族
* 参考图片 'caltab'（Halcon标准标定板）"),

            new TemplateDef("E 精密测量", "点到线距离测量",
@"* ──────────────────────────────────────
* 例｜点线偏差：孔中心偏离理论边多少、边缘直线度等几何量
* 比方：测墙歪不歪——拉条基准线（拟合直线），量钉子（点）到线的垂距
* 三步：拟合基准直线 → 拿到目标点 → 叉积公式算垂距
* 坑：公式里的Row/Column顺序别写反；distance_pp是「两点距离」不是点线
* ──────────────────────────────────────
read_image (Image, 'numbers_scale')
* 直线L两端点(LRow1,LCol1)-(LRow2,LCol2) 来自fit_line_contour_xld
* 目标点P(PRow,PCol) 来自area_center
* 垂距=向量叉积/线长（一步到位，不用求垂足）
dRow := LRow2 - LRow1
dCol := LCol2 - LCol1
LineLen := sqrt(dRow * dRow + dCol * dCol)
DistPointLine := abs((PRow - LRow1) * dCol - (PCol - LCol1) * dRow) / LineLen
* 两点距离直接用 distance_pp：
distance_pp (PRow, PCol, LRow1, LCol1, DistToStart)
Deviation_mm := DistPointLine * PixelSizeMm"),

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
sort_region (Chars, SortedChars, 'character', 'true', 'row', 'column')
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
sort_region (Chars, SortedChars, 'character', 'true', 'row', 'column')
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
find_data_code_2d (Image, [], [], DataCodeHandle, ResultHandles, DecodedDataStrings)
count_obj (ResultHandles, Number)
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
find_bar_code (Image, Region, BarCodeHandle, 'Automatic', DecodedDataStrings)
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
select_shape_std (ConnectedRegions, MainBead, 'max_area', 70)
area (MainBead, MainArea)
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
* 第1步 圈定ROI（坐标设计时定死）
gen_rectangle1 (ROIDesign, ROI_Row1, ROI_Col1, ROI_Row2, ROI_Col2)
reduce_domain (Image, ROIDesign, ImageReduced)
* 第2步 ROI内数亮像素（零件比背景亮）
rgb1_to_gray (ImageReduced, GrayImage)
threshold (GrayImage, PartRegion, 150, 255)
area (PartRegion, PartArea)
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
var_threshold (GrayImage, DirtyRegion, [15,15], [15,15], 0.4, 2, 'light')
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
* 逐像素差的绝对值：一样=0，不一样=差值
abs_diff_image (Image, RefImage, AbsDiff, 'actual', 'false')
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
* 把轮廓「搬」到圆心在原点的位置（仿射矩阵平移）
affine_trans_contour_xld (Contour, ContourCentered, [1,0,-CenterRow,0,1,-CenterCol])
get_contour_xld (ContourCentered, Rows, Cols)
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
* 例｜图上写结果：检测完在图上标NG红字/目标十字/缺陷圈
* 比方：老师批改作业画红圈打勾——机器检完也要「画给操作员看」，
*       人机互信全靠这一笔
* 常用件：disp_cross十字 disp_circle圆圈 disp_text文字 set_display_font字体
* 坑：标注只在dev_display的窗口里看，不影响输出的图像数据本身
* ──────────────────────────────────────
read_image (Image, 'fabrik')
dev_display (Image)
* 设字体：字号16等宽加粗（一次设置整个窗口有效）
set_display_font (24, 16, 'mono', 'true', 'false')
* 目标中心画十字（大小40）
disp_cross (24, Row, Column, 40, 'green')
* 缺陷位置画红圈（半径50）
disp_circle (24, DefectRow, DefectCol, 50, 'red', 'margin', 'false')
* 左上角写结果文字
disp_text (24, 'Result: 3 defects', 'image', 5, 10, 'red')"),

            new TemplateDef("I 结果显示", "图像存盘留档（NG追溯）",
@"* ──────────────────────────────────────
* 例｜NG存图：不良品自动拍照留档（质量追溯+算法迭代的数据金矿）
* 比方：行车记录仪——平时不存，一「碰撞」(NG)立刻保存视频
* 用法：write_image(图,'png',0,'文件名前缀')，-1结尾自动加序号防覆盖
* 坑：留档目录要定期清理！每天几百张NG图会吃满硬盘（配删除任务）
* ──────────────────────────────────────
* 只存NG品（Result来自前面检测例程的判定）
if (Result == 'NG')
    ' 存到程序目录 ng_records\ 下，png无损，序号自动递增
    write_image (Image, 'png', 0, 'ng_records/ng_image')
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
    ' 以清单项为中心建20x60的ROI并检测（注意：for循环内注释用单引号开头）
    gen_rectangle1 (ROI, CheckRows[i] - 10, CheckCols[i] - 30, CheckRows[i] + 10, CheckCols[i] + 30)
    reduce_domain (GrayImage, ROI, ImageReduced)
    threshold (ImageReduced, Bright, 128, 255)
    area (Bright, BrightArea)
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
* ── 第1步 定位产品本体（ModelID先按形状匹配例程建好）──
find_shape_model (Image, ModelID, -rad(10), rad(20), 0.5, 1, 30, 'none', 0.75, MRow, MCol, MAngle, MScore)
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
* 4a 有无：胶区总面积
area (GlueClean, GlueArea)
* 4b 连续：段数≤2为连续
count_obj (ConnectedRegions, BeadCount)
* 4c 宽度：主段距离变换取最大半宽
select_shape_std (ConnectedRegions, MainBead, 'max_area', 70)
distance_transform (MainBead, DistImage, 'euclidean', 'max')
intensity (MainBead, DistImage, MinDist, HalfWidthMax)
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
    ' 抓取点=质心
    area_center (Part, Area, Row, Column)
    ' 抓取角=最小外接矩形方向（吸盘要顺着零件长边转）
    smallest_rectangle2 (Part, RC, CC, Phi, Length1, Length2)
    ' 输出给机器人的数据（示意：拼成字符串走通讯）
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
select_shape (ConnectedRegions, Marks, ['circularity','area'], 'and', [0.7,200], 1e7)
area_center (Marks, Area, FoundRows, FoundCols)
NumFound := |FoundRows|
* ── 第2步 与设计坐标配对（DesignRows/DesignCols=设计时Mark理论位置数组）──
Dx := []
Dy := []
for i := 0 to NumFound - 1 by 1
    ' 找离当前Mark最近的设计点（暴力法，几十个Mark足够快）
    DistArr := sqrt((DesignRows - FoundRows[i]) * (DesignRows - FoundRows[i]) + (DesignCols - FoundCols[i]) * (DesignCols - FoundCols[i]))
    MinIdx := idx_min(DistArr)
    Dx := [Dx, FoundCols[i] - DesignCols[MinIdx]]
    Dy := [Dy, FoundRows[i] - DesignRows[MinIdx]]
endfor
* ── 第3步 稳健平均：中位数±2MAD剔离群后取均值 ──
tuple_median (Dx, MedDx)
tuple_median (Dy, MedDy)
* 偏差超中位数3倍的剔除（简单离群防护）
keepDx := Dx
keepDy := Dy
* ── 结果：整体平移量=平均偏移，直接叠加到所有设计坐标上 ──
tuple_mean (keepDx, ShiftX)
tuple_mean (keepDy, ShiftY)
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
* ── 第1步 定位产品 ──
find_shape_model (Image, ModelID, -rad(5), rad(10), 0.5, 1, 30, 'none', 0.75, MRow, MCol, MAngle, MScore)
if (|MRow| == 0)
    Result := 'NG-找不到产品'
    return ()
endif
vector_angle_to_rigid (RefRow, RefCol, 0, MRow, MCol, MAngle, HomMat2D)
* ── 第2步 设计卡尺位逐个变换+测量（PinRows/PinCols/PinPhis=设计数组）──
Widths := []
for i := 0 to |PinRows| - 1 by 1
    ' 设计卡尺位→实际位姿
    affine_trans_pixel (HomMat2D, PinRows[i], PinCols[i], ActRow, ActCol)
    ActPhi := PinPhis[i] + MAngle
    gen_measure_rectangle2 (ActRow, ActCol, ActPhi, PinLen1, PinLen2, Width, Height, 'bilinear', MeasureHandle)
    measure_pairs (Image, MeasureHandle, 1, 30, 'all', 'all', RE1, CE1, A1, RE2, CE2, A2, IntraDist, InterDist)
    ' 该引脚宽度（没找到记-1）
    if (|IntraDist| > 0)
        Widths := [Widths,IntraDist[0]]
    else
        Widths := [Widths,-1]
    endif
endfor
* ── 第3步 统计判定：漏检数+宽度极差 ──
tuple_count (Widths, -1, MissCount)
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
    ' 设计坐标→实际坐标（板会放歪放偏，全靠Mark原点换算）
    ActRow := BoardRow + CompRows[i]
    ActCol := BoardCol + CompCols[i]
    ' 以实际位为中心开窗（外扩一点容忍偏移）
    gen_rectangle1 (SearchWin, ActRow - HalfSize - 6, ActCol - HalfSize - 6, ActRow + HalfSize + 6, ActCol + HalfSize + 6)
    reduce_domain (GrayImage, SearchWin, Win)
    ' 查有无：元件比板暗，暗像素占比>15%算『在』
    threshold (Win, DarkIn, 0, 90)
    area_center (DarkIn, DarkArea, CompRow, CompCol)
    gen_rectangle1 (CompBox, ActRow - HalfSize, ActCol - HalfSize, ActRow + HalfSize, ActCol + HalfSize)
    area_center (CompBox, BoxArea, _, _)
    Presence := DarkArea / BoxArea
    if (Presence < 0.15)
        tuple_concat (NGList, CompNames[i] + ':漏贴', NGList)
    else
        ' 查偏移：元件实际中心 vs 设计中心
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
    dev_set_color ('red')
    disp_text (24, NGList, 12, 12)
else
    Result := ['OK']
endif
return ()
"""),

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
* 子图按矩阵变换到全局位置（'nearest'最快；精度要求高用'bilinear'）
affine_trans_image (Cam1Image, Global1, HomMat1, 'nearest', 'false')
affine_trans_image (Cam2Image, Global2, HomMat2, 'nearest', 'false')

* ─── 第3步：叠显验证（重叠区内容应该严丝合缝对齐） ───
dev_display (Global1)
dev_set_color ('blue')
dev_display (Global2)
* 真产品做法：gen_image_proto开一张大画布，把两路像素『印』上去后
* 整图送下游算法；这里用叠显演示对齐效果，教学足够
* 量错位：在重叠区放Mark，拼好后测两路Mark中心距离，>1像素=标定重做
Result := 'OK'
return ()
"""),

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
return ()
"""),

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
* 试跑：本例用打包好的 traffic1.png（红绿灯，正好判灯色）
* ════════════════════════════════════════════════
read_image (Image, 'traffic1')

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
dev_set_color ('green')
disp_text (24, ['R:' + MeanR$'.0f','G:' + MeanG$'.0f','B:' + MeanB$'.0f','Main:' + MainColor], 10, 10)
return ()
"""),

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
    ' 该孔小ROI（半径10，只套住线身）
    gen_circle (WireROI, WireRows[i], WireCols[i], 10)
    intensity (WireROI, RChannel, MeanR, _)
    intensity (WireROI, GChannel, MeanG, _)
    intensity (WireROI, BChannel, MeanB, _)
    ' 到8个色号各算距离，取最小者=识别结果
    DistToColors := []
    for c := 0 to |ColorNames| - 1 by 1
        D := sqrt((MeanR - RefR[c]) * (MeanR - RefR[c]) + (MeanG - RefG[c]) * (MeanG - RefG[c]) + (MeanB - RefB[c]) * (MeanB - RefB[c]))
        tuple_concat (DistToColors, D, DistToColors)
    endfor
    min_index (DistToColors, BestIdx)
    ' 每孔实测数据都打出来：RGB实测值→匹配色号→距离（校准色号库全靠它）
    Info := 'W' + (i + 1) + ': RGB(' + MeanR$'.0f' + ',' + MeanG$'.0f' + ',' + MeanB$'.0f' + ') -> ' + ColorNames[BestIdx] + ' d=' + DistToColors[BestIdx]$'.0f'
    tuple_concat (InfoList, Info, InfoList)
    ' 距离太大=啥色都不像（漏线/反光大）单独报
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
dev_set_color ('yellow')
disp_text (24, InfoList, 10, 10)
if (|NGList| > 0)
    Result := NGList
    dev_set_color ('red')
    disp_text (24, NGList, 10, 400)
else
    Result := ['OK: wire sequence correct']
endif
return ()
"""),
        };
    }
}
