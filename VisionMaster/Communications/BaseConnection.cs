using HslCommunication;
using System;

namespace VisionMaster.Communications
{
    public abstract class BaseConnection<TDevice> : ICommunicationConnection where TDevice : class, IDisposable
    {
        protected TDevice? _device;
        protected bool _isConnected = false;
        protected readonly object _lock = new();

        public abstract string ConnectionName { get; set; }
        public abstract CommunicationType Type { get; }
        public abstract ConnectionConfigBase? Config { get; protected set; }
        public bool IsConnected => _isConnected && _device != null;

        protected abstract void InitializeDevice();
        protected abstract OperateResult ConnectServer();
        protected abstract void CloseConnection();
        protected abstract OperateResult<bool> ReadBool(string address);
        protected abstract OperateResult<short> ReadInt16(string address);
        protected abstract OperateResult<ushort> ReadUInt16(string address);
        protected abstract OperateResult<int> ReadInt32(string address);
        protected abstract OperateResult<uint> ReadUInt32(string address);
        protected abstract OperateResult<float> ReadFloat(string address);
        protected abstract OperateResult<double> ReadDouble(string address);
        protected abstract OperateResult<byte[]> ReadBytesCore(string address, ushort length);
        protected abstract OperateResult WriteBool(string address, bool value);
        protected abstract OperateResult WriteInt16(string address, short value);
        protected abstract OperateResult WriteUInt16(string address, ushort value);
        protected abstract OperateResult WriteInt32(string address, int value);
        protected abstract OperateResult WriteUInt32(string address, uint value);
        protected abstract OperateResult WriteFloat(string address, float value);
        protected abstract OperateResult WriteDouble(string address, double value);
        protected abstract OperateResult WriteBytesCore(string address, byte[] data);

        public bool Connect()
        {
            lock (_lock)
            {
                if (_isConnected) return true;
                try
                {
                    InitializeDevice();
                    var result = ConnectServer();
                    _isConnected = result.IsSuccess;
                    return _isConnected;
                }
                catch
                {
                    _isConnected = false;
                    return false;
                }
            }
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                CloseConnection();
                _isConnected = false;
            }
        }

        public bool TestConnection()
        {
            var result = Connect();
            Disconnect();
            return result;
        }

        public T? Read<T>(string address)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");

            return typeof(T).Name switch
            {
                nameof(Boolean) => (T)(object)ReadBool(address).Content,
                nameof(Int16) => (T)(object)ReadInt16(address).Content,
                nameof(UInt16) => (T)(object)ReadUInt16(address).Content,
                nameof(Int32) => (T)(object)ReadInt32(address).Content,
                nameof(UInt32) => (T)(object)ReadUInt32(address).Content,
                nameof(Single) => (T)(object)ReadFloat(address).Content,
                nameof(Double) => (T)(object)ReadDouble(address).Content,
                nameof(Byte) => (T)(object)ReadBytesCore(address, 1).Content[0],
                _ => default
            };
        }

        public byte[] ReadBytes(string address, ushort length)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");
            var result = ReadBytesCore(address, length);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return result.Content;
        }

        public void Write(string address, object value)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");

            OperateResult result = value switch
            {
                bool b => WriteBool(address, b),
                byte b => WriteBytesCore(address, new[] { b }),
                short s => WriteInt16(address, s),
                ushort us => WriteUInt16(address, us),
                int i => WriteInt32(address, i),
                uint ui => WriteUInt32(address, ui),
                float f => WriteFloat(address, f),
                double d => WriteDouble(address, d),
                _ => throw new NotSupportedException($"不支持的类型: {value.GetType()}")
            };

            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }

        public void WriteBytes(string address, byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");
            var result = WriteBytesCore(address, data);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }

        public void Dispose()
        {
            Disconnect();
            _device?.Dispose();
        }
    }
}
