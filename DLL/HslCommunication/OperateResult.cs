using System;

namespace HslCommunication
{
	/// <summary>
	/// 操作结果的类，只带有成功标志和错误信息<br />
	/// The class that operates the result, with only success flags and error messages
	/// </summary>
	/// <remarks>
	/// 当 <see cref="P:HslCommunication.OperateResult.IsSuccess" /> 为 True 时，忽略 <see cref="P:HslCommunication.OperateResult.Message" /> 及 <see cref="P:HslCommunication.OperateResult.ErrorCode" /> 的值
	/// </remarks>
	public class OperateResult
	{
		/// <summary>
		/// 指示本次操作是否成功。<br />
		/// Indicates whether this operation was successful.
		/// </summary>
		public bool IsSuccess { get; set; }

		/// <summary>
		/// 具体的错误描述。<br />
		/// Specific error description.
		/// </summary>
		public string Message { get; set; } = StringResources.Language.UnknownError;


		/// <summary>
		/// 具体的错误代码。<br />
		/// The specific error code.
		/// </summary>
		/// <remarks>
		/// 默认的错误码值为 10000，此时需要看 <see cref="P:HslCommunication.OperateResult.Message" /> 的消息来查看错误信息，如果由于网络问题导致的错误，错误码小于 0，
		/// 其他情况的错误都是对应协议的错误码，有些错误码的描述信息可以看 <see cref="P:HslCommunication.OperateResult.Message" />，如果消息也是未知的话，此时需要根据错误码找对应协议的手册来确认错误消息。<br />
		/// The default error code value is 10000. In this case, you need to view the <see cref="P:HslCommunication.OperateResult.Message" /> message to view the error information. 
		/// If the error code is less than 0 due to network problems, the error code in other cases is the error code of the corresponding protocol. 
		/// You can see <see cref="P:HslCommunication.OperateResult.Message" /> for the description of some error codes. If the message is also unknown, you need to refer to the manual of the corresponding protocol to confirm the error message.
		/// </remarks>
		public int ErrorCode { get; set; } = 10000;


		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
		{
			Message = msg;
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
		{
			ErrorCode = err;
			Message = msg;
		}

		/// <summary>
		/// 获取错误代号及文本描述。<br />
		/// Get the error code and text description.
		/// </summary>
		/// <returns>包含错误码及错误消息</returns>
		public string ToMessageShowString()
		{
			return $"{StringResources.Language.ErrorCode}:{ErrorCode}{Environment.NewLine}{StringResources.Language.TextDescription}:{Message}";
		}

		/// <summary>
		/// 从另一个结果类中拷贝错误信息，主要是针对错误码和错误消息。<br />
		/// Copy error information from another result class, mainly for error codes and error messages.
		/// </summary>
		/// <typeparam name="TResult">支持结果类及派生类</typeparam>
		/// <param name="result">结果类及派生类的对象</param>
		public void CopyErrorFromOther<TResult>(TResult result) where TResult : OperateResult
		{
			if (result != null)
			{
				ErrorCode = result.ErrorCode;
				Message = result.Message;
			}
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T">结果类型</typeparam>
		/// <param name="content">如果操作成功将赋予的结果内容</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T> Convert<T>(T content)
		{
			return IsSuccess ? CreateSuccessResult(content) : CreateFailedResult<T>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T">结果类型</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T> ConvertFailed<T>()
		{
			return CreateFailedResult<T>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2> Convert<T1, T2>(T1 content1, T2 content2)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2) : CreateFailedResult<T1, T2>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2> ConvertFailed<T1, T2>()
		{
			return CreateFailedResult<T1, T2>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3> Convert<T1, T2, T3>(T1 content1, T2 content2, T3 content3)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3) : CreateFailedResult<T1, T2, T3>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3> ConvertFailed<T1, T2, T3>()
		{
			return CreateFailedResult<T1, T2, T3>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4> Convert<T1, T2, T3, T4>(T1 content1, T2 content2, T3 content3, T4 content4)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4) : CreateFailedResult<T1, T2, T3, T4>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4> ConvertFailed<T1, T2, T3, T4>()
		{
			return CreateFailedResult<T1, T2, T3, T4>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <param name="content5">如果操作成功将赋予的结果内容五</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5> Convert<T1, T2, T3, T4, T5>(T1 content1, T2 content2, T3 content3, T4 content4, T5 content5)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4, content5) : CreateFailedResult<T1, T2, T3, T4, T5>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5> ConvertFailed<T1, T2, T3, T4, T5>()
		{
			return CreateFailedResult<T1, T2, T3, T4, T5>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <param name="content5">如果操作成功将赋予的结果内容五</param>
		/// <param name="content6">如果操作成功将赋予的结果内容六</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6> Convert<T1, T2, T3, T4, T5, T6>(T1 content1, T2 content2, T3 content3, T4 content4, T5 content5, T6 content6)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4, content5, content6) : CreateFailedResult<T1, T2, T3, T4, T5, T6>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6> ConvertFailed<T1, T2, T3, T4, T5, T6>()
		{
			return CreateFailedResult<T1, T2, T3, T4, T5, T6>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <param name="content5">如果操作成功将赋予的结果内容五</param>
		/// <param name="content6">如果操作成功将赋予的结果内容六</param>
		/// <param name="content7">如果操作成功将赋予的结果内容七</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7> Convert<T1, T2, T3, T4, T5, T6, T7>(T1 content1, T2 content2, T3 content3, T4 content4, T5 content5, T6 content6, T7 content7)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4, content5, content6, content7) : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7> ConvertFailed<T1, T2, T3, T4, T5, T6, T7>()
		{
			return CreateFailedResult<T1, T2, T3, T4, T5, T6, T7>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <param name="content5">如果操作成功将赋予的结果内容五</param>
		/// <param name="content6">如果操作成功将赋予的结果内容六</param>
		/// <param name="content7">如果操作成功将赋予的结果内容七</param>
		/// <param name="content8">如果操作成功将赋予的结果内容八</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> Convert<T1, T2, T3, T4, T5, T6, T7, T8>(T1 content1, T2 content2, T3 content3, T4 content4, T5 content5, T6 content6, T7 content7, T8 content8)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4, content5, content6, content7, content8) : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> ConvertFailed<T1, T2, T3, T4, T5, T6, T7, T8>()
		{
			return CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <typeparam name="T9">泛型参数九</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <param name="content5">如果操作成功将赋予的结果内容五</param>
		/// <param name="content6">如果操作成功将赋予的结果内容六</param>
		/// <param name="content7">如果操作成功将赋予的结果内容七</param>
		/// <param name="content8">如果操作成功将赋予的结果内容八</param>
		/// <param name="content9">如果操作成功将赋予的结果内容九</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> Convert<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 content1, T2 content2, T3 content3, T4 content4, T5 content5, T6 content6, T7 content7, T8 content8, T9 content9)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4, content5, content6, content7, content8, content9) : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <typeparam name="T9">泛型参数九</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> ConvertFailed<T1, T2, T3, T4, T5, T6, T7, T8, T9>()
		{
			return CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，如果当前结果为失败，则返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// if the current result is a failure, then return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <typeparam name="T9">泛型参数九</typeparam>
		/// <typeparam name="T10">泛型参数十</typeparam>
		/// <param name="content1">如果操作成功将赋予的结果内容一</param>
		/// <param name="content2">如果操作成功将赋予的结果内容二</param>
		/// <param name="content3">如果操作成功将赋予的结果内容三</param>
		/// <param name="content4">如果操作成功将赋予的结果内容四</param>
		/// <param name="content5">如果操作成功将赋予的结果内容五</param>
		/// <param name="content6">如果操作成功将赋予的结果内容六</param>
		/// <param name="content7">如果操作成功将赋予的结果内容七</param>
		/// <param name="content8">如果操作成功将赋予的结果内容八</param>
		/// <param name="content9">如果操作成功将赋予的结果内容九</param>
		/// <param name="content10">如果操作成功将赋予的结果内容十</param>
		/// <returns>最终的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Convert<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 content1, T2 content2, T3 content3, T4 content4, T5 content5, T6 content6, T7 content7, T8 content8, T9 content9, T10 content10)
		{
			return IsSuccess ? CreateSuccessResult(content1, content2, content3, content4, content5, content6, content7, content8, content9, content10) : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(this);
		}

		/// <summary>
		/// 将当前的结果对象转换到指定泛型的结果类对象，直接返回指定泛型的失败结果类对象<br />
		/// Convert the current result object to the result class object of the specified generic type, 
		/// and directly return the result class object of the specified generic type failure
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <typeparam name="T9">泛型参数九</typeparam>
		/// <typeparam name="T10">泛型参数十</typeparam>
		/// <returns>最终失败的结果类对象</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> ConvertFailed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
		{
			return CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<OperateResult> func)
		{
			return IsSuccess ? func() : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T> Then<T>(Func<OperateResult<T>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2> Then<T1, T2>(Func<OperateResult<T1, T2>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3> Then<T1, T2, T3>(Func<OperateResult<T1, T2, T3>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4> Then<T1, T2, T3, T4>(Func<OperateResult<T1, T2, T3, T4>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4, T5> Then<T1, T2, T3, T4, T5>(Func<OperateResult<T1, T2, T3, T4, T5>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4, T5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6> Then<T1, T2, T3, T4, T5, T6>(Func<OperateResult<T1, T2, T3, T4, T5, T6>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4, T5, T6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7> Then<T1, T2, T3, T4, T5, T6, T7>(Func<OperateResult<T1, T2, T3, T4, T5, T6, T7>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> Then<T1, T2, T3, T4, T5, T6, T7, T8>(Func<OperateResult<T1, T2, T3, T4, T5, T6, T7, T8>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <typeparam name="T9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> Then<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="T1">泛型参数一</typeparam>
		/// <typeparam name="T2">泛型参数二</typeparam>
		/// <typeparam name="T3">泛型参数三</typeparam>
		/// <typeparam name="T4">泛型参数四</typeparam>
		/// <typeparam name="T5">泛型参数五</typeparam>
		/// <typeparam name="T6">泛型参数六</typeparam>
		/// <typeparam name="T7">泛型参数七</typeparam>
		/// <typeparam name="T8">泛型参数八</typeparam>
		/// <typeparam name="T9">泛型参数九</typeparam>
		/// <typeparam name="T10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Then<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Func<OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>> func)
		{
			return IsSuccess ? func() : CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(this);
		}

		/// <inheritdoc cref="M:HslCommunication.OperateResult.CreateFailedResult``1(System.String,HslCommunication.OperateResult)" />
		public static OperateResult<T> CreateFailedResult<T>(OperateResult result)
		{
			return new OperateResult<T>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T">目标数据类型</typeparam>
		/// <param name="msgHead">额外添加的消息头信息</param>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T> CreateFailedResult<T>(string msgHead, OperateResult result)
		{
			return new OperateResult<T>
			{
				ErrorCode = result.ErrorCode,
				Message = msgHead + " : " + result.Message
			};
		}

		/// <inheritdoc cref="M:HslCommunication.OperateResult.CreateFailedResult``2(System.String,HslCommunication.OperateResult)" />
		public static OperateResult<T1, T2> CreateFailedResult<T1, T2>(OperateResult result)
		{
			return new OperateResult<T1, T2>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <param name="msgHead">额外添加的消息头信息</param>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2> CreateFailedResult<T1, T2>(string msgHead, OperateResult result)
		{
			return new OperateResult<T1, T2>
			{
				ErrorCode = result.ErrorCode,
				Message = msgHead + " : " + result.Message
			};
		}

		/// <inheritdoc cref="M:HslCommunication.OperateResult.CreateFailedResult``3(System.String,HslCommunication.OperateResult)" />
		public static OperateResult<T1, T2, T3> CreateFailedResult<T1, T2, T3>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <param name="msgHead">额外添加的消息头信息</param>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3> CreateFailedResult<T1, T2, T3>(string msgHead, OperateResult result)
		{
			return new OperateResult<T1, T2, T3>
			{
				ErrorCode = result.ErrorCode,
				Message = msgHead + " : " + result.Message
			};
		}

		/// <inheritdoc cref="M:HslCommunication.OperateResult.CreateFailedResult``4(System.String,HslCommunication.OperateResult)" />
		public static OperateResult<T1, T2, T3, T4> CreateFailedResult<T1, T2, T3, T4>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <param name="msgHead">额外添加的消息头信息</param>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4> CreateFailedResult<T1, T2, T3, T4>(string msgHead, OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4>
			{
				ErrorCode = result.ErrorCode,
				Message = msgHead + " : " + result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <typeparam name="T5">目标数据类型五</typeparam>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4, T5> CreateFailedResult<T1, T2, T3, T4, T5>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4, T5>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <typeparam name="T5">目标数据类型五</typeparam>
		/// <typeparam name="T6">目标数据类型六</typeparam>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6> CreateFailedResult<T1, T2, T3, T4, T5, T6>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <typeparam name="T5">目标数据类型五</typeparam>
		/// <typeparam name="T6">目标数据类型六</typeparam>
		/// <typeparam name="T7">目标数据类型七</typeparam>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7> CreateFailedResult<T1, T2, T3, T4, T5, T6, T7>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <typeparam name="T5">目标数据类型五</typeparam>
		/// <typeparam name="T6">目标数据类型六</typeparam>
		/// <typeparam name="T7">目标数据类型七</typeparam>
		/// <typeparam name="T8">目标数据类型八</typeparam>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <typeparam name="T5">目标数据类型五</typeparam>
		/// <typeparam name="T6">目标数据类型六</typeparam>
		/// <typeparam name="T7">目标数据类型七</typeparam>
		/// <typeparam name="T8">目标数据类型八</typeparam>
		/// <typeparam name="T9">目标数据类型九</typeparam>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个失败的结果对象，该对象复制另一个结果对象的错误信息
		/// </summary>
		/// <typeparam name="T1">目标数据类型一</typeparam>
		/// <typeparam name="T2">目标数据类型二</typeparam>
		/// <typeparam name="T3">目标数据类型三</typeparam>
		/// <typeparam name="T4">目标数据类型四</typeparam>
		/// <typeparam name="T5">目标数据类型五</typeparam>
		/// <typeparam name="T6">目标数据类型六</typeparam>
		/// <typeparam name="T7">目标数据类型七</typeparam>
		/// <typeparam name="T8">目标数据类型八</typeparam>
		/// <typeparam name="T9">目标数据类型九</typeparam>
		/// <typeparam name="T10">目标数据类型十</typeparam>
		/// <param name="result">之前的结果对象</param>
		/// <returns>带默认泛型对象的失败结果类</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(OperateResult result)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
			{
				ErrorCode = result.ErrorCode,
				Message = result.Message
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象
		/// </summary>
		/// <returns>成功的结果对象</returns>
		public static OperateResult CreateSuccessResult()
		{
			return new OperateResult
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有一个参数对象
		/// </summary>
		/// <typeparam name="T">参数类型</typeparam>
		/// <param name="value">类型的值对象</param>
		/// <returns>成功的结果对象</returns>
		public static OperateResult<T> CreateSuccessResult<T>(T value)
		{
			return new OperateResult<T>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content = value
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有两个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2> CreateSuccessResult<T1, T2>(T1 value1, T2 value2)
		{
			return new OperateResult<T1, T2>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有三个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3> CreateSuccessResult<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
		{
			return new OperateResult<T1, T2, T3>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有四个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4> CreateSuccessResult<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
		{
			return new OperateResult<T1, T2, T3, T4>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有五个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <typeparam name="T5">第五个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <param name="value5">类型五对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4, T5> CreateSuccessResult<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
		{
			return new OperateResult<T1, T2, T3, T4, T5>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4,
				Content5 = value5
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有六个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <typeparam name="T5">第五个参数类型</typeparam>
		/// <typeparam name="T6">第六个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <param name="value5">类型五对象</param>
		/// <param name="value6">类型六对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6> CreateSuccessResult<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4,
				Content5 = value5,
				Content6 = value6
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有七个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <typeparam name="T5">第五个参数类型</typeparam>
		/// <typeparam name="T6">第六个参数类型</typeparam>
		/// <typeparam name="T7">第七个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <param name="value5">类型五对象</param>
		/// <param name="value6">类型六对象</param>
		/// <param name="value7">类型七对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7> CreateSuccessResult<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4,
				Content5 = value5,
				Content6 = value6,
				Content7 = value7
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有八个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <typeparam name="T5">第五个参数类型</typeparam>
		/// <typeparam name="T6">第六个参数类型</typeparam>
		/// <typeparam name="T7">第七个参数类型</typeparam>
		/// <typeparam name="T8">第八个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <param name="value5">类型五对象</param>
		/// <param name="value6">类型六对象</param>
		/// <param name="value7">类型七对象</param>
		/// <param name="value8">类型八对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> CreateSuccessResult<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4,
				Content5 = value5,
				Content6 = value6,
				Content7 = value7,
				Content8 = value8
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有九个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <typeparam name="T5">第五个参数类型</typeparam>
		/// <typeparam name="T6">第六个参数类型</typeparam>
		/// <typeparam name="T7">第七个参数类型</typeparam>
		/// <typeparam name="T8">第八个参数类型</typeparam>
		/// <typeparam name="T9">第九个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <param name="value5">类型五对象</param>
		/// <param name="value6">类型六对象</param>
		/// <param name="value7">类型七对象</param>
		/// <param name="value8">类型八对象</param>
		/// <param name="value9">类型九对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> CreateSuccessResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4,
				Content5 = value5,
				Content6 = value6,
				Content7 = value7,
				Content8 = value8,
				Content9 = value9
			};
		}

		/// <summary>
		/// 创建并返回一个成功的结果对象，并带有十个参数对象
		/// </summary>
		/// <typeparam name="T1">第一个参数类型</typeparam>
		/// <typeparam name="T2">第二个参数类型</typeparam>
		/// <typeparam name="T3">第三个参数类型</typeparam>
		/// <typeparam name="T4">第四个参数类型</typeparam>
		/// <typeparam name="T5">第五个参数类型</typeparam>
		/// <typeparam name="T6">第六个参数类型</typeparam>
		/// <typeparam name="T7">第七个参数类型</typeparam>
		/// <typeparam name="T8">第八个参数类型</typeparam>
		/// <typeparam name="T9">第九个参数类型</typeparam>
		/// <typeparam name="T10">第十个参数类型</typeparam>
		/// <param name="value1">类型一对象</param>
		/// <param name="value2">类型二对象</param>
		/// <param name="value3">类型三对象</param>
		/// <param name="value4">类型四对象</param>
		/// <param name="value5">类型五对象</param>
		/// <param name="value6">类型六对象</param>
		/// <param name="value7">类型七对象</param>
		/// <param name="value8">类型八对象</param>
		/// <param name="value9">类型九对象</param>
		/// <param name="value10">类型十对象</param>
		/// <returns>成的结果对象</returns>
		public static OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> CreateSuccessResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10)
		{
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
			{
				IsSuccess = true,
				ErrorCode = 0,
				Message = StringResources.Language.SuccessText,
				Content1 = value1,
				Content2 = value2,
				Content3 = value3,
				Content4 = value4,
				Content5 = value5,
				Content6 = value6,
				Content7 = value7,
				Content8 = value8,
				Content9 = value9,
				Content10 = value10
			};
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带一个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T">泛型类</typeparam>
	public class OperateResult<T> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据
		/// </summary>
		public T Content { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T> Check(Func<T, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content))
			{
				return this;
			}
			return new OperateResult<T>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T> Check(Func<T, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T, OperateResult> func)
		{
			return base.IsSuccess ? func(Content) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带两个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	public class OperateResult<T1, T2> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">可以自由指定的错误信息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2> Check(Func<T1, T2, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2))
			{
				return this;
			}
			return new OperateResult<T1, T2>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2> Check(Func<T1, T2, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带三个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	public class OperateResult<T1, T2, T3> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3> Check(Func<T1, T2, T3, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3> Check(Func<T1, T2, T3, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带四个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4> Check(Func<T1, T2, T3, T4, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4> Check(Func<T1, T2, T3, T4, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带五个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	/// <typeparam name="T5">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4, T5> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T5 Content5 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5> Check(Func<T1, T2, T3, T4, T5, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4, Content5))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4, T5>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5> Check(Func<T1, T2, T3, T4, T5, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4, Content5);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4, T5>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, T5, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, T5, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, T5, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带六个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	/// <typeparam name="T5">泛型类</typeparam>
	/// <typeparam name="T6">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4, T5, T6> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T5 Content5 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T6 Content6 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6> Check(Func<T1, T2, T3, T4, T5, T6, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4, Content5, Content6))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4, T5, T6>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6> Check(Func<T1, T2, T3, T4, T5, T6, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4, Content5, Content6);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4, T5, T6>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, T5, T6, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, T5, T6, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带七个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	/// <typeparam name="T5">泛型类</typeparam>
	/// <typeparam name="T6">泛型类</typeparam>
	/// <typeparam name="T7">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4, T5, T6, T7> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T5 Content5 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据6
		/// </summary>
		public T6 Content6 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据7
		/// </summary>
		public T7 Content7 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7> Check(Func<T1, T2, T3, T4, T5, T6, T7, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4, Content5, Content6, Content7))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7> Check(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4, Content5, Content6, Content7);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4, T5, T6, T7>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, T5, T6, T7, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带八个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	/// <typeparam name="T5">泛型类</typeparam>
	/// <typeparam name="T6">泛型类</typeparam>
	/// <typeparam name="T7">泛型类</typeparam>
	/// <typeparam name="T8">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T5 Content5 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据6
		/// </summary>
		public T6 Content6 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据7
		/// </summary>
		public T7 Content7 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据8
		/// </summary>
		public T8 Content8 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> Check(Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8> Check(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, T5, T6, T7, T8, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带九个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	/// <typeparam name="T5">泛型类</typeparam>
	/// <typeparam name="T6">泛型类</typeparam>
	/// <typeparam name="T7">泛型类</typeparam>
	/// <typeparam name="T8">泛型类</typeparam>
	/// <typeparam name="T9">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T5 Content5 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据6
		/// </summary>
		public T6 Content6 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据7
		/// </summary>
		public T7 Content7 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据8
		/// </summary>
		public T8 Content8 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据9
		/// </summary>
		public T9 Content9 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> Check(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9> Check(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
	/// <summary>
	/// 操作结果的泛型类，允许带十个用户自定义的泛型对象，推荐使用这个类
	/// </summary>
	/// <typeparam name="T1">泛型类</typeparam>
	/// <typeparam name="T2">泛型类</typeparam>
	/// <typeparam name="T3">泛型类</typeparam>
	/// <typeparam name="T4">泛型类</typeparam>
	/// <typeparam name="T5">泛型类</typeparam>
	/// <typeparam name="T6">泛型类</typeparam>
	/// <typeparam name="T7">泛型类</typeparam>
	/// <typeparam name="T8">泛型类</typeparam>
	/// <typeparam name="T9">泛型类</typeparam>
	/// <typeparam name="T10">泛型类</typeparam>
	public class OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : OperateResult
	{
		/// <summary>
		/// 用户自定义的泛型数据1
		/// </summary>
		public T1 Content1 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据2
		/// </summary>
		public T2 Content2 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据3
		/// </summary>
		public T3 Content3 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据4
		/// </summary>
		public T4 Content4 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据5
		/// </summary>
		public T5 Content5 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据6
		/// </summary>
		public T6 Content6 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据7
		/// </summary>
		public T7 Content7 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据8
		/// </summary>
		public T8 Content8 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据9
		/// </summary>
		public T9 Content9 { get; set; }

		/// <summary>
		/// 用户自定义的泛型数据10
		/// </summary>
		public T10 Content10 { get; set; }

		/// <summary>
		/// 实例化一个默认的结果对象
		/// </summary>
		public OperateResult()
		{
		}

		/// <summary>
		/// 使用指定的消息实例化一个默认的结果对象
		/// </summary>
		/// <param name="msg">错误消息</param>
		public OperateResult(string msg)
			: base(msg)
		{
		}

		/// <summary>
		/// 使用错误代码，消息文本来实例化对象
		/// </summary>
		/// <param name="err">错误代码</param>
		/// <param name="msg">错误消息</param>
		public OperateResult(int err, string msg)
			: base(err, msg)
		{
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <param name="message">检查失败的错误消息</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Check(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, bool> check, string message = "All content data check failed")
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			if (check(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10))
			{
				return this;
			}
			return new OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(message);
		}

		/// <summary>
		/// 返回一个检查结果对象，可以进行自定义的数据检查。<br />
		/// Returns a check result object that allows you to perform custom data checks.
		/// </summary>
		/// <param name="check">检查的委托方法</param>
		/// <returns>如果检查成功，则返回对象本身，如果失败，返回错误信息。</returns>
		public OperateResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Check(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult> check)
		{
			if (!base.IsSuccess)
			{
				return this;
			}
			OperateResult operateResult = check(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(operateResult);
			}
			return this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult Then(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : this;
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。
		/// </summary>
		/// <typeparam name="TResult">泛型参数</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult> Then<TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2> Then<TResult1, TResult2>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3> Then<TResult1, TResult2, TResult3>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4> Then<TResult1, TResult2, TResult3, TResult4>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5> Then<TResult1, TResult2, TResult3, TResult4, TResult5>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9>(this);
		}

		/// <summary>
		/// 指定接下来要做的是内容，当前对象如果成功，就返回接下来的执行结果，如果失败，就返回当前对象本身。<br />
		/// Specify what you want to do next, return the result of the execution of the current object if it succeeds, and return the current object itself if it fails.
		/// </summary>
		/// <typeparam name="TResult1">泛型参数一</typeparam>
		/// <typeparam name="TResult2">泛型参数二</typeparam>
		/// <typeparam name="TResult3">泛型参数三</typeparam>
		/// <typeparam name="TResult4">泛型参数四</typeparam>
		/// <typeparam name="TResult5">泛型参数五</typeparam>
		/// <typeparam name="TResult6">泛型参数六</typeparam>
		/// <typeparam name="TResult7">泛型参数七</typeparam>
		/// <typeparam name="TResult8">泛型参数八</typeparam>
		/// <typeparam name="TResult9">泛型参数九</typeparam>
		/// <typeparam name="TResult10">泛型参数十</typeparam>
		/// <param name="func">等待当前对象成功后执行的内容</param>
		/// <returns>返回整个方法链最终的成功失败结果</returns>
		public OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10> Then<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, OperateResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>> func)
		{
			return base.IsSuccess ? func(Content1, Content2, Content3, Content4, Content5, Content6, Content7, Content8, Content9, Content10) : OperateResult.CreateFailedResult<TResult1, TResult2, TResult3, TResult4, TResult5, TResult6, TResult7, TResult8, TResult9, TResult10>(this);
		}
	}
}
