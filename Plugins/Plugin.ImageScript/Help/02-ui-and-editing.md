# 图像脚本 · 脚本编辑器与变量面板

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageScript

这一页讲**脚本里能写什么**：语法硬规则、变量怎么进出、画图用哪些算子、编辑器有什么帮助、示例模板从哪儿拿。

## 语言与语法硬规则

脚本是 **HDevelop 脚本语言**（类 Pascal，一行一算子），由 HALCON 官方 `HDevEngine` 编译执行。五条会直接导致编译失败的规则：

| 规则 | 合法写法 | 非法写法（会报 `invalid program line`） |
| --- | --- | --- |
| **一个算子写在一行内** | `threshold (Image, Region, 128, 255)` | 把参数拆到下一行续写（引擎不支持续行） |
| **注释必须独占一行** | 行首 `*` 或 `'` | `threshold(...) * 取亮区`、`X := 10 '备注'`（行尾拖注释/字符串） |
| **只能用英文半角标点** | `( ) , ' : =` | `，＇（）、“”` 与不间断空格（从聊天窗口/文档粘贴最常带进来） |
| **用到的变量必须存在于接口表或先赋值** | 先 `X := 0` 再用 | 直接引用「输入/输出变量」表里没有的名字 |
| **引号/括号必须成对** | — | 少一个 `'` 会让引擎报的行号指不到真凶 |

其余写法上的事实（都有实测依据）：

- 赋值用 `:=`；紧凑写法 `OutValue:=10.0` 合法，插件**不会**去改写它（别画蛇添足）。
- 字符串用**单引号**（`Result := 'NG'`）；中文注释、中文提示文本、制表符都是合法的。
- 控制结构照常可用：`if … elseif … else … endif`、`for … endfor`、`while … endwhile`；分支里的 `dev_*` 绘图回调也正常执行。
- 数值格式化用 `值$'.2f'`，字符串拼接用 `+`，例如
  `dev_disp_text ('面积: ' + Area$'.1f', 'window', 12, 12, 'black', [], [])`。
- 行尾注释插件会**自动剥离**（用空格占位、行号不变），但只在能确定是注释时才剥：
  含 `:=` 的赋值行里 `*` **一律当乘号**，绝不剥离（历史上 `Area := Width * Height` 被误剥成 `Area := Width`
  这类"不报错但算错"的失效模式，宁可放行让引擎报错也不改值）。真要写注释，请独占一行。

## 变量怎么进出：接口四类 + 变量表

过程的接口分四类，编辑区标题会显示成 ` script ( io : oo : ic : oc )`：

| 接口类别 | 含义 | 对应配置界面 |
| --- | --- | --- |
| `io` iconic 输入 | 进来的图形（图/区域/轮廓） | 「输入变量」行，类型选 `HImage` / `HRegion` / `HXld` / `HObject` |
| `ic` 控制输入 | 进来的数值/文本/元组 | 「输入变量」行，类型选 `Int` / `Double` / `String` / `HTuple` |
| `oo` iconic 输出 | 出去的图形 | 「输出变量」行，图形类型 |
| `oc` 控制输出 | 出去的数值/文本/元组 | 「输出变量」行，控制类型 |

- **变量表是唯一真源**：每次编译前插件把表格反向写进过程接口（图形类进 `io/oo`、控制类进 `ic/oc`）。
  所以手动"加一行变量"不用去改接口文本；反过来，只在接口里写了、表里删了，那行也会从接口消失。
- **输入取值**：图形类只能来自连线（上游端口的实际值）；控制类可以连线，也可以在该行常量框填文本，按声明类型解析。
- **输出取值**：脚本必须真的给这个输出变量赋过值，否则报"接口声明了它，但脚本没有写入"。
- **名字即端口**：变量名 = 流程端口名 = 脚本里的变量名，三者同一个字符串；改名字等于换端口，连线要重连。

## 画图：`dev_*` 显示算子（17 个）

引擎注册了宿主显示后端，脚本里的 `dev_*` 会被回调到 C# 画进一张离屏 `buffer` 画布，脚本结束后宿主回读成**效果图**发到主界面窗口 1。
底图默认是**第一路图像输入**（宿主每轮先预画一次），所以只贴字不必再写 `dev_display (Image)`。

| 脚本写法 | 说明 |
| --- | --- |
| `dev_display (Object)` | 显示图像/区域/轮廓（叠加画在画布上） |
| `dev_disp_text (Text, Coord, Row, Col, Color, GenParamName, GenParamValue)` | 写文字，**必须 7 参**（5 参简写编译不过）。`Coord` 只认 `'window'` / `'image'` 两个字面量 |
| `dev_set_color (Color)` | 后续绘制颜色：`'red'` / `'green'` / `'blue'` / `'yellow'` / `'black'` … |
| `dev_set_draw ('margin'/'fill')` | 区域按描边还是填充画（`margin` 不遮挡产品图） |
| `dev_set_line_width (Width)` | 线宽 |
| `dev_set_part (Row1, Col1, Row2, Col2)` | 局部放大（放大镜效果） |
| `dev_set_shape (...)` / `dev_set_contour_style (...)` / `dev_set_paint (...)` / `dev_set_colored (N)` / `dev_set_lut (...)` | 形状/轮廓样式/绘制模式/多彩/伪彩查找表 |
| `dev_open_window (Row, Col, Width, Height, Background, WindowHandle)` | 开窗口（宿主侧 mode 固定 `'buffer'`，不会闪出真窗口） |
| `dev_close_window ()` / `dev_clear_window ()` | 关窗（实际只清屏，画布常驻复用）/ 清屏 |
| `dev_get_window (WindowHandle)` | 取当前句柄（配 `set_display_font` 设字号用） |
| `dev_set_window (Window)` | 切当前窗口（`['vmview', N]` 这种写法是 `publish_preview` 的内部信令，不要手写） |
| `dev_set_window_extents (...)` | 窗口尺寸（画布尺寸通常由底图决定，一般不用写） |

`dev_disp_text` 的第 6/7 参（`GenParam`）实测边界：

- `['box']` **默认就是开的**（关框才看得出差别：同一行字 18303 像素落墨 vs 3754 像素）——再写 `['box'],['true']` 纯属冗余；
- `['box_color']` 有效（如 `['box','box_color'], ['true','yellow']`）；
- `['shadow']` **完全无效**；`['font']` / `['size']` 会**直接让这个算子失败**——字号没有单次调用的路子，只能走窗口级 `set_display_font`。

字号的三条事实：① `set_display_font` / `set_font` 可以用（要配 `dev_get_window` 拿到的句柄）；② Size 给 `-1` **不是**还原默认（官方实现里 `-1` 会变成 16）；
③ 字号是**窗口级状态**、画布常驻复用，脚本之间会串台——但宿主每轮开始都会把字体复位，所以脚本里"要多大就显式写多大"即可。

## 送图到主界面：`publish_preview`

```
publish_preview (Image, ViewIndex)     * ViewIndex = 1~9
```

它是宿主自带的自定义过程（`ScriptAssets\procedures\publish_preview.hdvp`，引擎的过程路径里已经挂着这个目录），
作用是**改道**：这张图直接发布到第 `ViewIndex` 号视图，**不再进入本脚本的效果图画布**。
只能送图像；传区域/轮廓会被静默丢掉。

> 为什么用 `.hdvp` 文件而不是注册 API：.NET 版 HDevelop Engine **没有**注册外部算子的接口
> （`hdevenginedotnet` / `halcondotnet` 里没有任何 `*External*` 类型，`HDevEngine` 也没有 `RegisterProcedure`；
> 教程里那套 `HExternalProcedure` 是 C++/CLI 的）。
> ⚠ 该目录由构建脚本投递到宿主运行目录，**漏投的表现是 `unresolved procedure call`**，很难联想到"少拷了一个文件"。

## 老式窗口算子：自动改写与需要手改的黑名单

从 HDevelop 或网上粘的代码大量用原生窗口算子。它们**照样编译通过、也不报错**，但画在另一张窗口上——
效果图静默变空白，比编译失败更难查。所以插件在送引擎编译前做"一行换一行"的改写（编辑器里的原文不变，行号不错位）。

| 老写法 | 自动改写成 |
| --- | --- |
| `open_window` / `clear_window` / `close_window` | `dev_open_window` / `dev_clear_window` / `dev_close_window` |
| `disp_obj` / `disp_image` / `disp_region` | `dev_display` |
| `disp_text` / `disp_message` | `dev_disp_text`（补齐 7 参；老教程里"第 2 参直接写行号"的写法会自动补 `'image'` 坐标系） |
| `set_color` / `set_draw` / `set_line_width` / `set_shape` / `set_paint` / `set_lut` / `set_contour_style` / `set_colored` / `set_part` | 去掉首参窗口句柄 + `dev_` 前缀 |
| `dump_window_image` / `dump_window` | 置空占位（出图由宿主统一负责，脚本里不用自己截屏） |

改不动的（无法一行换一行）会**直接给中文指引**并标出行号，照指引重写即可：

| 老写法 | 改成 |
| --- | --- |
| `disp_rectangle1` | `gen_rectangle1 (Rect, Row1, Column1, Row2, Column2)` 然后 `dev_display (Rect)` |
| `disp_circle` | `gen_circle (Circle, Row, Column, Radius)` 然后 `dev_display (Circle)` |
| `disp_line` | `gen_region_line (Line, Row1, Column1, Row2, Column2)` 然后 `dev_display (Line)`；要任意粗细的细线用 `gen_contour_polygon_xld` |
| `disp_arrow` | `gen_arrow_contour_xld (Arrow, Row1, Column1, Row2, Column2, HeadLength, HeadWidth)` 然后 `dev_display (Arrow)`（**7 参**，多写一个就报 `invalid program line`） |
| `disp_cross` | `gen_cross_contour_xld (Cross, Row, Col, Size, Angle)` 然后 `dev_display (Cross)` |
| `disp_polygon` | `gen_contour_polygon_xld (Contour, [Row1,Row2,…], [Col1,Col2,…])` 然后 `dev_display (Contour)` |
| `set_tposition` | 删掉它，把行/列直接写进 `dev_disp_text` 的第 3、4 个参数 |
| `write_string` | `dev_disp_text (Text, 'image', Row, Column, 'green', [], [])` |
| `get_window_extents` / `set_window_extents` | HDevEngine 里不可用；尺寸由输入图像自动决定，删掉/改用 `dev_open_window` |

## 可用算子

**全部 HALCON 算子（数千个）都能写**，插件不做事前白名单。编辑器另带一份**中文精编算子库（122 个）**，
补全时优先展示、悬浮卡有签名与参数说明；其余算子名走全量名单补全（只给名字，没有中文说明）。

| 分类 | 代表算子 |
| --- | --- |
| 图像 IO | `read_image` `write_image` `get_image_size` `get_image_type` `count_channels` `copy_image` `clear_image` `region_to_bin` `gen_image_const` |
| 颜色 | `rgb1_to_gray` `decompose3` `compose3` |
| 预处理 | `reduce_domain` `get_domain` `full_domain` `crop_part` `crop_domain` `mirror_image` `scale_image` `scale_image_max` `convert_image_type` |
| 滤波 | `gauss_image` `mean_image` `median_image` `eq_histo_image` `emphasize` |
| 阈值分割 | `threshold` `dyn_threshold` `var_threshold` `auto_threshold` `binary_threshold` `fast_threshold` `histo_to_thresh` |
| 区域生成 | `gen_rectangle1` `gen_rectangle2` `gen_circle` `gen_ellipse` `gen_region_points` `gen_empty_region` |
| 区域运算 | `union1` `union2` `intersection` `difference` `complement` `clip_region` `move_region` |
| 形态学 | `dilation_circle` `erosion_circle` `opening_circle` `closing_circle` `fill_up` `boundary` `shape_trans` |
| 连通域 | `connection` `select_shape` `select_shape_std` |
| 特征 | `area_center` `orientation_region` `circularity` `rectangularity` `convexity` `compactness` `contlength` `diameter_region` `smallest_rectangle1` `smallest_rectangle2` `smallest_circle` `inner_circle` `intensity` `gray_features` |
| XLD 轮廓 | `edges_sub_pix` `threshold_sub_pix` `gen_contour_polygon_xld` `get_contour_xld` `length_xld` `fit_line_contour_xld` `fit_circle_contour_xld` `select_contours_xld` `gen_circle_contour_xld` |
| 模板匹配 | `create_shape_model` `add_shape_model_xld_contours` `find_shape_model` `find_scaled_shape_model` `get_shape_model_contours` `clear_shape_model` `create_ncc_model` `find_ncc_model` |
| 2D 变换 | `hom_mat2d_identity` `hom_mat2d_translate` `hom_mat2d_rotate` `vector_angle_to_rigid` `hom_mat2d_to_affine_par` `affine_trans_pixel` `affine_trans_region` `affine_trans_image` `affine_trans_contour_xld` |
| 几何 | `distance_pp` `distance_pl` `angle_ll` `intersection_lines` |
| 对象 | `count_obj` `select_obj` `sort_obj` `concat_obj` `clear_obj` |
| Tuple | `tuple_gen_sequence` `tuple_length` `tuple_select_range` `tuple_sort` `tuple_find` `tuple_max` `tuple_min` `tuple_mean` `tuple_deviation` `gen_tuple_const` |
| 显示 | 上面那张 `dev_*` 表 + `publish_preview` |

参数顺序按 HDevelop 惯例：**图形输入 → 图形输出 → 控制输入 → 控制输出**（悬浮卡里的签名写成 `op ( io : oo : ic : oc )`）。

## 资源短名：脚本里不写死路径

`read_image (X, 'marks')` 里的 `'marks'` 会被自动解析成宿主运行目录下的
`ScriptAssets/images/marks.png`；`read_ocr_class_mlp ('Industrial_0-9A-Z_Rej', ModelID)` 同理解析到 `ScriptAssets/ocr/`。

- 已经写成路径（含 `/`、`\`、`:`）或带扩展名的，插件不动，交给 HALCON 自己找；
- 装了 HALCON 时官方图像名（如 `'fabrik'`）也能直接用；
- 本机自带 **32 张示例图**（`marks` / `fabrik` / `keypad` / `pcb_color` / `bga_14x14_defects` / `rim` / `traffic1` …）与
  **1 个 OCR 模型**（`Industrial_0-9A-Z_Rej`），示例模板直接引用它们，**换电脑、换部署路径不用改脚本**。

## 编辑器：怎么写得快

| 能力 | 怎么用 |
| --- | --- |
| 语法高亮 | 关键字着色 + **当前过程的接口变量单独着色**（一眼看出哪些是端口带进来的） |
| 算子补全 | 输入字母自动过滤；`Ctrl+Space` 强制弹全量列表 |
| 参数模板 | 选中算子插入 `op (P1, P2, …)` 并选中首个占位参数，`Tab` 逐个步进 |
| 参数提示 | 光标打出 `(` 弹该算子的参数清单；`)` 或换行自动关闭 |
| 悬浮文档 | 鼠标停在算子名上出签名/中文含义/参数说明/常见用途（122 个精编库覆盖的算子有） |
| 注释切换 | `Ctrl+/` 给当前行/选区加或去 `*` 注释 |
| 格式化 | 工具栏按钮：统一成 `op (a, b, c)` 风格、清行尾空白。**只动空白不动语义**，可一次 `Ctrl+Z` 回退 |
| 校验（编译） | 工具栏「校验脚本」或右键「编译（校验脚本）」：强制重编译、**不执行**，底部显示结果并把错误行标红、跳到首个错误行 |
| 插入示例代码 | 右键菜单按分类给出 **53 个模板**（A 图像预处理 / B Blob分析 / C 定位 / D 模板匹配 / E 精密测量 / F 识别 / G 缺陷检测·有无检测 / H 亚像素轮廓 / I 结果显示 / J 综合工艺 / K 工位级综合 / L 效果图标注），整段插在光标行下方 |
| 查找替换 / 撤销 / 自动缩进 / 括号配对 / 字号缩放 | 与常见编辑器一致 |
| 导入 / 导出 `.hdev` | 与 HDevelop 互导（导出前会把变量表同步进接口，保证 `.hdev` 接口完整） |

模板里的 `{A}` 占位符在插入时替换成宿主 `ScriptAssets` 的绝对路径（正斜杠），并自动创建 `models/` 与 `ng_records/` 两个子目录
——模型存盘、NG 留档类模板靠它，不需要你手填路径。
