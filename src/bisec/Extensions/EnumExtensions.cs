using System;
using System.Reflection;

namespace BiSec.Library.Extensions
{
    public static class EnumExtensions
    {
        public static T GetAttribute<T>(this Enum enumItem) where T : Attribute
        {
            var type = enumItem.GetType();
            var memberInfo = type.GetMember(Enum.GetName(type, enumItem));
            var attribute = memberInfo[0].GetCustomAttribute(typeof(T), false);

            return attribute as T;
        }
    }
}
