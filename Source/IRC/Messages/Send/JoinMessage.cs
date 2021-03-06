﻿using System.IO;

namespace Pyratron.PyraChat.IRC.Messages.Send
{
    /// <summary>
    /// Join message.
    /// </summary>
    /// <see cref="http://tools.ietf.org/html/rfc2812#section-3.2.1" />
    public class JoinMessage : SendableMessage
    {
        public string[] Channels { get; }

        public JoinMessage(string channel)
        {
            Channels = new[] {channel};
        }

        public JoinMessage(string[] channels)
        {
            Channels = channels;
        }

        public void Send(StreamWriter writer, Client client)
        {
            writer.WriteLine($"JOIN {string.Join(",", Channels)}");
        }
    }
}