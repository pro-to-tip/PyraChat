﻿using System;
using System.Text.RegularExpressions;

namespace Pyratron.PyraChat.IRC
{
    public class User
    {
        /// <summary>
        /// Current nickname.
        /// </summary>
        public string Nick { get; private set; }

        /// <summary>
        /// Real name.
        /// </summary>
        public string RealName { get; private set; }

        /// <summary>
        /// Ident (username).
        /// </summary>
        public string Ident { get; private set; }
        public string Host { get; private set; }
        public string Mode { get; private set; }

        private static readonly Regex maskRegex = new Regex(@"([a-z0-9_\-\[\]\\`|^{}]+)!([a-z0-9_\-\~]+)\@([a-z0-9\.\-]+)", RegexOptions.IgnoreCase);

        public User(string mask)
        {
            var match = maskRegex.Match(mask);
            if (!match.Success) return;
            Nick = match.Groups[1].Value;
            Ident = match.Groups[2].Value;
            Host = match.Groups[3].Value;
        }

        public User(string nick, string realname, string ident, string hostname = "")
        {
            Nick = nick;
            RealName = realname;
            Ident = ident;
            Host = hostname;
        }

        public User(Client client, string nick)
        {
            Nick = nick;
        }
    }
}