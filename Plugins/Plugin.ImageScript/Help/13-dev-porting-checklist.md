# 图像脚本 · 开发章节：HDevEngine 对接与 HALCON 版本差异

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageScript ｜ 读者：开发

这页是给"把这个插件搬进另一个宿主 / 换一版 HALCON"的人看的清单。逐条都在源码里有落点，搬动时按顺序核对。

## 一 依赖与装载面

1. 引用 HALCON 的 .NET 引擎程序集（`hdevenginedotnet`），与 `halcondotnet` 同版本；两者版本不一致时症状是引擎侧 `HDevEngine` 构造就抛，脚本一行都还没编译。
2. 原生库要能在宿主 exe 目录被找到。插件 dll 放子目录不会自动带出原生 dll——这套工程的原生依赖一律投到**宿主 exe 目录**，不是插件目录。
3. 目标框架 `net9.0-windows`（`<TargetFrameworks>` 是复数写法），平台 x64。

## 二 引擎初始化四步（`CreateEngine`）

```
var e = new HDevEngine();
e.SetProcedurePath(Path.GetTempPath());          // 编译期临时 .hdev 落在这里，引擎要能读
if (Directory.Exists(procedureDir))
    e.AddProcedurePath(procedureDir);            // ScriptAssets\procedures
e.SetEngineAttribute("execute_procedures_jit_compiled");
e.SetHDevOperators(new HDevDisplayBackend());    // 显示后端，见第五节
```

- **过程搜索路径必须含临时目录**：编译走的是"把过程写成临时 `.hdev` 再让引擎读"的路子（文件名 `vm_imgscript_{Guid:N}.hdev`，`finally` 里删），
  搜索路径没有它就报"读不到程序"。删文件要在 `finally`，否则异常一次就漏一个临时文件。
- **外部过程注册**：.NET 引擎**没有**"把过程注册进引擎"的 API，只能靠文件搜索路径。
  所以 `ScriptAssets\procedures` 里放的是 `.hdvp`（HDevelop 过程文件），靠 `AddProcedurePath` 生效。
- `execute_procedures_jit_compiled` 只影响首次执行的编译开销，不改语义。

## 三 过程与调用的对象模型

| 对象 | 用途 | 生命周期 |
| --- | --- | --- |
| `HDevProgram` | 整个脚本（含全部过程） | 进 `CallCache`，按脚本 SHA256 指纹复用，条目超 64 全清 |
| `HDevProcedure` | 单个过程，`HDevProgram.GetProcedure(name)` | 随程序 |
| `HDevProcedureCall` | 一次绑定实参的执行会话 | 每次运行新建，用完释放 |

绑参与取值的四个 API：`SetInputIconicParamObject`、`SetInputCtrlParamTuple`、`GetOutputIconicParamObject`（以及 `...Image` / `...Region` / `...Xld` 的窄型版本）、`GetOutputCtrlParamTuple`。
窄型版本只在已经确认对象类别后用；类别判定用 `GetObjClass()`，这也是"类型不符"提示能说出真实类别的原因。

## 四 脚本文件的构造（EProcedure）

过程以 XML 编组存进 `Procedures` 键：根节点 `hdevelop`，属性 `file_version="1.1"`、`halcon_version="12.0"`；
过程节点带 `name`，接口节点分 `io`（图形输入）、`oo`（图形输出）、`ic`（控制输入）、`oc`（控制输出），
每个 `par` 写 `name`、`base_type`（`iconic` 或 `ctrl`）、`dimension`。

`halcon_version` 写 `12.0` 是**兼容用的低版本号**，不是"我们用 12.0"：HDevelop 读文件时看的是版本能否向下兼容，
写当前大版本号反而会让低版本引擎拒读。换 HALCON 版本时不要顺手把它改成新版本号。

## 五 显示后端（R36 的永久豁免点）

`HDevDisplayBackend : IHDevOperators` 实现了 `dev_open_window`、`dev_close_window`、`dev_get_window`、`dev_set_window_extents`、
`dev_set_part`、`dev_clear_window`、`dev_display`、`dev_disp_text`、`dev_set_draw`、`dev_set_colored`、`dev_set_color`、
`dev_set_lut`、`dev_set_paint`、`dev_set_line_width`、`dev_set_contour_style`、`dev_set_shape` 这一族。
要点：

1. 窗口是 `open_window(0, 0, w, h, 'white', 'buffer', 'local', ...)` 的**离屏 buffer 窗口**，不是屏上窗口；
   所以"运行脚本不会弹一个 HDevelop 那样的窗"。R37 的窗口纪律同样按这条判。
2. 交给引擎的窗口句柄必须是**新容器**，不能把内部持有的实例直接 `out`。
3. `dev_get_window` 只有在宿主视图窗口标记（内部按窗口归属判断）存在时才回真实句柄，否则回落到默认窗口。
4. 新窗口刚建好时 `get_font` 拿到的是原生值，后端存下来用于每次 `Begin` 复位字体——
   不复位会让上一个脚本设的字体串到下一个脚本。
5. 原生窗口算子（`open_window`、`set_color`、`disp_*`）在引擎里**能编译通过**，但画到屏外，症状是"成功但画布空的"。
   这就是 `LegacyDisplayShim` 存在的理由：能改写的改写，改不动的报错并给具体两行写法。

搬进别的宿主时，这一族是唯一需要重写代码的地方；其余都是引擎原生 API，语义不变。

## 六 HALCON 版本差异与错误码

工程基线是 HALCON 23.05。这几个错误码在本插件的路径上出现过，写法上要有针对性：

| 码 | 触发面 | 代码里的应对 |
| --- | --- | --- |
| #1305 | 空对象集上做输出/显示类操作 | `ComposeIconicToImage` 先用纯算子探一次，空集就不合成 |
| #9302 | 窗口相关调用在离屏场景不被支持 | 显示一律走后端，不让脚本自己开屏上窗 |
| #2453、#2455 | 元组长度或参数个数与算子声明不匹配 | 控制实参按 `ToTuple` 切分后长度可能为 0，出错时提示改接口 |
| #3101 | 区域或轮廓坐标越界 | `GenBlankCanvas` 与合成路径先把尺寸夹进 64~4096 |

`ValidateScript()`（界面上的「校验脚本」）走的是"编译但不执行"的路径，
所以它能报语法与接口问题，报不出运行期问题；排错时不要把"校验通过"当成"能跑通"。

## 七 资源短名解析（脚本里不写死路径）

`ResolveAssetNames` 用正则找到 `read_image (Var,'name')` 与 `read_ocr_class_mlp('name',...)`，
分别把短名解析到 `ScriptAssets\images` 与 `ScriptAssets\ocr`；`FindAssetFile` 会**跳过含 `/ \ :` 的名字**
（那是用户写真路径，不是短名，不该被改写）。搬运工程时这两个目录必须一起投，
否则症状是"脚本在别人机器上能跑，在这台报读图失败"。
