using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Plugin.ResultUpload;

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
            DataPipelineCheckSupport.SafeRun("仅NG时机", OnlyNgTiming);
            DataPipelineCheckSupport.SafeRun("触发端口时机", TriggerTiming);
            DataPipelineCheckSupport.SafeRun("重试与幂等ID", RetryAndIdempotencyId);
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
