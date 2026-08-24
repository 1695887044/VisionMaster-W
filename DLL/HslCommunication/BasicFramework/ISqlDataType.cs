using System.Data.SqlClient;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 数据库对应类的读取接口
	/// </summary>
	public interface ISqlDataType
	{
		/// <summary>
		/// 根据sdr对象初始化数据的方法
		/// </summary>
		/// <param name="sdr">数据库reader对象</param>
		void LoadBySqlDataReader(SqlDataReader sdr);
	}
}
