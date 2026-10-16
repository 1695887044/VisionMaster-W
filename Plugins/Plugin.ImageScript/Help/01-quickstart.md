# 图像脚本 · 快速上手

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageScript

在流程里嵌一段 **HDevelop 脚本**（Halcon 的类 Pascal 语言），用 HALCON 官方引擎 `HDevEngine` 执行。
你在配置界面声明的**输入/输出变量**会自动长成流程端口，能和任何算子正常连线：
一个节点顶掉一串算子（预处理 → 分割 → 连通域 → 筛选 → 判定），或者做那些现成算子没有的活（自定义拟合、多 ROI 循环、按文件名规律读图）。

## 为什么需要它 / 原理

现成算子是"成品工具"，覆盖面广但不可能覆盖全部；一旦流程里出现"这三步只能用循环写""这个判定要把五个结果拼起来算"，
就得上脚本。本插件把 HDevelop 过程原样搬进流程：

1. **过程（procedure）是脚本单位**。一个步骤可以存多个过程，「运行过程」下拉指定本轮跑哪一个（`main` 不进这个下拉）。
   过程有**接口**（四类参数：`io` 图形输入 / `oo` 图形输出 / `ic` 控制输入 / `oc` 控制输出）和**过程体**（一行一个算子）。
2. **变量表是接口的唯一真源**。配置界面的「输入变量 / 输出变量」两张表决定：
   ① 流程端口（名字 = 变量名，类型 = 声明类型）；② 送进引擎的实参；③ 从引擎取回的出参。
   编译前插件会把表格**反向同步**进过程接口，所以只加变量不写接口也能跑；反过来，脚本里用到了表里没有的变量就会报错。
3. **执行走 HALCON 原生引擎**（`hdevenginedotnet`，不是自研解释器，也不是 C# 脚本插件那套 Roslyn）。
   全部 HALCON 算子（数千个）都能直接写；引擎开了 JIT 预编译（`execute_procedures_jit_compiled`），
   脚本内容按 SHA256 指纹缓存，内容一个字没改就复用已编译结果、零成本。
4. **显示由宿主接管**。引擎注册了显示后端，脚本里的 `dev_display` / `dev_disp_text` / `dev_set_*` 会画到一张离屏画布上，
   脚本跑完由宿主回读成**效果图**发布到主界面窗口 1；区域/轮廓类输出还能按变量行的「显示窗口」下拉单独推送到 1~9 号窗口。

> 什么时候**不该**用它：只需要一两个标准算法时，用现成算子更好维护（端口、参数、监视都是现成的）。
> 脚本越长越像"黑箱"，产线换人接手时读不动。建议把脚本当成"最后一段兜底逻辑"，而不是默认选项。

## 典型用法 / 接线

```
图像采集.OutputImage ──▶ 图像脚本.Image        （输入变量 Image，类型 HImage）
几何查找.RoiRegion ────▶ 图像脚本.Roi          （输入变量 Roi，类型 HRegion）
条件分支.判定 ←──────── 图像脚本.Result         （输出变量 Result，类型 String，脚本里写 OK/NG）
CSV记录.图片     ←───── 图像脚本.ResultImage    （输出变量 ResultImage，类型 HImage）
图像脚本.ResultImage ──▶ 数据记录.图片
```

最小骨架（1 图进 → 1 图出 + 结论），也是插件自带的默认脚本：

```
count_channels (Image, Channels)
if (Channels >= 3)
    rgb1_to_gray (Image, GrayImage)
else
    copy_image (Image, GrayImage)
endif
threshold (GrayImage, Region, ThreshMin, 255)
connection (Region, ConnectedRegions)
select_shape (ConnectedRegions, Targets, 'area', 'and', MinArea, 1e7)
count_obj (Targets, Number)
if (Number > 0)
    Result := 'NG'
else
    Result := 'OK'
endif
dev_display (GrayImage)
dev_display (Targets)
ResultImage := GrayImage
```

变量表要配成：输入 `Image`(HImage)、`ThreshMin`(Int)、`MinArea`(Double)；输出 `ResultImage`(HImage)、`Result`(**String**)。

## 常见问题

**Q：拖进来是空的，还是要从 HDevelop 导出 `.hdev` 再导入？**
都不用。新步骤**首次**打开配置窗口会自动装一份默认脚本（1 图进 → 1 图出 + OK/NG 结论）；
想推倒重来就用工具栏「恢复默认脚本」（会弹确认框，整表替换）。要搬现成工程才用「导入.hdev」。
注意判据是"这个步骤从没保存过脚本配置"——手动删空并保存后，默认脚本不会再来第二次。

**Q：流程里这个节点连不上上游，端口列表是空的？**
端口由变量表生成。先在「输入变量」加一行（名字要和脚本里用的变量名**逐字一致**），端口才会出现。
改了名字 = 换了端口，原来的连线要重连。

**Q：报 `输入变量[Image]未链接或图像为空`？**
① 该行没连线也没值（图形类必须连上游）；② 上游步骤本轮失败，输出端口是空的；③ 名字写错（大小写、多空格）。
这条报错说明**脚本一行都没执行**，不用去查脚本内容。

**Q：报 `invalid program line: N`，但第 N 行看着没问题？**
引擎数的是"第 N 个**非空行**"，插件已经换算成编辑器真实行号并标红。剩下的三类真凶：
① 行尾拖注释（`threshold(...) * 注释` 非法，HDevelop 要求注释独占一行）；
② 中文全角标点（`，＇（）、“”` 和不间断空格，从聊天窗口/文档复制代码最容易带进来）；
③ 算子被拆成多行（引擎不支持续行，一个算子必须写在一行内）。
点编辑器右键「编译（校验脚本）」可以在不运行的情况下拿到全部定位。

**Q：效果图一片空白，但一点报错都没有？**
十有八九是脚本里画的是**另一张窗口**：原生 `open_window` / `set_color` / `disp_text` 注册显示后端后照样编译通过、也不报错，
但画不出效果图。插件会在编译前把能安全改写的老算子自动改成 `dev_*`（详见第 2 页）；
剩下改不动的（`disp_rectangle1` / `disp_circle` / `disp_line` / `disp_arrow` / `disp_cross` / `disp_polygon` / `write_string`）
会直接给中文指引，照它说的改成 `gen_* + dev_display` 两行写法。

**Q：明明算出区域了，取结果时报 `Output object type mismatch (excepted image, got region)`？**
输出变量表的「类型」选错了：`threshold`/`connection` 出的是**区域**（选 `HRegion`），`edges_sub_pix` 出的是**轮廓**（选 `HXld`）。
插件会把这句天书翻译成"请把该变量的类型下拉改成 区域(HRegion)"，照改即可。

**Q：脚本里只写了一句 `dev_disp_text`，能贴在我自己的图上吗？**
能。宿主每轮先把**第一路图像输入**预画成底图，所以只贴字不必先写 `dev_display (Image)`；写了也只是原位重画，没有副作用。

**Q：想把图送到窗口 3，但效果图只去窗口 1？**
两条路子，语义不同：
① 输出变量行的「显示窗口」下拉选 3 —— 按变量推，支持区域/轮廓（会叠到底图上）；
② 脚本里写 `publish_preview (Image, 3)` —— 主动改道，这张图**不再进入本脚本的效果图画布**，且**只能送图像**（区域/轮廓会被静默丢掉）。

**Q：导入 `.hdev` 后运行不起来？**
运行过程下拉**不含 `main`**。导入后在「运行过程」里手动选一个非 `main` 的过程，
再点「同步接口变量」把接口刷进变量表、给图形输入连上上游。

**Q：默认过程为什么叫 `script` 不叫 `main`？**
同上：叫 `main` 就进不了运行下拉、执行时也找不到过程。新建过程时别用 `main` 当运行过程。

**Q：`select_shape` 用 `'elongation'` 报 #3101？**
本仓库的 HALCON 23.05 不认识这个特征名，改用 `smallest_rectangle1` / `smallest_rectangle2` 自己算长宽比。

**Q：改完脚本要重启软件吗？**
不用。执行按内容指纹判重编译，改一个字下一轮就生效。但**插件 DLL 本身**是另一回事——
改过插件源码必须重建插件工程并重启，否则跑的还是旧 DLL。
