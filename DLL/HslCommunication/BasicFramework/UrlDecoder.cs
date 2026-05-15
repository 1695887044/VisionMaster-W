using System.Text;

namespace HslCommunication.BasicFramework
{
	internal class UrlDecoder
	{
		private int _bufferSize;

		private int _numChars;

		private char[] _charBuffer;

		private int _numBytes;

		private byte[] _byteBuffer;

		private Encoding _encoding;

		private void FlushBytes()
		{
			if (_numBytes > 0)
			{
				_numChars += _encoding.GetChars(_byteBuffer, 0, _numBytes, _charBuffer, _numChars);
				_numBytes = 0;
			}
		}

		internal UrlDecoder(int bufferSize, Encoding encoding)
		{
			_bufferSize = bufferSize;
			_encoding = encoding;
			_charBuffer = new char[bufferSize];
		}

		internal void AddChar(char ch)
		{
			if (_numBytes > 0)
			{
				FlushBytes();
			}
			_charBuffer[_numChars++] = ch;
		}

		internal void AddByte(byte b)
		{
			if (_byteBuffer == null)
			{
				_byteBuffer = new byte[_bufferSize];
			}
			_byteBuffer[_numBytes++] = b;
		}

		internal string GetString()
		{
			if (_numBytes > 0)
			{
				FlushBytes();
			}
			if (_numChars > 0)
			{
				return new string(_charBuffer, 0, _numChars);
			}
			return string.Empty;
		}
	}
}
