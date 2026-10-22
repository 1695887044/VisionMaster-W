using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 标定类型。
    /// </summary>
    public enum CalibrationKind
    {
        /// <summary>像素当量：只有 mm/px（没有机械坐标）。适合"只测毫米尺寸"的场景（卡尺/Blob 的当量来源）</summary>
        PixelScale = 0,

        /// <summary>
        /// 网格标定（习惯叫"九点标定"）：N×N 组 (机械坐标 ↔ 图像点) 求出的 2D 仿射变换，
        /// 支持像素 → 机械 / 机械 → 像素 双向换算。**注意它只覆盖标定平面**：
        /// 有高度差、镜头畸变、相机倾斜都会表现为残差（见 ResidualRmsPx / ResidualByRadiusBands）。
        /// </summary>
        NinePoint = 1,

        /// <summary>
        /// 透视标定（投影变换，二期①）：N 组点求出 3×3 投影矩阵，覆盖相机倾斜/大视场下的"近大远小"。
        /// 产出用 <see cref="CalibrationTransform.ProjectiveMatrix"/>；
        /// 旧 <see cref="CalibrationTransform.Matrix"/> 保持零（故意）：未升级的消费者走 6 元 Matrix（det=0）
        /// 会**明确失败**——绝不允许把透视静默近似成仿射。
        /// </summary>
        Perspective = 2,

        /// <summary>
        /// 网格标定（分段仿射，二期②）：N×N 节点（像素 ↔ 机械一一对应）三角化后**逐格仿射**，
        /// 局部吸收径向畸变（工程近似；正解是标定板内参标定）。产出用
        /// <see cref="CalibrationTransform.MeshNodes"/> + <see cref="CalibrationTransform.MeshSize"/>；
        /// <see cref="CalibrationTransform.Matrix"/> 保持零（故意，同透视）：未升级的消费者必须明确失败，
        /// 绝不能把网格静默当单份仿射用（边缘区域会整体错位）。
        /// </summary>
        Mesh = 3,

        /// <summary>
        /// 九点标定 + **内参去畸变**（二期③）：与 <see cref="NinePoint"/> 同一套 6 元矩阵，
        /// 但**像素坐标语义变了**——矩阵映射的是「**去畸变后**的像素」→ 机械坐标。
        ///
        /// 为什么单列一个 Kind（而不是靠 <see cref="CalibrationTransform.Intrinsics"/> 是否为 null 判断）
        /// ---------
        /// 这是**响亮信号**，与 <see cref="Perspective"/> / <see cref="Mesh"/> 让 Matrix 保持零同一用意：
        /// 不知道这个 Kind 的消费者会在自己的 <c>switch</c> 里落到 <c>default:</c> → **明确失败**，
        /// 而不是把畸变像素直接喂进矩阵、静默算出偏掉几个像素的机械坐标。
        /// 靠"字段是不是 null"判断则要求每个消费者都**记得**去查——那是靠纪律，不是靠结构。
        ///
        /// 消费方义务：拿到本 Kind 时，**必须先把输入像素经 <see cref="CalibrationTransform.Intrinsics"/>
        /// 去畸变**再喂矩阵；反向（机械 → 像素）则先算出去畸变域像素、再施加**逆向**畸变。
        /// 用 <c>HALCON change_radial_distortion_points</c> 实现，正反向只需对调 camParamIn/Out。
        /// </summary>
        NinePointUndistorted = 4,

        /// <summary>
        /// 透视标定 + **内参去畸变**（二期③）：语义同 <see cref="NinePointUndistorted"/>，
        /// 产出用 <see cref="CalibrationTransform.ProjectiveMatrix"/>（9 元）。
        /// </summary>
        PerspectiveUndistorted = 5,

        /// <summary>
        /// 网格标定 + **内参去畸变**（二期③）：语义同 <see cref="NinePointUndistorted"/>，
        /// 产出用 <see cref="CalibrationTransform.MeshNodes"/> + <see cref="CalibrationTransform.MeshSize"/>。
        /// 注意网格与内参的**互补**关系：网格是"把畸变就地摊平"（不外推），
        /// 内参是"显式建模后整个视野一致"（可外推）——两者都做时，去畸变把畸变拿掉、
        /// 网格只负责平面单应性，格内残差更小。
        /// </summary>
        MeshUndistorted = 6,

        /// <summary>
        /// **手眼标定**（2026-10-22 新增，纯加法）：成对 (机器人位姿 A_i, 相机位姿 B_i) 求出的
        /// **6-DOF 刚体变换** X，满足 A_i·X·B_i = const。产出用
        /// <see cref="CalibrationTransform.HandEyeMatrix"/>（16 元 4×4 行主序）；
        /// <see cref="CalibrationTransform.Matrix"/> 保持零（故意，与透视/网格同一纪律）：
        /// 未升级的消费者走 6 元矩阵会**明确失败**，绝不允许把 6-DOF 静默降成平面仿射。
        ///
        /// 为什么不是 9 元：投影矩阵（Perspective）表达的是**平面到平面**的射影映射，
        /// 手眼要的是三维刚体变换（旋转 + 平移），数学上不是一回事，不能用同一个数组装。
        /// </summary>
        HandEye = 7
    }

    /// <summary>
    /// 手眼装法：决定 <see cref="CalibrationTransform.HandEyeMatrix"/> 里那个 4×4 的物理含义。
    /// 两种装法的求解方程同形（A·X·B = const），**差别全在 B 是谁**——
    /// 所以消费方拿不准装法时，矩阵看起来"完全正常"，错的是物理语义。
    /// </summary>
    public enum HandEyePoseKind
    {
        /// <summary>眼在手上（相机随机器人动）：X = T_gripper←camera，B_i = T_camera←target（靶标固定不动）</summary>
        [System.ComponentModel.DataAnnotations.Display(Name = "眼在手上")]
        EyeInHand = 0,

        /// <summary>眼在手外（相机固定）：X = T_base←camera，B_i = T_target←gripper（靶标装在机器人上）</summary>
        [System.ComponentModel.DataAnnotations.Display(Name = "眼在手外")]
        EyeToHand = 1
    }

    /// <summary>
    /// <see cref="CalibrationKind"/> 的**判据单一真源**：哪些 Kind 属于"去畸变像素域"、
    /// 某个 Kind 的**基础模型**是什么。
    ///
    /// 为什么放契约程序集
    /// ---------
    /// 生产端（「标定」插件）与消费端（「坐标变换」插件）都要判这两件事。
    /// 各写一遍 `kind is A or B or C` 就是**分叉风险**——将来加一个 Kind 时漏改一处，
    /// 那一处就会把"去畸变域"当"畸变域"处理，产出**看起来正常的错坐标**（本仓反复立规要杜绝的形态）。
    /// 本类无状态、纯函数，与 <see cref="CalibrationMesh"/> 同一理由放在契约层。
    /// </summary>
    public static class CalibrationKinds
    {
        /// <summary>是否属于"去畸变像素域"（矩阵定义在理想针孔坐标系上、必须先去畸变再喂）。</summary>
        public static bool IsUndistorted(CalibrationKind kind)
            => kind is CalibrationKind.NinePointUndistorted
                or CalibrationKind.PerspectiveUndistorted
                or CalibrationKind.MeshUndistorted;

        /// <summary>
        /// 去掉"去畸变"标记后的**基础模型**（PixelScale / NinePoint / Perspective / Mesh）。
        /// 用于"按模型族分支"的判据——比手写 `kind is Mesh or MeshUndistorted` 更难漏。
        /// 未识别的值原样返回（调用方按自己的 default 分支处理）。
        /// </summary>
        public static CalibrationKind Base(CalibrationKind kind) => kind switch
        {
            CalibrationKind.NinePointUndistorted => CalibrationKind.NinePoint,
            CalibrationKind.PerspectiveUndistorted => CalibrationKind.Perspective,
            CalibrationKind.MeshUndistorted => CalibrationKind.Mesh,
            _ => kind,
        };

        /// <summary>
        /// 给基础模型套上/摘掉"去畸变"标记。
        /// <paramref name="undistorted"/> = false 时原样返回（基础模型本来就是非去畸变成员）。
        /// 不支持的组合（如 PixelScale + 去畸变）原样返回——由调用方的守卫负责拒绝。
        /// </summary>
        public static CalibrationKind WithUndistortion(CalibrationKind baseKind, bool undistorted)
        {
            if (!undistorted) return baseKind;
            return baseKind switch
            {
                CalibrationKind.NinePoint => CalibrationKind.NinePointUndistorted,
                CalibrationKind.Perspective => CalibrationKind.PerspectiveUndistorted,
                CalibrationKind.Mesh => CalibrationKind.MeshUndistorted,
                _ => baseKind,
            };
        }
    }

    /// <summary>
    /// 标定结果：跨插件传递的契约（由「标定」插件产出，由「坐标变换」等下游消费）。
    ///
    /// 为什么放 Core.Interfaces
    /// ---------
    /// ① 跨插件传递必须走契约程序集（插件之间不互相引用）；
    /// ② 契约层**零 HALCON 依赖**（与 CameraFrame / HubImageItem 同一纪律）：本类全是基元类型，
    ///    JSON 往返安全（要随方案落盘、也可能进全局变量）。
    ///
    /// 像素坐标的两种语义（二期③起，务必先读）
    /// ---------
    /// · 六个 **非** Undistorted 的 Kind：像素 = **图像上的原始（畸变）像素**；
    /// · <see cref="CalibrationKind.NinePointUndistorted"/> / <see cref="CalibrationKind.PerspectiveUndistorted"/> /
    ///   <see cref="CalibrationKind.MeshUndistorted"/>：像素 = **去畸变后**的像素，矩阵定义在理想针孔坐标系上，
    ///   且 <see cref="Intrinsics"/> 必非 null。消费方必须先去畸变（正向）或最后加畸变（反向）。
    ///
    /// 坐标与角度约定（全仓库统一，写死避免静默错位）
    /// ---------
    /// · 像素坐标 (Row, Col)：Row 向下、Col 向右（与 HALCON 一致）；
    /// · 机械坐标 (X, Y)：单位毫米；轴方向/镜像/旋转**由矩阵自然吸收**，消费方不做额外假设；
    /// · 角度单位：度（与模板匹配输出一致）；
    /// · 多点数组排布：[x0, y0, x1, y1, ...]（x 在前、y 在后，成对）。
    ///
    /// 矩阵语义
    /// ---------
    /// <see cref="Matrix"/> 为 6 元 [a11 a12 a21 a22 a31 a32]，映射 **(Row, Col) → (X, Y)**：
    ///   X = a11·Row + a12·Col + a31
    ///   Y = a21·Row + a22·Col + a32
    /// 与 HALCON 的关系（2026-10-05 按本机 HALCON 23.05 实测订正旧注释）：
    /// vector_to_hom_mat2d 的 6 元排布是行主序 [h11 h12 h13 h21 h22 h23] = [a11 a12 a31 a21 a22 a32]，
    /// 与本契约 6 元相差一次重排：平移项 a31 提前到索引 2（a21/a22 顺移一位；a32 仍在索引 5）——
    /// 要喂 HALCON 算子（affine_trans_point_2d 等）必须先重排，**不存在"直喂不需要任何转换"**。方向约定 Px=Row、Py=Col 成立。
    /// PixelScale 模式下：a11 = a22 = MmPerPixel，其余为 0。
    ///
    /// ⚠ <see cref="MmPerPixel"/> 在带内参时的适用范围
    /// ---------
    /// 带内参的标定，其当量是**去畸变域**的当量。只吃 <c>MmPerPixel</c>（double）的下游
    /// （卡尺/Blob/几何计算等）**拿不到本对象、结构上无法自证**——它们拿原始像素乘这个当量，
    /// 在视野边缘会有百分之几的静默误差（kappa=-1500 时角点去畸变位移实测 ~23px）。
    /// 这类流程要么改用不带内参的标定，要么先在图像域去畸变。详见主文档 §4.5。
    ///
    /// 失配自证（消费方据此报错，绝不静默用旧标定）
    /// ---------
    /// · <see cref="SourceImageWidth"/> / <see cref="SourceImageHeight"/>：标定时的图像尺寸——
    ///   当前图尺寸与它不符 = 换了分辨率/换了相机 → 旧标定失效；
    /// · <see cref="CameraSerial"/>：这份标定属于哪台相机（多相机下机器可读的身份）；
    /// · <see cref="CreatedAtUtc"/> / <see cref="SourceTag"/>：创建时间与来源（标定图文件名等），供日志与人工核对；
    /// · <see cref="Intrinsics"/> 自带的 ImageWidth/Height 与 CameraSerial：与上面两个字段**交叉核对**
    ///   （两边都填才查，避免误报旧数据）——不一致说明"标定变换"与"内参"来自不同的帧/相机。
    /// </summary>
    public sealed class CalibrationTransform
    {
        /// <summary>标定类型</summary>
        public CalibrationKind Kind { get; set; }

        /// <summary>
        /// 像素当量（mm/px）。
        /// PixelScale 模式=直接算出；NinePoint 模式=由矩阵两基向量长度反解的均值。
        /// </summary>
        public double MmPerPixel { get; set; }

        /// <summary>仿射 6 元 [a11 a12 a21 a22 a31 a32]，语义见类注释（映射 (Row,Col)→(X,Y)）</summary>
        public double[] Matrix { get; set; } = new double[6];

        /// <summary>
        /// 投影（透视）9 元 [h11 h12 h13 h21 h22 h23 h31 h32 h33]（行主序 3×3，与 HALCON 9 元同排布），
        /// **仅 Kind = Perspective 有效**（其余模式为 null）。
        ///
        /// 映射 (Row, Col) → (X, Y)（X/Y 为机械坐标 mm）：
        ///   w = h31·Row + h32·Col + h33
        ///   X = (h11·Row + h12·Col + h13) / w
        ///   Y = (h21·Row + h22·Col + h23) / w
        /// 存储前统一按**带符号 h33** 归一（h33 = 1）；|h33| 低于 1e-9 视作解退化，标定侧直接失败（不会产出该字段）。
        ///
        /// 与旧 6 元（仿射子集）的换算：9 元 = [a11, a12, a31, a21, a22, a32, 0, 0, 1]
        /// （**a31/a32 插在索引 2/5，不是拼在尾部**——与 HALCON 行主序一致）。
        /// </summary>
        public double[]? ProjectiveMatrix { get; set; }

        /// <summary>
        /// 网格节点（**仅 Kind = Mesh 有效**，其余模式为 null）：长度 = N·N·4，每 4 个一组
        /// [Row, Col, X, Y]，按**行主序**（组下标 = r·N + c，r=行、c=列）。
        /// 语义：节点 (r,c) 的**实测**像素位置 (Row, Col) 与对应机械坐标 (X, Y)——不假设"理想等距网格"。
        ///
        /// 映射方式（消费方按此实现，确保两端一致）：把 N×N 节点三角化为 2·(N-1)² 个三角形，
        /// 像素点落在哪个三角形就用该三角形的仿射（重心坐标插值）换算；节点处精确通过、格内近似畸变。
        /// **点落在网格覆盖范围之外（凸包外）必须明确失败**——网格是插值，不做外推。
        /// </summary>
        public double[]? MeshNodes { get; set; }

        /// <summary>
        /// 网格边长 N（**仅 Kind = Mesh 有效**）：<see cref="MeshNodes"/> 应为 N×N 个节点。
        /// N &lt; 2 或 长度 ≠ N·N·4 一律视作契约损坏，消费方必须明确失败（不猜、不降级）。
        /// </summary>
        public int MeshSize { get; set; }

        /// <summary>
        /// 相机内参（**仅 *Undistorted 三个 Kind 有效**；其余模式为 null）。
        ///
        /// 非 null 时，本变换的**像素坐标语义是「去畸变后」的**——即
        /// <see cref="Matrix"/> / <see cref="ProjectiveMatrix"/> / <see cref="MeshNodes"/> 里的像素
        /// 都定义在理想针孔坐标系上。消费方必须先经 <see cref="CameraIntrinsics"/> 去畸变再喂矩阵
        /// （反向则先算去畸变域像素、再施加逆向畸变）。语义与理由详见
        /// <see cref="CalibrationKind.NinePointUndistorted"/> 的注释。
        ///
        /// 为什么用 null 而不是"永远给一份恒等内参"：null 表达"这份标定根本没做内参"，
        /// 与"做了内参但系数为 0"是**两件事**（后者会走一趟去畸变算子，虽然结果几乎相同）。
        /// 兼容性：纯加法字段——旧 JSON 读入为 null，历史行为逐字不变。
        /// </summary>
        public CameraIntrinsics? Intrinsics { get; set; }

        /// <summary>标定时的图像宽（失配检测用）</summary>
        public int SourceImageWidth { get; set; }

        /// <summary>标定时的图像高（失配检测用）</summary>
        public int SourceImageHeight { get; set; }

        /// <summary>来源标签（标定图文件名等，人工核对用）</summary>
        public string SourceTag { get; set; } = "";

        /// <summary>相机序列号（多相机的"这份标定属于哪台"；一期日志/人工核对，二期做自动失配校验）</summary>
        public string CameraSerial { get; set; } = "";

        /// <summary>标定创建时间（UTC）</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>残差均方根（像素）：标定点拟合后，各点"实测 - 反算"的距离 RMS</summary>
        public double ResidualRmsPx { get; set; }

        /// <summary>最大单点残差（像素）</summary>
        public double MaxResidualPx { get; set; }

        /// <summary>
        /// 按**径向带**统计的平均残差（像素），长度 3：
        /// [0]=中心带（归一化半径 ≤0.4）、[1]=中间带（0.4~0.7）、[2]=边缘带（>0.7）。
        /// 用途：区分"随机噪声"与"镜头畸变"——残差随半径单调增大（边缘 ≫ 中心）就是畸变的典型信号。
        ///
        /// **无点的带记 0**（不是 NaN）：NaN 会被 JSON 序列化成非标准的 <c>"NaN"</c> 字符串，
        /// 跨语言消费有风险；"这个带没有点"由 <see cref="ResidualBandCounts"/> 表达——
        /// 消费方必须先看计数再看均值，计数为 0 时均值无意义。
        /// </summary>
        public double[] ResidualByRadiusBands { get; set; } = new double[3];

        /// <summary>
        /// 各径向带**参与统计的点数**（长度 3，与 <see cref="ResidualByRadiusBands"/> 一一对应）。
        /// 计数为 0 的带其均值为 0（占位、无意义）——判读形态前先确认计数非 0。
        /// 为什么单列一个字段：3 点标定时常见"只有中心带有 1 个点"，
        /// 没有计数就无法区分"残差真的是 0"与"这个带根本没点"。
        /// </summary>
        public int[] ResidualBandCounts { get; set; } = new int[3];

        /// <summary>
        /// 手眼 4×4 刚体变换（**仅 Kind = HandEye 有效**，其余模式为 null），**16 元行主序**
        /// [r11 r12 r13 tx, r21 r22 r23 ty, r31 r32 r33 tz, 0 0 0 1]。
        ///
        /// 语义按 <see cref="HandEyePoseKind"/> 区分：
        /// · <c>EyeInHand</c>：X = T_gripper←camera（相机装在手上），满足 A_i·X·B_i = const，
        ///   A_i = T_base←gripper（机器人控制器给的位姿）、B_i = T_camera←target（靶标在相机里的位姿）；
        /// · <c>EyeToHand</c>：X = T_base←camera（相机固定），满足 A_i·X·B_i = const，
        ///   其中 A_i = T_base←gripper、B_i = T_target←gripper（靶标装在手上）。
        ///
        /// **消费方必须自己判 Kind 再解释 X**：同一个 4×4 在两种装法下物理含义不同，
        /// 混用不会报错，只会让抓取点整体偏一个常数——这类错误在单站调试里看不出来。
        /// </summary>
        public double[]? HandEyeMatrix { get; set; }

        /// <summary>手眼装法（**仅 Kind = HandEye 有效**）：决定 <see cref="HandEyeMatrix"/> 的物理含义</summary>
        public HandEyePoseKind HandEyePose { get; set; }

        /// <summary>
        /// 手眼求解的旋转残差 RMS（**弧度**，仅 Kind = HandEye 有效）。
        /// 与 <see cref="ResidualRmsPx"/> 不同单位：那个是像素，这个是弧度——别混着判阈值。
        /// </summary>
        public double HandEyeRotationRmsRad { get; set; }

        /// <summary>手眼求解的平移残差 RMS（**毫米**，仅 Kind = HandEye 有效）</summary>
        public double HandEyeTranslationRmsMm { get; set; }

        /// <summary>手眼求解实际参与的成对数据组数（仅 Kind = HandEye 有效）</summary>
        public int HandEyeSampleCount { get; set; }
    }
}
