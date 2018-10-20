﻿namespace System.Reflection
{
	static class SystemReflectionExtensions
	{
		public static bool IsDescendantOf(this Type type, Type ancestor)
		{
			while (type != null)
			{
				if (type == ancestor)
					return true;

				type = type.GetTypeInfo().BaseType;
			}

			return false;
		}
	}
}