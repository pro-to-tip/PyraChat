﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Pyratron.PyraChat.IRC
{
    /// <summary>
    /// User's rank.
    /// </summary>
    public sealed class UserRank : IComparable
    {
        public static readonly UserRank None = new UserRank('\0');
        public static readonly UserRank Voice = new UserRank('+');
        public static readonly UserRank HalfOp = new UserRank('%');
        public static readonly UserRank Op = new UserRank('@');
        public static readonly UserRank Admin = new UserRank('&');
        public static readonly UserRank Owner = new UserRank('~');
        private static List<UserRank> types;
        public char Prefix { get; }

        private UserRank(char prefix)
        {
            if (types == null)
                types = new List<UserRank>();
            Prefix = prefix;
            types.Add(this);
        }

        public int CompareTo(object obj)
        {
            var other = obj as UserRank;
            if (other == null)
                return -1;
            return types.IndexOf(other).CompareTo(types.IndexOf(this));
        }

        public override string ToString() => Prefix.ToString();

        public static UserRank FromPrefix(char c)
        {
            var type = types.FirstOrDefault(t => t.Prefix.Equals(c));
            return type ?? None;
        }
    }
}