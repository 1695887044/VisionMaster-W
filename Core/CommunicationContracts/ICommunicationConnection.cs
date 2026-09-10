using System;

namespace VisionMaster.Communications
{

    public interface ICommunicationConnection : IDisposable
    {

        string ConnectionName { get; }

        CommunicationType Type { get; }

        bool IsConnected { get; }


        bool Connect();

        void Disconnect();


        bool TestConnection();


        T Read<T>(string address) where T : struct;

        void Write(string address, object value);

        byte[] ReadBytes(string address, ushort length);

        void WriteBytes(string address, byte[] data);
    }
}
