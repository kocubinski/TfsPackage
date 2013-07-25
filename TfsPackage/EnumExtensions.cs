using System;

namespace TfsPackage
{
    public static class EnumExtensions
    {
        static public bool HasFlag(this Enum eThis, Enum eFlag)
        {
            if (!eThis.GetType().IsInstanceOfType(eFlag)) {
                throw new ArgumentException("Flag is not of same type as enum");
            }

            var uFlag = Convert.ToUInt64(eFlag);
            var uThis = Convert.ToUInt64(eThis);
            var res = ((uThis & uFlag) == uFlag);
            return res;
        }

        static public bool HasOnlyFlag(this Enum eThis, Enum eFlag)
        {
            if (!eThis.GetType().IsInstanceOfType(eFlag)) {
                throw new ArgumentException("Flag is not of same type as enum");
            }

            var uFlag = Convert.ToUInt64(eFlag);
            var uThis = Convert.ToUInt64(eThis);
            return ((uThis ^ uFlag) == 0); 
        }
    }
}