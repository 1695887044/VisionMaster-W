using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Plugin.ResultUpload;
using VisionMaster.Models;
using VisionMaster.Services;
// 裸写 ExecutionContext 会与 System.Threading.ExecutionContext 撞名（两个宿主都启用了隐式 using）
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 结果上报插件的行为断言（P0-B）。
    ///
    /// 覆盖的是"结果落地"最容易静默出错的几面：
    ///  - 字段表 → JSON 的类型保真（double 不能变字符串）；
    ///  - 上报时机（每次/仅NG/触发端口）真的在拦；
    ///  - HTTP 重试、幂等 ID、断网续传（spool）不丢件、不重复；
    ///  - 业务成功关键字、阻断契约、加签/自定义头。
    ///
    /// 不测"网络库本身"：本地服务器是裸 TCP 的极简 HTTP，只验插件发给它的东西。
    /// </summary>
    internal static class ResultUploadChecks
    {
        public static void Run()
        {
            DataPipelineCheckSupport.Section("结果上报：字段表 / 时机 / 重试 / 断网续传");

            DataPipelineCheckSupport.SafeRun("字段表类型", FieldTableTypes);
            DataPipelineCheckSupport.SafeRun("变量来源覆盖全局/通讯与数组格式（N2）", VariableSourceCoversSharedRegistry);
            DataPipelineCheckSupport.SafeRun("缺名变量 WARN 有上限（N2）", MissingVariableWarnsBounded);
            DataPipelineCheckSupport.SafeRun("仅NG时机", OnlyNgTiming);
            DataPipelineCheckSupport.SafeRun("触发端口时机", TriggerTiming);
            DataPipelineCheckSupport.SafeRun("重试与幂等ID", RetryAndIdempotencyId);
            DataPipelineCheckSupport.SafeRun("4xx 不重试（永久性错误）", Permanent4xxNoRetry);
            DataPipelineCheckSupport.SafeRun("业务关键字与阻断", SuccessKeywordAndBlocking);
            DataPipelineCheckSupport.SafeRun("异步不阻塞流程", AsyncDoesNotBlockFlow);
            DataPipelineCheckSupport.SafeRun("断网续传", SpoolAndDrain);
            DataPipelineCheckSupport.SafeRun("自定义头与加签", HeadersAndDingTalkSign);
            DataPipelineCheckSupport.SafeRun("钉钉文本模板", DingTalkTextTemplate);
        }

        // ------------------------------------------------------------------
        //  用例
        // ------------------------------------------------------------------

        private static void FieldTableTypes()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.Fields.Add(new UploadFieldDef { Name = "value", Source = FieldSource.Port });
                p.Fields.Add(new UploadFieldDef { Name = "flag", Source = FieldSource.Port });
                p.Fields.Add(new UploadFieldDef { Name = "time", Source = FieldSource.BuiltIn, BuiltIn = BuiltInField.Timestamp });
            });

            SetPort(plugin, "sn", "SN001");
            SetPort(plugin, "value", 12.3d);
            SetPort(plugin, "flag", true);

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("字段表：一次执行发一条请求", server.RequestCount == 1, $"{server.RequestCount}");
            DataPipelineCheckSupport.Check("字段表：步骤成功", plugin.Success.Value is true, $"{plugin.Success.Value}");

            var req = server.Requests.FirstOrDefault();
            if (req == null)
            {
                DataPipelineCheckSupport.Check("字段表：拿到请求体（后续判据前提）", false, "没有请求");
                return;
            }

            using var doc = JsonDocument.Parse(req.Body);
            var root = doc.RootElement;
            DataPipelineCheckSupport.Check("字段表：字符串字段仍是字符串", root.GetProperty("sn").ValueKind == JsonValueKind.String, root.GetProperty("sn").ToString());
            DataPipelineCheckSupport.Check("字段表：double 字段仍是数字（12.3 不是 \"12.3\"）",
                root.GetProperty("value").ValueKind == JsonValueKind.Number && Math.Abs(root.GetProperty("value").GetDouble() - 12.3) < 1e-9,
                root.GetProperty("value").ToString());
            DataPipelineCheckSupport.Check("字段表：bool 字段仍是布尔", root.GetProperty("flag").ValueKind is JsonValueKind.True or JsonValueKind.False, root.GetProperty("flag").ToString());
            DataPipelineCheckSupport.Check("字段表：内置时间戳非空", root.GetProperty("time").GetString() is { Length: > 0 }, root.GetProperty("time").ToString());
            DataPipelineCheckSupport.Check("字段表：自动带幂等头 X-Upload-Id", req.Header("X-Upload-Id") is { Length: 32 }, req.Header("X-Upload-Id") ?? "(空)");
            DataPipelineCheckSupport.Check("字段表：Content-Type 是 application/json",
                (req.Header("Content-Type") ?? "").Contains("application/json", StringComparison.OrdinalIgnoreCase),
                req.Header("Content-Type") ?? "(空)");
        }

        /// <summary>
        /// N2：来源=变量不再只认「本轮局部变量」——取不到时兜底到共享变量注册表
        /// （全局变量 / 通讯变量 / 系统变量共用一张表），数组按 <c>[a,b,c]</c> 转文本
        /// （不落 <c>System.Int32[]</c>）。
        ///
        /// 为什么用**真 WorkspaceContext + 真 VariableRegistry**：生产链路是
        /// <c>GlobalVariableWriter.TryRead → VariableRegistry.FindByName → IVariable.Value</c>，
        /// 桩掉这一段就把"注册表到底装没装全局/通讯变量"排除在断言之外了。
        /// </summary>
        private static void VariableSourceCoversSharedRegistry()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "okCount", Source = FieldSource.Variable, VariableName = "OKCount" });
                p.Fields.Add(new UploadFieldDef { Name = "speed", Source = FieldSource.Variable, VariableName = "PLC1.Speed" });
                p.Fields.Add(new UploadFieldDef { Name = "arr", Source = FieldSource.Variable, VariableName = "Arr" });
            });

            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();
            workspace.GlobalVariables.Add(
                VariableFactory.CreateLocal("OKCount", typeof(int), "全局计数", 42));

            // 通讯变量：注册表只看 IVariable.Value（通信线程维护的镜像）；
            // 没有地址/管理器时 Value 的 setter 不会写设备，正好当镜像值用。
            workspace.GlobalVariables.Add(new NetworkVariableModel
            {
                Name = "PLC1.Speed",
                ConnectionName = "PLC1",
                DataType = typeof(double),
                Value = 12.5
            });

            var log = new StubLog();
            var ctx = new ExecutionContext(log, new FlowSession { FlowName = "数据链路断言" }, workspace, CancellationToken.None);
            ctx.LocalVariables["Arr"] = new[] { 1, 2, 3 }; // 局部池放数组：验报文里的格式化

            plugin.Execute(ctx);

            var req = server.Requests.FirstOrDefault();
            if (req == null)
            {
                DataPipelineCheckSupport.Check("结果上报 N2：拿到请求体（后续判据前提）", false, "没有请求");
                return;
            }

            using var doc = JsonDocument.Parse(req.Body);
            var root = doc.RootElement;

            DataPipelineCheckSupport.Check("结果上报 N2：变量字段取到全局变量（数值仍保真）",
                root.TryGetProperty("okCount", out var ok) && ok.ValueKind == JsonValueKind.Number && ok.GetInt32() == 42,
                root.TryGetProperty("okCount", out var okDetail) ? okDetail.ToString() : "(缺键)");
            DataPipelineCheckSupport.Check("结果上报 N2：变量字段取到通讯变量（网络变量镜像值）",
                root.TryGetProperty("speed", out var speed)
                && speed.ValueKind == JsonValueKind.Number
                && Math.Abs(speed.GetDouble() - 12.5) < 1e-9,
                root.TryGetProperty("speed", out var speedDetail) ? speedDetail.ToString() : "(缺键)");
            DataPipelineCheckSupport.Check("结果上报 N2：数组字段格式化成 [1,2,3]（不是 System.Int32[]）",
                root.TryGetProperty("arr", out var arr)
                && arr.ValueKind == JsonValueKind.String
                && arr.GetString() == "[1,2,3]",
                root.TryGetProperty("arr", out var arrDetail) ? arrDetail.ToString() : "(缺键)");
            DataPipelineCheckSupport.Check("结果上报 N2：三个变量都取到时不留缺名 WARN",
                log.Warns.All(w => !w.Contains("既不在本轮局部变量里", StringComparison.Ordinal)),
                log.Warns.Count == 0 ? "(无)" : string.Join(" | ", log.Warns));
        }

        /// <summary>
        /// N2：缺名变量必须留 WARN（不是静默发空），但同一名字最多 3 条——高频上报里
        /// "每轮都找不到"会把日志刷满，反而淹没真正的问题。
        /// </summary>
        private static void MissingVariableWarnsBounded()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef
                {
                    Name = "missing",
                    Source = FieldSource.Variable,
                    VariableName = "NoSuchVariableN2"
                });
            });

            var log = new StubLog();
            var ctx = DataPipelineCheckSupport.NewContext(log);

            // 同一个插件实例跑 4 轮：提示计数挂在实例上（跨执行累计）
            for (int i = 0; i < 4; i++)
                plugin.Execute(ctx);

            int warns = log.Warns.Count(w => w.Contains("NoSuchVariableN2", StringComparison.Ordinal));
            DataPipelineCheckSupport.Check("结果上报 N2：缺名变量留 WARN（不是静默发空）",
                warns > 0, log.Warns.Count == 0 ? "(无 WARN)" : string.Join(" | ", log.Warns));
            DataPipelineCheckSupport.Check("结果上报 N2：缺名 WARN 有上限（4 轮只提示 3 条）",
                warns == 3, $"{warns} 条");
        }

        private static void OnlyNgTiming()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "result", Source = FieldSource.Port });
                p.SendTiming = SendTiming.OnlyNG;
                p.JudgeFieldName = "result";
                p.NgValue = "NG";
            });

            SetPort(plugin, "result", "OK");
            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            DataPipelineCheckSupport.Check("仅NG：OK 件不发请求", server.RequestCount == 0, $"{server.RequestCount}");
            DataPipelineCheckSupport.Check("仅NG：OK 件不阻断（Success=true）", plugin.Success.Value is true, $"{plugin.Success.Value}");

            SetPort(plugin, "result", "NG");
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            DataPipelineCheckSupport.Check("仅NG：NG 件发一条请求", server.RequestCount == 1, $"{server.RequestCount}");
        }

        private static void TriggerTiming()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.SendTiming = SendTiming.OnTrigger;
            });
            SetPort(plugin, "sn", "SN002");

            var log = new StubLog();
            plugin.TriggerPort.Value = false;
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            DataPipelineCheckSupport.Check("触发端口：false 不发", server.RequestCount == 0, $"{server.RequestCount}");

            plugin.TriggerPort.Value = true;
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            DataPipelineCheckSupport.Check("触发端口：true 发一条", server.RequestCount == 1, $"{server.RequestCount}");
        }

        private static void RetryAndIdempotencyId()
        {
            int calls = 0;
            using var server = new LocalHttpServer(_ =>
                Interlocked.Increment(ref calls) <= 2
                    ? LocalHttpResponse.Status(500, "boom")
                    : LocalHttpResponse.Ok());

            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.RetryCount = 2;
                p.TimeoutMs = 1000;
            });
            SetPort(plugin, "sn", "SN003");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("重试：两次 500 后成功（总请求 3 次）", server.RequestCount == 3, $"{server.RequestCount}");
            DataPipelineCheckSupport.Check("重试：步骤最终成功", plugin.Success.Value is true, $"{plugin.Success.Value}");
            DataPipelineCheckSupport.Check("重试：IsUploaded=true", plugin.IsUploaded.Value is true, $"{plugin.IsUploaded.Value}");

            var ids = server.Requests.Select(r => r.Header("X-Upload-Id")).Distinct(StringComparer.Ordinal).ToList();
            DataPipelineCheckSupport.Check("重试：三次尝试共用同一个幂等 ID", ids.Count == 1 && ids[0] is { Length: 32 }, string.Join(",", ids));
        }

        /// <summary>
        /// 4xx（除 408 超时 / 429 限流）= 请求本身有问题（令牌错 / 字段被拒 / 路径不存在）→ **不重试**，
        /// 也不进断网续传账本（2026-10-22 用户裁决 B4）。
        ///
        /// 为什么这条值得单列：旧实现对任何非 2xx 都按 RetryCount 重试 3 次 ——
        /// 令牌填错时现场只看到"上报失败（已重试 3 次）"，白等 3 秒退避、把服务端压力放大 4 倍，
        /// 真正的原因（401 令牌无效）还被埋在最后一条里。分档之后 401 只发 1 次、原因直接说"检查访问令牌"。
        /// 反向仍由上面 <see cref="RetryAndIdempotencyId"/> 的 5xx 用例钉住（500 两次后成功 = 重试还在）。
        /// </summary>
        private static void Permanent4xxNoRetry()
        {
            string spoolDir = DataPipelineCheckSupport.TempDir("upload_4xx");
            UploadQueue.ConfigureForTests(spoolDir);

            using var server = new LocalHttpServer(_ => LocalHttpResponse.Status(401, "unauthorized"));

            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.RetryCount = 3;        // 若仍按旧策略重试，这里会打出 4 次请求（判据立刻红）
                p.TimeoutMs = 1000;
                p.BlockOnFailure = true; // 让失败原因落到 ErrorMessage（现场看到的就是这句）
                p.SpoolEnabled = true;   // 永久性错误不该进补传账本
            });
            SetPort(plugin, "sn", "SN003b");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("★4xx（401）不重试：只发 1 次请求（旧实现会发 1+RetryCount=4 次）",
                server.RequestCount == 1, $"{server.RequestCount}");

            string err = plugin.ErrorMessage.Value as string ?? "";
            DataPipelineCheckSupport.Check("4xx 失败原因带状态码 + 令牌提示 + 不重试（现场知道该改哪里，不是去查网络）",
                err.Contains("401", StringComparison.Ordinal)
                && err.Contains("访问令牌", StringComparison.Ordinal)
                && err.Contains("不重试", StringComparison.Ordinal),
                err.Length > 0 ? err : "(空)");

            DataPipelineCheckSupport.Check("4xx 失败如实回写端口：IsUploaded=false / StatusCode=401",
                plugin.IsUploaded.Value is false && plugin.StatusCode.Value is int statusCode && statusCode == 401,
                $"IsUploaded={plugin.IsUploaded.Value} 状态码={plugin.StatusCode.Value}");

            string spool = UploadQueue.SpoolFilePathForTests;
            DataPipelineCheckSupport.Check("★4xx 不进断网续传账本（401 不会自己好，补传只会每 30 秒撞一次墙）",
                !File.Exists(spool) || new FileInfo(spool).Length == 0,
                File.Exists(spool) ? $"{new FileInfo(spool).Length} 字节" : "(无文件)");
        }

        private static void SuccessKeywordAndBlocking()
        {
            using (var server = new LocalHttpServer(_ => LocalHttpResponse.Ok("NO")))
            {
                var plugin = NewPlugin(server, p =>
                {
                    p.Fields.Clear();
                    p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                    p.SuccessKeyword = "OK";
                });
                SetPort(plugin, "sn", "SN004");

                var log = new StubLog();
                plugin.Execute(DataPipelineCheckSupport.NewContext(log));
                DataPipelineCheckSupport.Check("业务关键字：200 但响应不含关键字 → IsUploaded=false",
                    plugin.IsUploaded.Value is false, $"{plugin.IsUploaded.Value}");
                DataPipelineCheckSupport.Check("业务关键字：未勾阻断时步骤仍成功（宁漏报不停线）",
                    plugin.Success.Value is true, $"{plugin.Success.Value}");
            }

            using (var server = new LocalHttpServer(_ => LocalHttpResponse.Ok("NO")))
            {
                var plugin = NewPlugin(server, p =>
                {
                    p.Fields.Clear();
                    p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                    p.SuccessKeyword = "OK";
                    p.BlockOnFailure = true;
                });
                SetPort(plugin, "sn", "SN005");

                var log = new StubLog();
                plugin.Execute(DataPipelineCheckSupport.NewContext(log));
                DataPipelineCheckSupport.Check("业务关键字：勾阻断后步骤失败", plugin.Success.Value is false, $"{plugin.Success.Value}");
                DataPipelineCheckSupport.Check("业务关键字：失败原因指向关键字",
                    (plugin.ErrorMessage.Value as string ?? "").Contains("结果上报", StringComparison.Ordinal),
                    plugin.ErrorMessage.Value as string ?? "(空)");
            }
        }

        private static void AsyncDoesNotBlockFlow()
        {
            using var server = new LocalHttpServer(_ => new LocalHttpResponse { StatusCode = 200, Body = "OK", DelayMs = 150 });
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.AsyncMode = true;
            });
            SetPort(plugin, "sn", "SN006");

            var log = new StubLog();
            var sw = Stopwatch.StartNew();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            sw.Stop();

            DataPipelineCheckSupport.Check("异步模式：步骤不等服务器（<1000ms，服务器延迟 150ms）",
                sw.ElapsedMilliseconds < 1000, $"{sw.ElapsedMilliseconds}ms");
            DataPipelineCheckSupport.Check("异步模式：最终收到成功回调",
                DataPipelineCheckSupport.WaitUntil(() => plugin.IsUploaded.Value is true, 5000),
                $"{plugin.IsUploaded.Value}");
            DataPipelineCheckSupport.Check("异步模式：请求确实发出", server.RequestCount == 1, $"{server.RequestCount}");
        }

        private static void SpoolAndDrain()
        {
            string spoolDir = DataPipelineCheckSupport.TempDir("upload_spool");
            UploadQueue.ConfigureForTests(spoolDir);

            bool fail = true;
            using var server = new LocalHttpServer(_ =>
                fail ? LocalHttpResponse.Status(500, "down") : LocalHttpResponse.Ok());

            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.SpoolEnabled = true;
                p.RetryCount = 0;
                p.TimeoutMs = 1000;
            });
            SetPort(plugin, "sn", "SN007");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            string spool = UploadQueue.SpoolFilePathForTests;
            DataPipelineCheckSupport.Check("断网续传：失败后 spool 落盘 1 条",
                File.Exists(spool) && File.ReadAllLines(spool).Count(l => l.Trim().Length > 0) == 1,
                File.Exists(spool) ? $"{File.ReadAllLines(spool).Length} 行" : "(无文件)");

            string firstId = server.Requests.FirstOrDefault()?.Header("X-Upload-Id");
            DataPipelineCheckSupport.Check("断网续传：首发带幂等 ID", firstId is { Length: 32 }, firstId ?? "(空)");

            fail = false;
            UploadQueue.DrainSpoolForTests();

            DataPipelineCheckSupport.Check("断网续传：补传成功后 spool 清空",
                DataPipelineCheckSupport.WaitUntil(
                    () => !File.Exists(spool) || new FileInfo(spool).Length == 0, 5000),
                File.Exists(spool) ? $"{new FileInfo(spool).Length} 字节" : "(已删除)");
            DataPipelineCheckSupport.Check("断网续传：补传请求确实发出（总请求 2 次）", server.RequestCount == 2, $"{server.RequestCount}");

            string replayId = server.Requests.LastOrDefault()?.Header("X-Upload-Id");
            DataPipelineCheckSupport.Check("断网续传：补传沿用原幂等 ID", replayId == firstId, $"{firstId} → {replayId}");
        }

        private static void HeadersAndDingTalkSign()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.Headers.Add(new HttpHeaderDef { Key = "Authorization", Value = "Bearer test" });
                p.SignSecret = "SECtest";
            });
            SetPort(plugin, "sn", "SN008");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            var req = server.Requests.FirstOrDefault();
            DataPipelineCheckSupport.Check("自定义头：Authorization 原样发出",
                req?.Header("Authorization") == "Bearer test", req?.Header("Authorization") ?? "(空)");
            DataPipelineCheckSupport.Check("加签：URL 带 timestamp 与 sign",
                req != null && req.Path.Contains("timestamp=", StringComparison.Ordinal) &&
                req.Path.Contains("sign=", StringComparison.Ordinal),
                req?.Path ?? "(空)");
        }

        private static void DingTalkTextTemplate()
        {
            using var server = new LocalHttpServer(_ => LocalHttpResponse.Ok());
            var plugin = NewPlugin(server, p =>
            {
                p.PayloadKind = PayloadKind.DingTalkText;
                p.Fields.Clear();
                p.Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
                p.Fields.Add(new UploadFieldDef { Name = "result", Source = FieldSource.Port });
                p.MessageTemplate = "件{sn}判定{result}";
            });
            SetPort(plugin, "sn", "SN009");
            SetPort(plugin, "result", "NG");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            var req = server.Requests.FirstOrDefault();
            if (req == null)
            {
                DataPipelineCheckSupport.Check("钉钉文本：拿到请求体（后续判据前提）", false, "没有请求");
                return;
            }

            using var doc = JsonDocument.Parse(req.Body);
            DataPipelineCheckSupport.Check("钉钉文本：msgtype=text", doc.RootElement.GetProperty("msgtype").GetString() == "text", req.Body);
            DataPipelineCheckSupport.Check("钉钉文本：content 占位符已展开",
                doc.RootElement.GetProperty("text").GetProperty("content").GetString() == "件SN009判定NG",
                req.Body);
        }

        // ------------------------------------------------------------------
        //  辅助
        // ------------------------------------------------------------------

        private static ResultUploadPlugin NewPlugin(LocalHttpServer server, Action<ResultUploadPlugin> configure)
        {
            var plugin = new ResultUploadPlugin();
            plugin.Initialize(new CheckStepData { StepName = "上报" });
            plugin.Url = server.Url;
            plugin.PayloadKind = PayloadKind.FieldTable;
            plugin.AsyncMode = false;
            plugin.RetryCount = 0;
            plugin.TimeoutMs = 2000;
            plugin.Fields.Clear();
            configure?.Invoke(plugin);
            return plugin;
        }

        private static void SetPort(ResultUploadPlugin plugin, string name, object value)
        {
            if (plugin.Inputs.TryGetValue(name, out var port))
                port.Value = value;
        }
    }
}
