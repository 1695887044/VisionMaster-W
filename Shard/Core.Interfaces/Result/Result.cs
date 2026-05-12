using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces.Result
{
    public class Result
    {
        public bool Success { get; set; }

        public int Code { get; set; }

        public string Message { get; set; }

        public static Result Ok(string message = "操作成功") => new()
        {
            Success = true,
            Code = 200,
            Message = message
        };
        public static Result NG(string message, int code = 500) => new()
        {
            Success = false,
            Code = code,
            Message = message
        };
    }
    public class Result<T> : Result
    {
        public T Data { get; set; }
        public static Result<T> Ok(T data, string message = "操作成功") => new()
        {
            Success = true,
            Code = 200,
            Message = message,
            Data = data
        };
        public new static Result<T> NG(string message, int code = 500) => new()
        {
            Success = false,
            Code = code,
            Message = message
        };

    }
}
