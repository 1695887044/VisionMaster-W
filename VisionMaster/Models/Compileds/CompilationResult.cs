﻿﻿﻿using Core.Interfaces.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译结果类
    /// 继承自通用结果类，包含编译后的流程和错误列表
    /// </summary>
    public class CompilationResult : Result<CompiledFlow>
    {
        /// <summary>
        /// 编译错误列表
        /// </summary>
        public List<string> Errors { get; } = new();

        /// <summary>
        /// 创建成功的编译结果
        /// </summary>
        public static new CompilationResult Ok(CompiledFlow flow)
        {
            return new CompilationResult { Success = true, Data = flow };
        }

        /// <summary>
        /// 创建失败的编译结果
        /// </summary>
        public static new CompilationResult NG(string message)
        {
            var result = new CompilationResult { Success = false };
            result.Errors.Add(message);
            return result;
        }
    }
}
