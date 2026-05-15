namespace HslCommunication.Core
{
	/// <summary>
	/// 大端顺序的字节的转换类，字节的顺序和C#的原生字节的顺序是完全相反的，高字节在前，低字节在后。<br />
	/// In the reverse byte order conversion class, the byte order is completely opposite to the native byte order of C#, 
	/// with the high byte first and the low byte following.
	/// </summary>
	/// <remarks>
	/// 适用西门子PLC的S7协议的数据转换
	/// </remarks>
	public class ReverseBytesTransform : RegularByteTransform
	{
		/// <inheritdoc cref="M:HslCommunication.Core.RegularByteTransform.#ctor" />
		public ReverseBytesTransform()
		{
			base.DataFormat = DataFormat.ABCD;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.RegularByteTransform.#ctor(HslCommunication.Core.DataFormat)" />
		public ReverseBytesTransform(DataFormat dataFormat)
			: base(dataFormat)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IByteTransform.CreateByDateFormat(HslCommunication.Core.DataFormat)" />
		public override IByteTransform CreateByDateFormat(DataFormat dataFormat)
		{
			return new ReverseBytesTransform(dataFormat)
			{
				IsStringReverseByteWord = base.IsStringReverseByteWord
			};
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ReverseBytesTransform[{base.DataFormat}]";
		}
	}
}
