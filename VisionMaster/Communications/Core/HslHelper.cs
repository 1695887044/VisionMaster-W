using HslCommunication;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// HslCommunication 辅助类，提供数据类型转换功能
    /// </summary>
    internal static class HslHelper
    {
        /// <summary>
        /// 将字节数组转换为指定类型
        /// </summary>
        public static T ConvertTo<T>(byte[] data) where T : struct
        {
            if (data == null || data.Length < 2)
                return default;

            var typeCode = Type.GetTypeCode(typeof(T));
            return typeCode switch
            {
                TypeCode.Boolean => (T)(object)(data[0] != 0),
                TypeCode.Byte => (T)(object)data[0],
                TypeCode.Int16 => (T)(object)BitConverter.ToInt16(data, 0),
                TypeCode.UInt16 => (T)(object)BitConverter.ToUInt16(data, 0),
                TypeCode.Int32 => (T)(object)BitConverter.ToInt32(data, 0),
                TypeCode.UInt32 => (T)(object)BitConverter.ToUInt32(data, 0),
                TypeCode.Single => (T)(object)BitConverter.ToSingle(data, 0),
                TypeCode.Double => (T)(object)BitConverter.ToDouble(data, 0),
                _ => default
            };
        }

        /// <summary>
        /// 将对象值转换为字节数组
        /// </summary>
        public static byte[] GetValueArray(object value)
        {
            return value switch
            {
                bool b => new[] { (byte)(b ? 1 : 0) },
                byte b => new[] { b },
                short s => BitConverter.GetBytes(s),
                ushort us => BitConverter.GetBytes(us),
                int i => BitConverter.GetBytes(i),
                uint ui => BitConverter.GetBytes(ui),
                float f => BitConverter.GetBytes(f),
                double d => BitConverter.GetBytes(d),
                string s => System.Text.Encoding.ASCII.GetBytes(s),
                _ => BitConverter.GetBytes(Convert.ToInt64(value))
            };
        }
    }
}