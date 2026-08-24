using System.Reflection;

namespace HslCommunication.Reflection
{
	internal class PropertyInfoHashKeyName : PropertyInfoKeyName
	{
		public string Field { get; set; }

		public PropertyInfoHashKeyName(PropertyInfo property, string key, string field)
			: base(property, key)
		{
			Field = field;
		}

		public PropertyInfoHashKeyName(PropertyInfo property, string key, string field, string value)
			: base(property, key, value)
		{
			Field = field;
		}
	}
}
