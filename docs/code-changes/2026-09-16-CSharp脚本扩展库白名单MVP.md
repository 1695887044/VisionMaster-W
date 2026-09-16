# 开发记录：C#脚本扩展库白名单（ExternalLibs MVP）

- 日期：2026-09-16
- 类型：新功能（Plugin.CSharpScript 安全增强）
- 触发场景：前期讨论"脚本能否引入第三方包"时给出三条路线（A 蹭插件 LoadFrom / B 宿主 PackageReference / C ExternalLibs 扩展目录），并给出白名单四道防线设计。用户拍板：「按方案 A 执行，直接实现 MVP 白名单机制」——即 **路线 C + MVP 白名单** 一起做，默认拒绝（fail-closed）。

---

## 一、需求与决策

### 需求

1. 脚本要能引用"宿主 bin 之外"的第三方 dll，但必须**受控**：谁批准的、干什么用的、文件有没有被换过，都要有据可查。
2. 不加沙箱（MVP 明确不做），先把"纪律"用机制固化下来：**白名单登记 + 哈希锁 + 审计日志 + 总开关**。

### 关键决策（及理由）

| 决策 | 理由 |
|------|------|
| 扩展目录固定为**宿主运行目录** `ExternalLibs\`（`AppContext.BaseDirectory`），清单文件 `ExternalLibs\whitelist.json` | 与插件 `Modules` 目录同范式；部署时一起分发，路径零配置 |
| **fail-closed 默认拒绝**：目录不存在=未启用（静默跳过）；目录存在但清单缺失/解析失败/`enabled` 非 true/未登记/哈希不符 → 一律不加载并写审计 | 安全机制的缺省状态必须是"关"，配置错误不能变成放开 |
| **SHA256 哈希锁**：清单登记值 ≠ 文件实际哈希 → 拒载 + ERROR 审计 | 防"登记后被替换"的影子攻击；只认内容不认文件名 |
| **防影子替换同名库**：扩展 dll 若与宿主 TPA/已加载程序集同名，不加载扩展副本，INFO 提示以宿主版本为准 | halcondotnet 版本冲突的历史教训——副本压主库是最难查的一类事故 |
| 清单解析用 **Newtonsoft.Json（JObject）** | 宿主链已有 13.0.4（dgspec/assets 实测确认），零新增依赖 |
| **编译引用 ≠ 运行加载**两件事都要做：`MetadataReference.CreateFromFile` 喂编译器 + `AssemblyLoadContext.Default.Resolving` 按简单名从白名单路径兜底 | Roslyn 引用只解决"编译找得到类型"；脚本真正执行到该类型时 CLR 还要运行期加载，非 probing 路径必须挂 Resolving。**且只对白名单通过项挂载**——被拒的 dll 连运行期搭便车的机会都没有 |
| 静态引擎拿不到 `IExecutionContext`，审计采用**暂存 + 首次执行投递**：引擎内 `ExternalAudit` 列表，插件 `RunAlgorithm` 里 `TakeExternalLibsAudit()`（取走即清空）分级写给 `context.Logger` | 白名单扫描发生在 `GetOptions()`（首次编译时），无日志通道；`ILogService` 双通道（UI 面板+落盘）现成，投给它即免费获得持久化 |
| 引用集**进程级缓存**不变，改白名单需重启宿主生效 | 与编译缓存同一套生命周期逻辑；MVP 不做热重载（见已知边界） |

### whitelist.json 格式

```json
{
  "enabled": true,
  "remark": "总开关；false 或缺省 = 全部扩展库不加载",
  "libraries": [
    {
      "file": "SomeLib.dll",
      "sha256": "64位十六进制小写，必须与文件实际内容一致",
      "approvedBy": "批准人/部门",
      "purpose": "用途说明，出问题时回溯用"
    }
  ]
}
```

### 登记流程（给使用者的三步）

1. 把 dll 扔进宿主目录 `ExternalLibs\`；
2. 跑一次任意 C# 脚本，日志里会 WARN 出该 dll 的**真实 SHA256**（未登记提示直接带哈希），复制；
3. 粘进 `whitelist.json` 登记（含批准人/用途），**重启宿主**生效，日志转 INFO"校验通过"。

---

## 二、修改文件清单

| 文件 | 修改 |
|------|------|
| `Plugins\Plugin.CSharpScript\CSharpScriptEngine.cs` | ① usings 增 `Newtonsoft.Json.Linq`、`System.Runtime.Loader`；② `GetOptions()` 里 `AddAssembly` 同步收集 `hostSimpleNames`（防影子名单），TPA+已加载扫描后新增第 3 步调用 `LoadWhitelistedExtensions(refs, hostSimpleNames)`；③ 新增区段：`ExternalLibSeverity` 枚举、`ExternalAudit`+`AuditLock` 暂存、`ExternalLibPaths` 映射、`TakeExternalLibsAudit()`、`LoadWhitelistedExtensions()`（开关/清单/未登记/同名/哈希锁/审计全链路）、`HookResolving()`（只挂一次、只挂通过项）、`ComputeFileSha256()` |
| `Plugins\Plugin.CSharpScript\CSharpScriptPlugin.cs` | `RunAlgorithm` 编译之后插入审计投递循环：`TakeExternalLibsAudit()` 按 Error/Warn/Info 分级写 `context.Logger`（取走即清空，全局天然只投一次） |

引擎核心判定顺序（每项 dll）：未登记 → WARN+真实哈希；与宿主同名 → INFO 跳过；哈希缺失或不符 → ERROR 拒载；通过 → 加编译引用 + 登记运行期路径 + INFO。

---

## 三、验证结果

1. **编译**：`Plugin.CSharpScript` Debug（完整构建，DLL 已拷入 `Modules`）与 Release 单工程均 **0 错误**（Release 报的 565 警告全部来自 UI/Controls 既有代码，与本次无关）。
2. **白名单冒烟**（临时控制台工程 `_tmp_roslyn_probe`，用 Roslyn 现场 `Emit` 三个假"第三方 dll"+程序化生成 whitelist.json，全过后已删除）——`=== 9 通过 / 0 失败 ===`，退出码 0：
   - R1 引擎回归：编译+执行+SetVar/GetVar 往返 ✅（白名单改动不影响既有能力）
   - R2 `Context.Fail()` 契约 ✅
   - W1 白名单 dll **编译+运行全链路**可用（脚本真调到了扩展库方法，证明 Resolving 兜底生效）✅
   - W2 篡改 dll（登记但哈希不符）→ 拒载 → 引用其类型的脚本编译必败 ✅
   - W3 未登记 dll → 默认拒绝 → 编译必败 ✅
   - W4 审计三态齐备：放行 INFO（含批准人+哈希前缀）/ 未登记 WARN（含 64 位真实哈希，可一键复制登记）/ 篡改 ERROR（清单哈希 vs 实际哈希并排展示）✅
   - W5/W6 **enabled:false 场景**：因引用集进程级缓存，用 `--off` 子进程独立验证——已登记 dll 也一律不加载、审计含手动关闭提示，子进程全项通过 ✅
3. 审计输出实例（真实日志样式）：
   - `[Info] [扩展库白名单] 校验通过，已加入脚本引用：SmokeGoodLib.dll（批准人：冒烟测试，哈希：a67cf6b08b54…）`
   - `[Error] [扩展库白名单] 哈希不符，疑似被篡改，拒绝加载：SmokeTamperedLib.dll（清单=0000… 实际=d5cdf71d…）`
   - `[Warning] [扩展库白名单] 发现未登记 dll，已忽略：SmokeUnlistedLib.dll（实际 SHA256=b19e4316…，如确需使用请登记进 whitelist.json）`

---

## 四、已知边界

| 项 | 说明 |
|----|------|
| 改动白名单需**重启宿主** | 引用集 `_options` 进程级懒加载终身缓存（与脚本编译缓存同一设计）；热重载（监视 whitelist.json 变更→重建引用集→清脚本缓存）留待后续 |
| 白名单管 dll，**管不住脚本正文** | 脚本本身仍可 `Process.Start`/读写文件（无沙箱，MVP 明确不做）；正文纪律靠**方案文件进版本管理**兜底 |
| 审计只投一次 | 引用集只扫一遍，审计记录随宿主首次脚本执行投递进日志后清空；历史审计看落盘日志文件 |
| `enabled` 语义从严 | 缺省、写成字符串/数字都按"关闭"处理（fail-closed），必须显式 `true` |
| 清单 file 字段只认文件名 | 解析时经 `Path.GetFileName` 归一，防 `..\` 路径穿越指向目录外 |
| 依赖闭包不自动跟进 | 扩展库若还依赖其它私有 dll，运行期 Resolving 只认白名单路径表；其依赖也需逐个登记（这是纪律，不是 bug） |
| 宿主实跑未验证 | 冒烟在独立进程模拟（smoke.exe 自身目录当宿主目录）；生产首次使用时按"三步登记流程"在真宿主上走一遍即可 |
| 与既有问题无涉 | HslCommunication Release 缺 Newtonsoft 类型、`Modules` 被运行中 VisionMaster.exe 锁定——均为既有环境性问题，未在本次处理 |
