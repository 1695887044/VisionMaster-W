using System.Reflection;

namespace HslCommunication.Reflection
{
	internal class PropertyInfoKeyName
	{
		public PropertyInfo PropertyInfo { get; set; }

		public string KeyName { get; set; }

		public string Value { get; set; }

		public PropertyInfoKeyName(PropertyInfo property, string key)
		{
			PropertyInfo = property;
			KeyName = key;
		}

		public PropertyInfoKeyName(PropertyInfo property, string key, string value)
		{
			PropertyInfo = property;
			KeyName = key;
			Value = value;
		}
	}
}
